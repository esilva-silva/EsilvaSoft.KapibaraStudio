using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Deterministic effective-grant evaluation. This service has no allow fallback or tool execution path.</summary>
public sealed class AgentPermissionEvaluator : IAgentPermissionEvaluator
{
    private readonly IAgentAuthorizationPolicyProvider _policyProvider;

    public AgentPermissionEvaluator(IAgentAuthorizationPolicyProvider policyProvider) =>
        _policyProvider = policyProvider ?? throw new ArgumentNullException(nameof(policyProvider));

    public async Task<AgentPermissionDecision> EvaluateAsync(AgentPermissionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var principal = request.Principal;
        var permission = request.Permission;
        var risk = request.Risk;
        var scope = request.Scope;
        var invocationContext = request.InvocationContext;
        var sourceGenerationId = request.SourceGenerationId;
        var destination = request.Destination;
        var outputDataScope = request.OutputDataScope;
        var expectedRevision = request.ExpectedPolicyRevision;

        if (principal is null) return Deny(expectedRevision, AgentPermissionDenialReason.MissingPrincipal);
        if (principal.Id == Guid.Empty || !Enum.IsDefined(principal.Origin) || principal.PolicyRevision < 1)
            return Deny(expectedRevision, AgentPermissionDenialReason.InvalidPrincipal);
        if (permission is null) return Deny(expectedRevision, AgentPermissionDenialReason.MissingPermission);
        if (!Enum.IsDefined(permission.Value)) return Deny(expectedRevision, AgentPermissionDenialReason.UnknownPermission);
        if (risk is null) return Deny(expectedRevision, AgentPermissionDenialReason.MissingRisk);
        if (!Enum.IsDefined(risk.Value)) return Deny(expectedRevision, AgentPermissionDenialReason.UnknownRisk);
        if (scope is null || scope.ConnectionId == Guid.Empty ||
            (scope.CollectionName is not null && scope.DatabaseName is null))
            return Deny(expectedRevision, AgentPermissionDenialReason.InvalidScope);
        if (invocationContext is null) return Deny(expectedRevision, AgentPermissionDenialReason.MissingInvocationContext);
        if (invocationContext.SessionId is null || invocationContext.SessionId == Guid.Empty ||
            invocationContext.TurnId is null || invocationContext.TurnId == Guid.Empty)
            return Deny(expectedRevision, AgentPermissionDenialReason.InvalidInvocationContext);
        if (sourceGenerationId is null || sourceGenerationId == Guid.Empty)
            return Deny(expectedRevision, AgentPermissionDenialReason.MissingSourceGeneration);
        if (destination is null) return Deny(expectedRevision, AgentPermissionDenialReason.MissingDestination);
        if (!Enum.IsDefined(destination.Kind) ||
            (destination.Kind == AgentOutputDestinationKind.Local && destination.ProviderId is not null) ||
            (destination.IsExternal && string.IsNullOrWhiteSpace(destination.ProviderId)))
            return Deny(expectedRevision, AgentPermissionDenialReason.UnknownDestination);
        if (destination.IsExternal &&
            (string.IsNullOrWhiteSpace(invocationContext.ProviderId) ||
             !string.Equals(destination.ProviderId, invocationContext.ProviderId, StringComparison.Ordinal)))
            return Deny(expectedRevision, AgentPermissionDenialReason.DestinationMismatch);
        // The MCP channel is only reachable by an externally authenticated principal (broker), never by the runtime.
        if (destination.Kind == AgentOutputDestinationKind.McpExternal && principal.Origin != AgentPrincipalOrigin.External)
            return Deny(expectedRevision, AgentPermissionDenialReason.DestinationMismatch);
        if (outputDataScope is null) return Deny(expectedRevision, AgentPermissionDenialReason.MissingOutputDataScope);
        if (!Enum.IsDefined(outputDataScope.Value)) return Deny(expectedRevision, AgentPermissionDenialReason.UnknownOutputDataScope);
        if (expectedRevision < 1) return Deny(expectedRevision, AgentPermissionDenialReason.InvalidPolicyRevision);
        if (!AgentPermissionRiskCompatibility.IsCompatible(permission.Value, risk.Value))
            return Deny(expectedRevision, AgentPermissionDenialReason.RiskPermissionMismatch);
        if (request.ConnectionIsReadOnly && risk.Value != AgentToolRisk.ReadOnly)
            return Deny(expectedRevision, AgentPermissionDenialReason.ReadOnlyConnection);

        AgentAuthorizationPolicySnapshot? policy;
        try
        {
            policy = await _policyProvider.LoadAsync(principal.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Deny(expectedRevision, AgentPermissionDenialReason.PolicyUnavailable);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (policy is null) return Deny(expectedRevision, AgentPermissionDenialReason.PolicyUnavailable);
        if (!policy.IsValid) return Deny(policy.Revision, AgentPermissionDenialReason.InvalidPolicy);
        if (policy.PrincipalId != principal.Id) return Deny(policy.Revision, AgentPermissionDenialReason.InvalidPolicy);
        if (policy.Revision != expectedRevision || principal.PolicyRevision != expectedRevision)
            return Deny(policy.Revision, AgentPermissionDenialReason.PolicyRevisionMismatch);

        var isGranted = policy.Grants.Any(grant =>
            grant.PrincipalId == principal.Id &&
            grant.InvocationScope.Covers(invocationContext) &&
            grant.SourceGenerationId == sourceGenerationId.Value &&
            grant.Permission == permission.Value &&
            grant.Destination == destination &&
            grant.OutputDataScope == outputDataScope.Value &&
            grant.Scope.Covers(scope));

        return isGranted
            ? new AgentPermissionDecision(true, policy.Revision, AgentPermissionDenialReason.None)
            : Deny(policy.Revision,
                policy.Grants.Any(grant => grant.PrincipalId == principal.Id &&
                    grant.InvocationScope.Covers(invocationContext) &&
                    grant.Permission == permission.Value && grant.Destination == destination &&
                    grant.OutputDataScope == outputDataScope.Value && grant.Scope.Covers(scope))
                    ? AgentPermissionDenialReason.SourceGenerationMismatch
                    : AgentPermissionDenialReason.MissingGrant);
    }

    private static AgentPermissionDecision Deny(long revision, AgentPermissionDenialReason reason) =>
        new(false, revision, reason);

}
