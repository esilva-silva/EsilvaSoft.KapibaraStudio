using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

public sealed partial class AgentToolRegistry
{
    // A read has already happened here. This gate controls release of its buffered result; it
    // never retries the MongoDB operation. The transport must apply its own final gate as well.
    private async Task<AgentToolInvocationResult?> RevalidateReleaseAsync(
        AgentToolInvocationResult result, AgentPrincipal principal, AgentInvocationContext context,
        AgentOutputDestination destination, AgentOutputDataScope? outputScope,
        string name, string? argumentsJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Per-session tools without a MongoDB namespace: only the channel and the turn plan can change.
        if (name is GetWorkspaceContextToolName or ProposeFileEditToolName)
            return await RevalidateSessionReleaseAsync(principal, name, cancellationToken).ConfigureAwait(false);
        if (SessionScopeOf(principal) is { } sessionScope && !sessionScope.Exposes(name))
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PermissionMissing);
        if (result.ReleaseProfiles is null || result.StructuredContentJson is null ||
            outputScope is null || !IsCompleteInvocationContext(context) ||
            !IsValidDestination(destination, context) || FindDescriptor(name) is not { } descriptor)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        if (await CheckPrincipalCurrentAsync(principal, cancellationToken).ConfigureAwait(false) is { } channelDenial)
            return channelDenial;

        var policyLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (policyLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, policyLoad.DenialReason);
        var policy = policyLoad.Policy;

        IReadOnlyList<ConnectionProfile> currentProfiles;
        try
        {
            currentProfiles = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (currentProfiles is null) throw new InvalidDataException();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }

        var expected = result.ReleaseProfiles;
        foreach (var original in expected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = currentProfiles.Where(item => item?.Id == original.Id).Take(2).ToArray();
            var sameProfile = name == ListConnectionsToolName
                ? matches.Length == 1 && matches[0]?.SourceGenerationId == original.SourceGenerationId &&
                  string.Equals(matches[0]?.Name, original.Name, StringComparison.Ordinal) &&
                  matches[0]?.IsReadOnly == original.IsReadOnly
                : matches.Length == 1 && matches[0] == original;
            if (!sameProfile ||
                original.SourceGenerationId is not { } generation || generation == Guid.Empty ||
                (name != ListConnectionsToolName && HasDynamicMongoTarget(original)))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        }

        IReadOnlyList<AgentNamespaceScope> scopes;
        try
        {
            using var output = JsonDocument.Parse(result.StructuredContentJson);
            if (name == ListConnectionsToolName)
            {
                scopes = expected.Select(profile => AgentNamespaceScope.ForConnection(profile.Id)).ToArray();
            }
            else if (name is ListDatabasesToolName or ListCollectionsToolName)
            {
                using var input = JsonDocument.Parse(argumentsJson!);
                var connectionId = input.RootElement.GetProperty("connectionId").GetGuid();
                var database = name == ListCollectionsToolName
                    ? input.RootElement.GetProperty("database").GetString() : null;
                scopes = output.RootElement.GetProperty("names").EnumerateArray()
                    .Select(item => name == ListDatabasesToolName
                        ? AgentNamespaceScope.ForDatabase(connectionId, item.GetString()!)
                        : AgentNamespaceScope.ForCollection(connectionId, database!, item.GetString()!))
                    .ToArray();
            }
            else
            {
                using var input = JsonDocument.Parse(argumentsJson!);
                scopes = [AgentNamespaceScope.ForCollection(
                    input.RootElement.GetProperty("connectionId").GetGuid(),
                    input.RootElement.GetProperty("database").GetString()!,
                    input.RootElement.GetProperty("collection").GetString()!)];
            }
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        }

        if (name != ListConnectionsToolName &&
            (expected.Count != 1 || scopes.Any(scope => scope.ConnectionId != expected[0].Id)))
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        foreach (var scope in scopes)
        {
            var profile = name == ListConnectionsToolName
                ? expected.Single(item => item.Id == scope.ConnectionId) : expected[0];
            foreach (var permission in descriptor.RequiredPermissions)
            {
                AgentPermissionDecision decision;
                try
                {
                    decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(
                        new AgentPermissionRequest(principal, permission, AgentToolRisk.ReadOnly, scope,
                            policy.Revision, profile.IsReadOnly, context, profile.SourceGenerationId,
                            destination, outputScope), cancellationToken), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyUnavailable);
                }
                if (decision.PolicyRevision != policy.Revision)
                    return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);
                if (!decision.IsAllowed)
                    return AgentToolInvocationResult.Failure(PermissionDenied, MapDenialReason(decision.Reason));
            }
        }
        // Evaluators may reload policy asynchronously. Recheck the revision after the last grant.
        var finalPolicy = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalPolicy.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalPolicy.DenialReason);
        if (finalPolicy.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);
        cancellationToken.ThrowIfCancellationRequested();
        return null;
    }
}
