using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string AgentAuthorizationCollectionName = "agentAuthorizationPolicies";

    Task<AgentAuthorizationPolicySnapshot?> IAgentAuthorizationPolicyProvider.LoadAsync(
        Guid principalId, CancellationToken cancellationToken)
    {
        if (principalId == Guid.Empty) throw new ArgumentException("Principal inválido.", nameof(principalId));
        return RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ReadAgentAuthorizationPolicy(principalId);
        }, cancellationToken);
    }

    Task<AgentAuthorizationPolicySnapshot> IAgentAuthorizationPolicyRepository.SaveAsync(
        Guid principalId,
        IReadOnlyList<AgentPermissionGrant> grants,
        long expectedRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grants);
        if (expectedRevision is < 0 or long.MaxValue) throw new ArgumentOutOfRangeException(nameof(expectedRevision));
        if (grants.Count > AgentAuthorizationPolicyDocumentCodec.MaximumGrants)
            throw new ArgumentException("Limite de concessões excedido.", nameof(grants));
        // Copy and validate the caller's collection before entering the worker. Grants themselves are immutable.
        var proposed = AgentAuthorizationPolicySnapshot.Load(
            principalId, AgentAuthorizationPolicySnapshot.CurrentSchemaVersion, expectedRevision + 1, grants);
        if (!proposed.IsValid || proposed.Grants.Count > AgentAuthorizationPolicyDocumentCodec.MaximumGrants)
            throw new ArgumentException("Concessões de autorização inválidas.", nameof(grants));
        var document = AgentAuthorizationPolicyDocumentCodec.Encode(proposed);
        return RunAsync(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            // All facets share _gate. Read + compare + one atomic Upsert cannot interleave with another writer.
            // In particular, a parse/read failure exits before Upsert and preserves the unreadable document.
            var current = ReadAgentAuthorizationPolicy(principalId);
            if ((current?.Revision ?? 0) != expectedRevision) throw new AgentPolicyConcurrencyException();
            cancellationToken.ThrowIfCancellationRequested();
            _database.GetCollection(AgentAuthorizationCollectionName).Upsert(document);
            return proposed;
        }, cancellationToken);
    }

    private AgentAuthorizationPolicySnapshot? ReadAgentAuthorizationPolicy(Guid principalId)
    {
        var document = _database.GetCollection(AgentAuthorizationCollectionName).FindById(principalId);
        return document is null ? null : AgentAuthorizationPolicyDocumentCodec.Decode(document, principalId);
    }
}
