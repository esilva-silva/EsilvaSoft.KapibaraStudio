using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string AgentSchemaSamplingConsentCollectionName = "agentSchemaSamplingConsents";

    /// <summary>
    /// Trusted local read used by the tool registry. Denies whenever the request is malformed, no consent document
    /// exists, or no single entry matches every binding at once: this exact profile generation, a scope covering the
    /// requested namespace, the same output destination, a sample no larger than the consented ceiling, the same
    /// authorization policy revision, and not expired (see <see cref="AgentSchemaSamplingConsentGrant.Authorizes"/>).
    /// Legacy schema v1 entries carry none of these bindings and never match. A corrupt or unknown-version document surfaces as
    /// <see cref="InvalidDataException"/> from <see cref="AgentSchemaSamplingConsentDocumentCodec.Decode"/>, which
    /// this method does not catch: the registry's generic failure handling around this call already denies on any
    /// exception, so corruption can only ever deny, never grant, and is never rewritten from here.
    /// </summary>
    Task<bool> IAgentSchemaSamplingConsentProvider.HasLocalConsentAsync(
        AgentSchemaSamplingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.PrincipalId == Guid.Empty || request.ConnectionId == Guid.Empty ||
            request.SourceGenerationId == Guid.Empty || request.Destination is null ||
            request.SampleSize is < 1 or > AgentSchemaSamplingConsentGrant.MaximumAllowedSampleSize ||
            request.PolicyRevision < 1)
            return Task.FromResult(false);
        AgentNamespaceScope requestedScope;
        try
        {
            requestedScope = AgentNamespaceScope.ForCollection(request.ConnectionId, request.Database, request.Collection);
        }
        catch (ArgumentException)
        {
            return Task.FromResult(false);
        }
        return RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var consent = ReadAgentSchemaSamplingConsent(request.PrincipalId);
            if (consent is null) return false;
            var now = DateTimeOffset.UtcNow;
            return consent.Consents.Any(grant => grant.Authorizes(request.PrincipalId, request.SourceGenerationId,
                requestedScope, request.Destination, request.SampleSize, request.PolicyRevision, now));
        }, cancellationToken);
    }

    Task<AgentSchemaSamplingConsentSnapshot?> IAgentSchemaSamplingConsentRepository.LoadAsync(
        Guid principalId, CancellationToken cancellationToken)
    {
        if (principalId == Guid.Empty) throw new ArgumentException("Principal inválido.", nameof(principalId));
        return RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ReadAgentSchemaSamplingConsent(principalId);
        }, cancellationToken);
    }

    Task<AgentSchemaSamplingConsentSnapshot> IAgentSchemaSamplingConsentRepository.SaveAsync(
        Guid principalId,
        IReadOnlyList<AgentSchemaSamplingConsentGrant> consents,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(consents);
        if (expectedRevision is < 0 or long.MaxValue) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (consents.Count > AgentSchemaSamplingConsentDocumentCodec.MaximumConsents)
            throw new ArgumentException("Limite de consentimentos excedido.", nameof(consents));
        // Copy and validate the caller's collection before entering the worker, exactly like the authorization facet.
        var proposed = AgentSchemaSamplingConsentSnapshot.Load(
            principalId, AgentSchemaSamplingConsentSnapshot.CurrentSchemaVersion, expectedRevision + 1, consents);
        if (!proposed.IsValid || proposed.Consents.Count > AgentSchemaSamplingConsentDocumentCodec.MaximumConsents)
            throw new ArgumentException("Consentimentos de amostragem de schema inválidos.", nameof(consents));
        var document = AgentSchemaSamplingConsentDocumentCodec.Encode(proposed);
        return RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Shares _gate with every other facet. Read + compare + one atomic Upsert cannot interleave with another
            // writer; a corrupt or unreadable document surfaces here (propagating InvalidDataException) and exits
            // before Upsert, preserving the original bytes instead of repairing or replacing them.
            var current = ReadAgentSchemaSamplingConsent(principalId);
            if ((current?.Revision ?? 0) != expectedRevision) throw new AgentSchemaSamplingConsentConcurrencyException();
            cancellationToken.ThrowIfCancellationRequested();
            _database.GetCollection(AgentSchemaSamplingConsentCollectionName).Upsert(document);
            return proposed;
        }, cancellationToken);
    }

    private AgentSchemaSamplingConsentSnapshot? ReadAgentSchemaSamplingConsent(Guid principalId)
    {
        var document = _database.GetCollection(AgentSchemaSamplingConsentCollectionName).FindById(principalId);
        return document is null ? null : AgentSchemaSamplingConsentDocumentCodec.Decode(document, principalId);
    }

    /// <summary>
    /// Removes schema sampling consent entries scoped to a connection that no longer exists. Called from profile
    /// deletion, inside the same transaction (DEC-L-RETENTION: profile removal is one of the events that cascades
    /// automatically): a deleted connection's identifier must not go on conferring sampling consent, even though
    /// consents are indexed by principal rather than by connection. A document that fails to decode is left
    /// untouched — it already denies every request by construction (see
    /// <see cref="IAgentSchemaSamplingConsentProvider.HasLocalConsentAsync"/>), and this cascade must never silently
    /// repair or discard corruption unrelated to the deleted connection.
    /// </summary>
    private void RemoveAgentSchemaSamplingConsentsForConnection(Guid connectionId)
    {
        var collection = _database.GetCollection(AgentSchemaSamplingConsentCollectionName);
        foreach (var document in collection.FindAll().ToArray())
        {
            if (!document.TryGetValue("_id", out var idValue) || !idValue.IsGuid) continue;
            var principalId = idValue.AsGuid;
            AgentSchemaSamplingConsentSnapshot snapshot;
            try
            {
                snapshot = AgentSchemaSamplingConsentDocumentCodec.Decode(document, principalId);
            }
            catch (InvalidDataException)
            {
                continue;
            }
            var remaining = snapshot.Consents.Where(consent => consent.Scope.ConnectionId != connectionId).ToArray();
            if (remaining.Length == snapshot.Consents.Count) continue;
            // Keeps the stored schema version: a legacy v1 document stays v1 (its grants cannot be expressed as v2),
            // so the cascade removes grants without silently migrating or discarding the others.
            var next = AgentSchemaSamplingConsentSnapshot.Load(
                principalId, snapshot.SchemaVersion, snapshot.Revision + 1, remaining);
            collection.Upsert(AgentSchemaSamplingConsentDocumentCodec.Encode(next));
        }
    }
}
