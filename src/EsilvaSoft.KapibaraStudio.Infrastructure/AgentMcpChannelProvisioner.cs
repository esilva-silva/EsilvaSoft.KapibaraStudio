using System.Globalization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>
/// Per-session local MCP channel of an integrated provider (ADR-056). Lazy: nothing is started at composition; the
/// broker opens its endpoint on the first session that needs product tools. Each session gets its own enrolled channel
/// and principal (proof only in the OS store, read by the proxy itself), an initial empty policy (so the principal can
/// be issued while nothing is granted) and a scope in <see cref="AgentMcpSessionRegistry"/>. Before each turn the
/// adapter binds the turn plan: grants are rewritten to exactly the planned connections and data scopes, and the
/// registry/broker expose exactly the planned tools. Closing the session revokes the channel durably.
/// </summary>
/// <remarks>
/// Separate from the user's opt-in for external MCP clients (<see cref="AgentBrokerOptions.Enabled"/>): when no
/// external broker was composed, this provisioner owns a broker that accepts only per-session channels.
/// </remarks>
public sealed class AgentMcpChannelProvisioner : IAgentMcpChannelProvisioner, IAsyncDisposable, IDisposable
{
    public const string ServerExecutableFolder = "mcp";
    public const string ServerExecutableName = "EsilvaSoft.KapibaraStudio.McpServer";

    private static readonly AgentOutputDestination McpDestination =
        AgentOutputDestination.McpExternal(AgentBrokerProtocol.McpProviderId);

    private readonly IAgentToolRegistry _registry;
    private readonly IAgentPrincipalAuthority _authority;
    private readonly IAgentAuthorizationPolicyRepository _policies;
    private readonly IConnectionProfileRepository _profiles;
    private readonly AgentMcpSessionRegistry _sessions;
    private readonly AgentToolExposureStage _stage;
    private readonly AgentBrokerHost? _externalBroker;
    private readonly string? _serverExecutable;
    private readonly bool _platformSupported;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<Guid, OpenSession> _open = [];
    private readonly Dictionary<Guid, Guid?> _pendingRevocations = [];
    private bool _orphansSwept;
    private AgentBrokerHost? _ownedBroker;
    private bool _disposed;

    /// <remarks>
    /// <c>externalBroker</c>: the opt-in external broker when composed, reused so there is one endpoint.
    /// <c>serverExecutable</c>: absolute proxy path; defaults to <c>mcp/EsilvaSoft.KapibaraStudio.McpServer[.exe]</c> next to
    /// the application. <c>platformSupported</c>: defaults to Windows, because the proxy reads its proof only from Windows
    /// Credential Manager today (Linux has no client transport credential store).
    /// </remarks>
    public AgentMcpChannelProvisioner(IAgentToolRegistry registry, IAgentPrincipalAuthority authority,
        IAgentAuthorizationPolicyRepository policies, IConnectionProfileRepository profiles,
        AgentMcpSessionRegistry sessions, AgentToolExposureStage stage, AgentBrokerHost? externalBroker = null,
        string? serverExecutable = null, bool? platformSupported = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        _policies = policies ?? throw new ArgumentNullException(nameof(policies));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _stage = stage;
        _externalBroker = externalBroker;
        _serverExecutable = serverExecutable ?? DefaultServerExecutable();
        _platformSupported = platformSupported ?? OperatingSystem.IsWindows();
    }

    public bool ProductToolsAvailable => _platformSupported && _stage != AgentToolExposureStage.None;

    /// <summary>Broker serving the session channels (null until the first session). Composition evidence for tests.</summary>
    internal AgentBrokerHost? Broker => _externalBroker ?? _ownedBroker;

    public static string DefaultServerExecutable() =>
        Path.Combine(AppContext.BaseDirectory, ServerExecutableFolder,
            ServerExecutableName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));

    public async Task<AgentMcpChannelProvisioning> OpenSessionAsync(string providerId, Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        if (conversationId == Guid.Empty) throw new ArgumentException("Conversa inválida.", nameof(conversationId));
        if (!_platformSupported) return new(AgentMcpChannelStatus.UnavailableOnPlatform);
        if (_stage == AgentToolExposureStage.None) return new(AgentMcpChannelStatus.ToolsNotReleased);
        if (string.IsNullOrWhiteSpace(_serverExecutable) || !Path.IsPathFullyQualified(_serverExecutable) ||
            !File.Exists(_serverExecutable))
            return new(AgentMcpChannelStatus.ServerExecutableMissing);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await RetryPendingRevocationsAsync().ConfigureAwait(false);
            var broker = await EnsureBrokerAsync(cancellationToken).ConfigureAwait(false);
            if (broker is null) return new(AgentMcpChannelStatus.BrokerUnavailable);
            if (!_orphansSwept)
            {
                // A reused external broker may have started before this process owned any session: sweep once here too.
                try
                {
                    await _authority.RevokeOrphanSessionChannelsAsync(_sessions.ChannelIds, CancellationToken.None)
                        .ConfigureAwait(false);
                    _orphansSwept = true;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Fail-closed anyway: an orphan session principal has no scope and is denied everywhere.
                }
            }

            AgentChannelEnrollmentResult enrollment;
            try
            {
                enrollment = await _authority.EnrollSessionChannelAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return new(AgentMcpChannelStatus.EnrollmentFailed);
            }
            if (enrollment is not
                {
                    Status: AgentChannelEnrollmentStatus.Enrolled, ChannelId: { } channelId, PrincipalId: { } principalId,
                    ProofReference: { } proof
                })
            {
                if (enrollment?.ChannelId is { } partial) await RevokeOrTombstoneAsync(partial, null).ConfigureAwait(false);
                return new(AgentMcpChannelStatus.EnrollmentFailed);
            }

            // From here on the channel exists: any failure or cancellation revokes it (CancellationToken.None).
            var registered = false;
            try
            {
                // Empty grants: the principal can be issued, nothing is authorized until the first turn plan.
                var revision = (await _policies.SaveAsync(principalId, [], 0, CancellationToken.None).ConfigureAwait(false))
                    .Revision;
                _sessions.Register(channelId, principalId, providerId, conversationId);
                registered = true;
                cancellationToken.ThrowIfCancellationRequested();
                var handle = new AgentMcpChannelHandle(channelId, providerId, conversationId);
                _open[channelId] = new OpenSession(handle, principalId, revision);
                var args = new List<string>
                {
                    "--stdio",
                    "--workspace-id", broker.WorkspaceId.ToString("D"),
                    "--channel-id", channelId.ToString("D"),
                    "--proof-ref", proof.Id.ToString("D"),
                    "--proof-ref-version", proof.Version.ToString(CultureInfo.InvariantCulture)
                };
                return new(AgentMcpChannelStatus.Ready, handle,
                    new McpServerLaunchSpec(McpServerLaunchSpec.DefaultServerName, _serverExecutable, args));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                _open.Remove(channelId);
                if (registered) _sessions.Close(principalId);
                await RevokeOrTombstoneAsync(channelId, registered ? principalId : null).ConfigureAwait(false);
                if (exception is OperationCanceledException) throw;
                return new(AgentMcpChannelStatus.PolicyUnavailable);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<AgentMcpChannelStatus> UpdateTurnAsync(AgentMcpChannelHandle handle, AgentTurnPlan plan,
        AgentProviderPermissions permissions, CancellationToken cancellationToken = default) =>
        UpdateTurnAsync(handle, plan, permissions, null, cancellationToken);

    public async Task<AgentMcpChannelStatus> UpdateTurnAsync(AgentMcpChannelHandle handle, AgentTurnPlan plan,
        AgentProviderPermissions permissions, AgentWorkspaceContext? workspaceContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(permissions);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_open.TryGetValue(handle.ChannelId, out var session) || session.Handle != handle)
                return AgentMcpChannelStatus.UnknownSession;

            // Narrow first: the scope is the intersection with the grants, so a transient mismatch never widens access.
            _sessions.UpdateTurn(session.PrincipalId, plan, permissions, workspaceContext);
            var published = _registry.GetChannelDescriptors()
                .Where(static descriptor => descriptor.Risk == AgentToolRisk.ReadOnly)
                .Select(static descriptor => descriptor.Name)
                .ToHashSet(StringComparer.Ordinal);
            if (plan.ProductTools.Any(tool => !published.Contains(tool)) ||
                (plan.RequiresPermissionPromptTool && !published.Contains(AgentToolRegistry.ApproveToolName)))
            {
                return AgentMcpChannelStatus.RequiredToolUnavailable;
            }
            IReadOnlyList<AgentPermissionGrant> grants;
            try
            {
                grants = await BuildGrantsAsync(handle.ChannelId, session.PrincipalId, plan, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return AgentMcpChannelStatus.PolicyUnavailable;
            }

            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var saved = await _policies.SaveAsync(session.PrincipalId, grants, session.PolicyRevision,
                        cancellationToken).ConfigureAwait(false);
                    _open[handle.ChannelId] = session with { PolicyRevision = saved.Revision };
                    return AgentMcpChannelStatus.Ready;
                }
                catch (AgentPolicyConcurrencyException) when (attempt == 0)
                {
                    AgentAuthorizationPolicySnapshot? current;
                    try
                    {
                        current = await _policies.LoadAsync(session.PrincipalId, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        return AgentMcpChannelStatus.PolicyUnavailable;
                    }
                    if (current is null || !current.IsValid) return AgentMcpChannelStatus.PolicyUnavailable;
                    session = session with { PolicyRevision = current.Revision };
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    return AgentMcpChannelStatus.PolicyUnavailable;
                }
            }
            return AgentMcpChannelStatus.PolicyUnavailable;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Tombstones the scope first (everything denied), then revokes durably with <see cref="CancellationToken.None"/>;
    /// the scope is forgotten only after the revocation succeeded. A failed revocation keeps the tombstone, writes empty
    /// grants and is retried on the next session operation and at disposal.
    /// </summary>
    public async Task CloseSessionAsync(AgentMcpChannelHandle handle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(handle);
        await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!_open.Remove(handle.ChannelId, out var session)) return;
            _sessions.Close(session.PrincipalId);
            await RevokeOrTombstoneAsync(handle.ChannelId, session.PrincipalId).ConfigureAwait(false);
            await RetryPendingRevocationsAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Session channels whose durable revocation failed; their scopes stay tombstoned.</summary>
    internal int PendingRevocationCount => _pendingRevocations.Count;

    /// <summary>
    /// The desktop disposes the container synchronously at exit: revoke the open channels and stop the owned broker off
    /// the calling thread (the same pattern as <see cref="AgentRuntimeHost"/>).
    /// </summary>
    public void Dispose() => Task.Run(async () => await DisposeAsync().ConfigureAwait(false)).GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var session in _open.Values.ToArray())
            {
                _sessions.Close(session.PrincipalId);
                await RevokeOrTombstoneAsync(session.Handle.ChannelId, session.PrincipalId).ConfigureAwait(false);
            }
            _open.Clear();
            await RetryPendingRevocationsAsync().ConfigureAwait(false);
            if (_ownedBroker is not null) await _ownedBroker.DisposeAsync().ConfigureAwait(false);
            _ownedBroker = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<AgentBrokerHost?> EnsureBrokerAsync(CancellationToken cancellationToken)
    {
        var broker = _externalBroker ?? (_ownedBroker ??= new AgentBrokerHost(_registry, _authority,
            new AgentBrokerOptions { WorkspaceId = Guid.NewGuid(), Enabled = false, Stage = _stage },
            sessionScopes: _sessions));
        if (broker.IsRunning) return broker;
        try
        {
            await broker.StartAsync(cancellationToken).ConfigureAwait(false);
            return broker;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or
                                              UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return broker.IsRunning ? broker : null;
        }
    }

    /// <summary>
    /// Grants of the turn: MongoDB metadata only for the planned connections (null = every connection with a stable
    /// generation, empty = none), bound to this channel's session and the MCP destination. Schema grants only when
    /// <c>get_cached_schema</c> is planned. Nothing else (no document, write or sampling grant) is ever written.
    /// </summary>
    private async Task<IReadOnlyList<AgentPermissionGrant>> BuildGrantsAsync(Guid channelId, Guid principalId,
        AgentTurnPlan plan, CancellationToken cancellationToken)
    {
        var tools = plan.IsBlocked ? [] : plan.ProductTools;
        var metadata = tools.Any(static tool => tool is AgentToolRegistry.ListConnectionsToolName or
            AgentToolRegistry.ListDatabasesToolName or AgentToolRegistry.ListCollectionsToolName or
            AgentToolRegistry.GetIndexesToolName);
        var schema = tools.Contains(AgentToolRegistry.GetCachedSchemaToolName, StringComparer.Ordinal);
        if (!metadata && !schema) return [];

        var profiles = await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(false) ?? [];
        var allowed = plan.AllowedConnectionIds is { } ids ? ids.ToHashSet() : null;
        var session = AgentInvocationScope.ForSession(channelId);
        var grants = new List<AgentPermissionGrant>();
        foreach (var profile in profiles)
        {
            if (profile is null || profile.Id == Guid.Empty || profile.SourceGenerationId is not { } generation ||
                generation == Guid.Empty || allowed is not null && !allowed.Contains(profile.Id) ||
                grants.Any(grant => grant.Scope.ConnectionId == profile.Id))
                continue;
            var scope = AgentNamespaceScope.ForConnection(profile.Id);
            if (metadata)
                grants.Add(new AgentPermissionGrant(principalId, session, generation, AgentPermission.ReadMetadata, scope,
                    McpDestination, AgentOutputDataScope.Metadata));
            if (schema)
                grants.Add(new AgentPermissionGrant(principalId, session, generation, AgentPermission.ReadSchema, scope,
                    McpDestination, AgentOutputDataScope.Schema));
        }
        return grants;
    }

    private async Task RevokeOrTombstoneAsync(Guid channelId, Guid? principalId)
    {
        if (await TryRevokeAsync(channelId).ConfigureAwait(false))
        {
            if (principalId is { } revokedPrincipal) _sessions.Remove(revokedPrincipal);
            return;
        }
        // Revocation failed: keep denying (tombstone), drop every grant, and retry later.
        if (principalId is { } id)
        {
            try
            {
                var current = await _policies.LoadAsync(id, CancellationToken.None).ConfigureAwait(false);
                if (current is { IsValid: true })
                    await _policies.SaveAsync(id, [], current.Revision, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The tombstone and the durable purpose still deny; the retry revokes.
            }
        }
        _pendingRevocations[channelId] = principalId;
    }

    private async Task RetryPendingRevocationsAsync()
    {
        foreach (var (channelId, principalId) in _pendingRevocations.ToArray())
        {
            if (!await TryRevokeAsync(channelId).ConfigureAwait(false)) continue;
            _pendingRevocations.Remove(channelId);
            if (principalId is { } id) _sessions.Remove(id);
        }
    }

    private async Task<bool> TryRevokeAsync(Guid channelId)
    {
        try
        {
            // Revoked or RevokedCleanupPending: the revocation is durable (a pending proof removal is retried by
            // RecoverPendingChannelsAsync); UnknownChannel: nothing left to revoke.
            await _authority.RevokeExternalChannelAsync(channelId, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return false;
        }
    }

    private sealed record OpenSession(AgentMcpChannelHandle Handle, Guid PrincipalId, long PolicyRevision);
}
