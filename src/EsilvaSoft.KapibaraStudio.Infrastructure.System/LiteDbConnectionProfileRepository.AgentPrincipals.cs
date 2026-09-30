using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using LiteDB;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>
/// Channel identity facet of the single LiteDB owner. LiteDB keeps only opaque ids, state and the OS-store
/// reference; the proof exists only in <see cref="ISecretStore"/>. Every OS-store call runs outside the gate and
/// is followed by a re-read under the gate, so a concurrent revocation always wins.
/// </summary>
public sealed partial class LiteDbConnectionProfileRepository : IAgentPrincipalAuthority
{
    private const string AgentChannelCollectionName = "agentChannels";
    private const int AgentChannelSchemaVersion = 1;
    private const int ProofByteLength = 32;
    private const int MaximumProofLength = 128;
    /// <summary>Fixed key of the in-process channel; its principal id is random per workspace.</summary>
    internal static readonly Guid InternalAgentChannelId = new("6f1c2b1e-0d1a-4c55-9a3e-5e7b0c8d2f10");
    private readonly HashSet<Guid> _activeChannelEnrollments = [];

    private enum AgentChannelState { Pending = 0, Active = 1, Revoked = 2 }

    /// <summary>
    /// Additive field (absent in older rows = <see cref="ExternalClient"/>): a per-session channel of an integrated
    /// provider (ADR-056) is never accepted as an external MCP client and is revoked as an orphan when no session owns it.
    /// </summary>
    private enum AgentChannelPurpose { ExternalClient = 0, Session = 1 }

    public Task<AgentPrincipalIssueResult> IssueInternalAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = GetOrCreateInternalChannel();
            return channel is null
                ? AgentPrincipalIssueResult.Denied(AgentPrincipalIssueStatus.Corrupt)
                : IssueFromPolicy(channel, AgentPrincipalOrigin.Internal);
        }, cancellationToken);

    public Task<Guid> GetInternalPrincipalIdAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return GetOrCreateInternalChannel()?.PrincipalId
                ?? throw new InvalidDataException("Registro de canal de agente ilegível; nada foi alterado.");
        }, cancellationToken);

    /// <summary>Additive first use; an existing unreadable row returns null and is never replaced.</summary>
    private AgentChannelDocument? GetOrCreateInternalChannel()
    {
        var channels = AgentChannels();
        var channel = channels.FindById(InternalAgentChannelId);
        if (channel is null)
        {
            channel = new AgentChannelDocument
            {
                Id = InternalAgentChannelId, PrincipalId = Guid.NewGuid(),
                Origin = (int)AgentPrincipalOrigin.Internal, State = (int)AgentChannelState.Active,
                CreatedAtUtcTicks = DateTime.UtcNow.Ticks
            };
            channels.Insert(channel);
            return channel;
        }
        return IsReadable(channel) && channel.Origin == (int)AgentPrincipalOrigin.Internal &&
               channel.State == (int)AgentChannelState.Active ? channel : null;
    }

    public Task<AgentChannelEnrollmentResult> EnrollExternalChannelAsync(CancellationToken cancellationToken = default) =>
        EnrollChannelAsync(AgentChannelPurpose.ExternalClient, cancellationToken);

    public Task<AgentChannelEnrollmentResult> EnrollSessionChannelAsync(CancellationToken cancellationToken = default) =>
        EnrollChannelAsync(AgentChannelPurpose.Session, cancellationToken);

    public async Task<int> RevokeOrphanSessionChannelsAsync(IReadOnlyCollection<Guid> activeChannelIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(activeChannelIds);
        var keep = activeChannelIds.ToHashSet();
        var orphans = await RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentChannels().FindAll()
                .Where(channel => IsReadable(channel) && channel.Purpose == (int)AgentChannelPurpose.Session &&
                    !keep.Contains(channel.Id) && !_activeChannelEnrollments.Contains(channel.Id) &&
                    (channel.State != (int)AgentChannelState.Revoked || channel.ProofCleanupPending))
                .Select(channel => channel.Id)
                .ToArray();
        }, cancellationToken).ConfigureAwait(false);
        var revoked = 0;
        foreach (var channelId in orphans)
        {
            // Revocation is durable first; a failed proof removal stays pending for RecoverPendingChannelsAsync.
            if (await RevokeExternalChannelAsync(channelId, CancellationToken.None).ConfigureAwait(false) !=
                AgentChannelRevocationStatus.UnknownChannel)
                revoked++;
        }
        return revoked;
    }

    private async Task<AgentChannelEnrollmentResult> EnrollChannelAsync(AgentChannelPurpose purpose,
        CancellationToken cancellationToken)
    {
        if (_profileSecrets is null)
            return new(AgentChannelEnrollmentStatus.CredentialStoreFailed, FailureCode: SecretStoreFailureCode.Unavailable);
        var channelId = Guid.NewGuid();
        var principalId = Guid.NewGuid();
        var reference = new SecretReference(Guid.NewGuid(), 1);
        await RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Durable intent before the OS store is touched: a crash leaves a Pending row for recovery.
            AgentChannels().Insert(new AgentChannelDocument
            {
                Id = channelId, PrincipalId = principalId, Origin = (int)AgentPrincipalOrigin.External,
                State = (int)AgentChannelState.Pending, ProofReferenceId = reference.Id, Purpose = (int)purpose,
                ProofReferenceVersion = reference.Version, CreatedAtUtcTicks = DateTime.UtcNow.Ticks
            });
            _activeChannelEnrollments.Add(channelId);
        }, cancellationToken).ConfigureAwait(false);

        AgentChannelEnrollmentResult failure;
        try
        {
            var proof = CreateProof();
            var written = await SetChannelProofAsync(reference, proof, cancellationToken).ConfigureAwait(false);
            if (!written.IsSuccess)
                failure = new(AgentChannelEnrollmentStatus.CredentialStoreFailed, FailureCode: written.Failure!.Code);
            else
            {
                var readback = await GetChannelProofAsync(reference, cancellationToken).ConfigureAwait(false);
                if (!readback.IsSuccess)
                    failure = new(AgentChannelEnrollmentStatus.CredentialStoreFailed, FailureCode: readback.Failure!.Code);
                else if (!ProofEquals(readback.Value, proof))
                    failure = new(AgentChannelEnrollmentStatus.VerificationFailed);
                else
                {
                    var activated = await RunAsync(() =>
                    {
                        var channel = AgentChannels().FindById(channelId);
                        if (channel is null || !IsReadable(channel) || channel.State != (int)AgentChannelState.Pending ||
                            channel.ProofReferenceId != reference.Id) return false;
                        channel.State = (int)AgentChannelState.Active;
                        return AgentChannels().Update(channel);
                    }, CancellationToken.None).ConfigureAwait(false);
                    if (activated)
                    {
                        await EndChannelEnrollmentAsync(channelId).ConfigureAwait(false);
                        return new(AgentChannelEnrollmentStatus.Enrolled, channelId, principalId, reference);
                    }
                    failure = new(AgentChannelEnrollmentStatus.VerificationFailed);
                }
            }
        }
        catch
        {
            await EndChannelEnrollmentAsync(channelId).ConfigureAwait(false);
            await AbandonChannelEnrollmentAsync(channelId, reference).ConfigureAwait(false);
            throw;
        }
        await EndChannelEnrollmentAsync(channelId).ConfigureAwait(false);
        await AbandonChannelEnrollmentAsync(channelId, reference).ConfigureAwait(false);
        return failure;
    }

    private Task<bool> EndChannelEnrollmentAsync(Guid channelId) =>
        RunAsync(() => _activeChannelEnrollments.Remove(channelId), CancellationToken.None);

    public async Task<AgentPrincipalIssueResult> AuthenticateExternalAsync(Guid channelId, string proof,
        CancellationToken cancellationToken = default)
    {
        if (channelId == Guid.Empty || channelId == InternalAgentChannelId)
            return AgentPrincipalIssueResult.Denied(AgentPrincipalIssueStatus.UnknownChannel);
        if (string.IsNullOrEmpty(proof) || proof.Length > MaximumProofLength)
            return AgentPrincipalIssueResult.Denied(AgentPrincipalIssueStatus.InvalidProof);
        var snapshot = await RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentChannels().FindById(channelId);
        }, cancellationToken).ConfigureAwait(false);
        var denial = ClassifyExternal(snapshot);
        if (denial is not null) return AgentPrincipalIssueResult.Denied(denial.Value);
        if (_profileSecrets is null)
            return AgentPrincipalIssueResult.Denied(AgentPrincipalIssueStatus.CredentialStoreFailed,
                SecretStoreFailureCode.Unavailable);

        var reference = new SecretReference(snapshot!.ProofReferenceId!.Value, snapshot.ProofReferenceVersion);
        var stored = await GetChannelProofAsync(reference, cancellationToken).ConfigureAwait(false);
        if (!stored.IsSuccess)
            return AgentPrincipalIssueResult.Denied(AgentPrincipalIssueStatus.CredentialStoreFailed, stored.Failure!.Code);
        if (!ProofEquals(stored.Value, proof))
            return AgentPrincipalIssueResult.Denied(AgentPrincipalIssueStatus.InvalidProof);

        return await RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = AgentChannels().FindById(channelId);
            var changed = ClassifyExternal(current);
            if (changed is not null) return AgentPrincipalIssueResult.Denied(changed.Value);
            if (current!.PrincipalId != snapshot.PrincipalId || current.ProofReferenceId != reference.Id ||
                current.ProofReferenceVersion != reference.Version)
                return AgentPrincipalIssueResult.Denied(AgentPrincipalIssueStatus.Revoked);
            return IssueFromPolicy(current, AgentPrincipalOrigin.External);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> IsCurrentAsync(AgentPrincipal principal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = AgentChannels().FindOne(Query.EQ(nameof(AgentChannelDocument.PrincipalId), principal.Id));
            if (channel is null || !IsReadable(channel) || channel.State != (int)AgentChannelState.Active ||
                channel.Origin != (int)principal.Origin) return false;
            try { return ReadAgentAuthorizationPolicy(principal.Id)?.Revision == principal.PolicyRevision; }
            catch (InvalidDataException) { return false; }
        }, cancellationToken);
    }

    public async Task<AgentChannelRevocationStatus> RevokeExternalChannelAsync(Guid channelId,
        CancellationToken cancellationToken = default)
    {
        var outcome = await RunAsync<(bool Known, SecretReference? Proof)>(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var channel = AgentChannels().FindById(channelId);
            if (channel is null || channel.Origin != (int)AgentPrincipalOrigin.External) return (false, null);
            EnsureReadableChannel(channel);
            if (channel.State == (int)AgentChannelState.Revoked && !channel.ProofCleanupPending) return (true, null);
            // Revocation is committed before the OS store is touched, so authentication fails from here on.
            channel.State = (int)AgentChannelState.Revoked;
            channel.ProofCleanupPending = channel.ProofReferenceId is not null;
            AgentChannels().Update(channel);
            return (true, channel.ProofReferenceId is { } id ? new SecretReference(id, channel.ProofReferenceVersion) : null);
        }, cancellationToken).ConfigureAwait(false);
        if (!outcome.Known) return AgentChannelRevocationStatus.UnknownChannel;
        if (outcome.Proof is null) return AgentChannelRevocationStatus.Revoked;
        return await RemoveChannelProofAsync(channelId, outcome.Proof, CancellationToken.None).ConfigureAwait(false)
            ? AgentChannelRevocationStatus.Revoked : AgentChannelRevocationStatus.RevokedCleanupPending;
    }

    public async Task<int> RecoverPendingChannelsAsync(CancellationToken cancellationToken = default)
    {
        var pending = await RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentChannels().FindAll()
                .Where(channel => IsReadable(channel) && channel.Origin == (int)AgentPrincipalOrigin.External &&
                    channel.ProofReferenceId is not null && !_activeChannelEnrollments.Contains(channel.Id) &&
                    (channel.State == (int)AgentChannelState.Pending || channel.ProofCleanupPending))
                .Select(channel => (channel.Id, Reference: new SecretReference(channel.ProofReferenceId!.Value,
                    channel.ProofReferenceVersion)))
                .ToArray();
        }, cancellationToken).ConfigureAwait(false);
        var remaining = 0;
        foreach (var (channelId, reference) in pending)
        {
            await RunAsync(() =>
            {
                var channel = AgentChannels().FindById(channelId);
                if (channel is null || !IsReadable(channel) || _activeChannelEnrollments.Contains(channelId) ||
                    channel.State == (int)AgentChannelState.Active) return;
                channel.State = (int)AgentChannelState.Revoked;
                channel.ProofCleanupPending = true;
                AgentChannels().Update(channel);
            }, cancellationToken).ConfigureAwait(false);
            if (!await RemoveChannelProofAsync(channelId, reference, cancellationToken).ConfigureAwait(false))
                remaining++;
        }
        return remaining;
    }

    private async Task AbandonChannelEnrollmentAsync(Guid channelId, SecretReference reference)
    {
        await RunAsync(() =>
        {
            var channel = AgentChannels().FindById(channelId);
            if (channel is null || !IsReadable(channel) || channel.ProofReferenceId != reference.Id) return;
            channel.State = (int)AgentChannelState.Revoked;
            channel.ProofCleanupPending = true;
            AgentChannels().Update(channel);
        }, CancellationToken.None).ConfigureAwait(false);
        await RemoveChannelProofAsync(channelId, reference, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the OS-store proof only for a durably revoked row (Revoked is terminal, so the check cannot go stale)
    /// and clears the marker only when no enrollment is still in flight.
    /// </summary>
    private async Task<bool> RemoveChannelProofAsync(Guid channelId, SecretReference reference,
        CancellationToken cancellationToken)
    {
        if (_profileSecrets is null) return false;
        var revoked = await RunAsync(() => AgentChannels().FindById(channelId) is { } channel && IsReadable(channel) &&
            channel.ProofReferenceId == reference.Id && channel.State == (int)AgentChannelState.Revoked,
            cancellationToken).ConfigureAwait(false);
        if (!revoked || !await DeleteProfileSecretAsync(reference, cancellationToken).ConfigureAwait(false))
            return false;
        return await RunAsync(() =>
        {
            var channel = AgentChannels().FindById(channelId);
            if (channel is null || !IsReadable(channel) || channel.ProofReferenceId != reference.Id ||
                channel.State != (int)AgentChannelState.Revoked || _activeChannelEnrollments.Contains(channelId))
                return false;
            channel.ProofCleanupPending = false;
            return AgentChannels().Update(channel);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    private AgentPrincipalIssueResult IssueFromPolicy(AgentChannelDocument channel, AgentPrincipalOrigin origin)
    {
        AgentAuthorizationPolicySnapshot? policy;
        try { policy = ReadAgentAuthorizationPolicy(channel.PrincipalId); }
        catch (InvalidDataException) { return AgentPrincipalIssueResult.Denied(AgentPrincipalIssueStatus.Corrupt); }
        // No persisted policy means nothing is granted; there is no revision to bind, so no principal is issued.
        if (policy is null || !policy.IsValid || policy.Revision < 1)
            return AgentPrincipalIssueResult.Denied(AgentPrincipalIssueStatus.PolicyMissing);
        return AgentPrincipalIssueResult.Issued(new AgentPrincipal(channel.PrincipalId, origin, policy.Revision,
            isSessionChannel: origin == AgentPrincipalOrigin.External && channel.Purpose == (int)AgentChannelPurpose.Session));
    }

    private static AgentPrincipalIssueStatus? ClassifyExternal(AgentChannelDocument? channel)
    {
        if (channel is null || channel.Origin == (int)AgentPrincipalOrigin.Internal)
            return AgentPrincipalIssueStatus.UnknownChannel;
        if (!IsReadable(channel) || channel.Origin != (int)AgentPrincipalOrigin.External ||
            channel.ProofReferenceId is null)
            return AgentPrincipalIssueStatus.Corrupt;
        return channel.State switch
        {
            (int)AgentChannelState.Active => null,
            (int)AgentChannelState.Pending => AgentPrincipalIssueStatus.PendingEnrollment,
            _ => AgentPrincipalIssueStatus.Revoked
        };
    }

    /// <summary>A row from a newer schema or with invalid fields is reported, never rewritten.</summary>
    private static void EnsureReadableChannel(AgentChannelDocument channel)
    {
        if (!IsReadable(channel))
            throw new InvalidDataException("Registro de canal de agente ilegível; nada foi alterado.");
    }

    private static bool IsReadable(AgentChannelDocument channel) =>
        channel.SchemaVersion == AgentChannelSchemaVersion && channel.PrincipalId != Guid.Empty &&
        channel.State is >= (int)AgentChannelState.Pending and <= (int)AgentChannelState.Revoked &&
        channel.Origin is (int)AgentPrincipalOrigin.Internal or (int)AgentPrincipalOrigin.External &&
        (channel.Purpose == (int)AgentChannelPurpose.ExternalClient ||
         channel.Purpose == (int)AgentChannelPurpose.Session && channel.Origin == (int)AgentPrincipalOrigin.External) &&
        (channel.ProofReferenceId is null || (channel.ProofReferenceId != Guid.Empty && channel.ProofReferenceVersion >= 1));

    private ILiteCollection<AgentChannelDocument> AgentChannels()
    {
        var channels = _database.GetCollection<AgentChannelDocument>(AgentChannelCollectionName);
        channels.EnsureIndex(channel => channel.PrincipalId, unique: true);
        return channels;
    }

    private static string CreateProof()
    {
        Span<byte> bytes = stackalloc byte[ProofByteLength];
        RandomNumberGenerator.Fill(bytes);
        try { return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static bool ProofEquals(string stored, string presented)
    {
        var left = Encoding.UTF8.GetBytes(stored);
        var right = Encoding.UTF8.GetBytes(presented);
        try { return CryptographicOperations.FixedTimeEquals(left, right); }
        finally
        {
            CryptographicOperations.ZeroMemory(left);
            CryptographicOperations.ZeroMemory(right);
        }
    }

    private async Task<SecretStoreOperationResult> SetChannelProofAsync(SecretReference reference, string proof,
        CancellationToken cancellationToken)
    {
        try { return await _profileSecrets!.SetAsync(reference, proof, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable); }
    }

    private async Task<SecretStoreResult<string>> GetChannelProofAsync(SecretReference reference,
        CancellationToken cancellationToken)
    {
        try { return await _profileSecrets!.GetAsync(reference, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return SecretStoreResults.Failed<string>(SecretStoreFailureCode.Unavailable); }
    }

    private sealed class AgentChannelDocument
    {
        [BsonId] public Guid Id { get; init; }
        public int SchemaVersion { get; init; } = AgentChannelSchemaVersion;
        public Guid PrincipalId { get; init; }
        public int Origin { get; init; }
        public int State { get; set; }
        public Guid? ProofReferenceId { get; init; }
        public int ProofReferenceVersion { get; init; }
        public bool ProofCleanupPending { get; set; }
        public int Purpose { get; init; }
        public long CreatedAtUtcTicks { get; init; }
    }
}
