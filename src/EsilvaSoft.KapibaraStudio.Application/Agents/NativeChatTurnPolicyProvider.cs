using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Adds grants for active native chat turns without persisting turn authorization on the stable internal principal.
/// Each grant is bound to the exact turn, provider destination and current connection generation. The durable
/// internal policy is an empty anchor for principal issuance; a missing or nonempty anchor fails closed.
/// </summary>
public sealed class NativeChatTurnPolicyProvider : IAgentAuthorizationPolicyProvider
{
    private readonly IAgentAuthorizationPolicyRepository _repository;
    private readonly IAgentPrincipalAuthority _authority;
    private readonly IAgentNativeChatTurnScopes _turns;
    private readonly IConnectionProfileRepository _profiles;

    public NativeChatTurnPolicyProvider(IAgentAuthorizationPolicyRepository repository,
        IAgentPrincipalAuthority authority, IAgentNativeChatTurnScopes turns,
        IConnectionProfileRepository profiles)
    {
        _repository = repository;
        _authority = authority;
        _turns = turns;
        _profiles = profiles;
    }

    public async Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
    {
        var stored = await _repository.LoadAsync(principalId, cancellationToken).ConfigureAwait(false);
        if (stored is null || !stored.IsValid)
            return stored;
        if (stored.PrincipalId != principalId) return null;
        // The internal anchor must stay empty even when the last turn has already ended. Otherwise a late
        // invocation could acquire unexpected durable grants merely because the transient registry is empty.
        if (stored.Grants.Count != 0)
            return principalId == await _authority.GetInternalPrincipalIdAsync(cancellationToken).ConfigureAwait(false)
                ? null : stored;

        var turns = _turns.Snapshot();
        if (turns.Count == 0) return stored;

        if (principalId !=
            await _authority.GetInternalPrincipalIdAsync(cancellationToken).ConfigureAwait(false))
            return stored;

        var profiles = await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (profiles is null) return null;
        var grants = new List<AgentPermissionGrant>();
        foreach (var turn in turns)
        {
            var isLocal = turn.ProviderId == "local";
            if (turn.ProviderId is not (AgentProviderIds.GitHubCopilotSubscription or "local") ||
                turn.Plan.IsBlocked || !turn.Permissions.IsWellFormed ||
                !string.Equals(turn.Permissions.ProviderId, turn.ProviderId, StringComparison.Ordinal) ||
                (!isLocal && !turn.Permissions.HasExternalDestinationConsent) ||
                turn.Permissions.EnabledReadTools is null) continue;
            var enabled = (turn.Permissions.EnabledReadTools ?? []).ToHashSet(StringComparer.Ordinal);
            var tools = turn.Plan.ProductTools.Where(enabled.Contains).ToHashSet(StringComparer.Ordinal);
            var metadata = tools.Overlaps([
                AgentToolRegistry.ListConnectionsToolName, AgentToolRegistry.ListDatabasesToolName,
                AgentToolRegistry.ListCollectionsToolName, AgentToolRegistry.GetIndexesToolName, AgentToolRegistry.GetSearchIndexesToolName]);
            var schema = tools.Contains(AgentToolRegistry.GetCachedSchemaToolName) &&
                turn.Permissions.DataSending?.InferredSchema == true;
            HashSet<string> documentTools = turn.Permissions.DataSending?.MongoDocuments == true
                ? tools.Where(IsDocumentReadTool).ToHashSet(StringComparer.Ordinal)
                : [];
            if (!metadata && !schema && documentTools.Count == 0) continue;

            var selected = turn.Permissions.ConnectionScope == AgentConnectionScope.Selected
                ? (turn.Permissions.SelectedConnectionIds ?? []).ToHashSet() : null;
            var allowed = turn.Plan.AllowedConnectionIds is { } ids ? ids.ToHashSet() : null;
            var destination = isLocal ? AgentOutputDestination.Local() : AgentOutputDestination.ProviderExternal(turn.ProviderId);
            var invocation = AgentInvocationScope.ForTurn(turn.SessionId, turn.TurnId);
            foreach (var profile in profiles)
            {
                if (profile is null || profile.Id == Guid.Empty ||
                    profile.SourceGenerationId is not { } generation || generation == Guid.Empty ||
                    allowed is not null && !allowed.Contains(profile.Id) ||
                    selected is not null && !selected.Contains(profile.Id)) continue;
                var scope = AgentNamespaceScope.ForConnection(profile.Id);
                if (metadata)
                    grants.Add(new AgentPermissionGrant(principalId, invocation, generation,
                        AgentPermission.ReadMetadata, scope, destination, AgentOutputDataScope.Metadata));
                if (schema)
                    grants.Add(new AgentPermissionGrant(principalId, invocation, generation,
                        AgentPermission.ReadSchema, scope, destination, AgentOutputDataScope.Schema));
                foreach (var permission in documentTools.SelectMany(RequiredPermissionsFor).Distinct())
                {
                    grants.Add(new AgentPermissionGrant(principalId, invocation, generation,
                        permission, scope, destination, AgentOutputDataScope.DocumentValues));
                }
            }
        }

        // Loading profiles may suspend while an anchor is replaced or a turn is revoked. Do not publish grants
        // from the earlier revision, nor transfer grants to a replacement scope with the same session/turn IDs.
        var currentStored = await _repository.LoadAsync(principalId, cancellationToken).ConfigureAwait(false);
        if (currentStored is null || !currentStored.IsValid || currentStored.PrincipalId != principalId ||
            currentStored.SchemaVersion != stored.SchemaVersion || currentStored.Revision != stored.Revision ||
            currentStored.Grants.Count != 0)
            return null;
        var currentScopes = turns.Where(turn => ReferenceEquals(turn, _turns.Find(turn.SessionId, turn.TurnId)))
            .Select(turn => AgentInvocationScope.ForTurn(turn.SessionId, turn.TurnId)).ToHashSet();
        return AgentAuthorizationPolicySnapshot.Load(principalId, stored.SchemaVersion, stored.Revision,
            grants.Where(grant => currentScopes.Contains(grant.InvocationScope)).Distinct());
    }

    private static bool IsDocumentReadTool(string name) => AgentProductToolNames.IsMongoDocumentRead(name);

    private static IReadOnlyList<AgentPermission> RequiredPermissionsFor(string name) =>
        name == AgentToolRegistry.GetQueryDiagnosticsToolName
            ? [AgentPermission.ReadDiagnostics, AgentPermission.ReadDocuments]
            : [AgentPermission.ReadDocuments];
}
