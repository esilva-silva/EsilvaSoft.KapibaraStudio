using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed partial class AgentToolRegistry
{
    private async Task<AgentToolInvocationResult> InvokeExplainAsync(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseFindArguments(argumentsJson, out var connectionId, out var database, out var collection,
                out var query))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (principal is null || !IsCompleteInvocationContext(context) || destination is null ||
            !IsValidDestination(destination, context!) || outputScope != AgentOutputDataScope.DocumentValues ||
            _explain is null)
            return AgentToolInvocationResult.Failure(PermissionDenied);

        var initialLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (initialLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, initialLoad.DenialReason);
        var policy = initialLoad.Policy;

        ConnectionProfile profile;
        try
        {
            var profiles = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
                .ConfigureAwait(false);
            if (profiles is null) return AgentToolInvocationResult.Failure(PermissionDenied);
            var matching = profiles.Where(item => item?.Id == connectionId).Take(2).ToArray();
            if (matching.Length != 1 || matching[0] is not { SourceGenerationId: { } generation } ||
                generation == Guid.Empty || !IsValidProfileName(matching[0].Name))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            profile = matching[0];
            if (HasDynamicMongoTarget(profile))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }

        var scope = AgentNamespaceScope.ForCollection(connectionId, database!, collection!);
        foreach (var permission in new[]
                 { AgentPermission.ReadDiagnostics, AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments })
        {
            AgentPermissionDecision decision;
            try
            {
                decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(
                    new AgentPermissionRequest(principal, permission, AgentToolRisk.ReadOnly, scope,
                        policy.Revision, profile.IsReadOnly, context, profile.SourceGenerationId!.Value,
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

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } preflightFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, preflightFailure);
        var beforeRead = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (beforeRead.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, beforeRead.DenialReason);
        if (beforeRead.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        AgentMongoExplainResult explained;
        try
        {
            var allowedTimeMs = (int)Math.Clamp(_executionTimeout.TotalMilliseconds, 1, 30_000);
            explained = await AwaitWithCancellationAsync(_explain.ExplainAsync(profile,
                query! with { MaxTimeMs = Math.Min(query!.MaxTimeMs, allowedTimeMs) }, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (FormatException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(InvalidArguments);
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }
        if (explained is null || !explained.TargetVerified)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        if (explained.ResultTooLarge) return AgentToolInvocationResult.Failure(ResultTooLarge);
        if (!IsSafeExplainPlan(explained.PlanEjson))
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } outputFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, outputFailure);
        var finalLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalLoad.DenialReason);
        if (finalLoad.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        var json = JsonSerializer.Serialize(new ExplainResponse(explained.PlanEjson, "queryPlanner"), SerializerOptions);
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json, profile);
    }

    private static bool IsSafeExplainPlan(string? json)
    {
        if (json is null || Utf8ByteCount(json) > MaximumOutputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            var nodes = 0;
            return SafeNode(document.RootElement, ref nodes);
        }
        catch (JsonException) { return false; }
    }

    private static bool SafeNode(JsonElement element, ref int nodes)
    {
        if (element.ValueKind != JsonValueKind.Object || ++nodes > 200) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var hasStructure = false;
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name)) return false;
            switch (property.Name)
            {
                case "stage" or "indexName" when property.Value.ValueKind == JsonValueKind.String &&
                    IsSafeIndexName(property.Value.GetString()):
                    hasStructure = true;
                    break;
                case "direction" when property.Value.ValueKind == JsonValueKind.String &&
                    property.Value.GetString() is "forward" or "backward":
                    break;
                case "isMultiKey" when property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False:
                    break;
                case "inputStage" or "outerStage" or "innerStage" or "queryPlan" when
                    SafeNode(property.Value, ref nodes):
                    hasStructure = true;
                    break;
                case "inputStages" when SafeNodes(property.Value, ref nodes):
                    hasStructure = true;
                    break;
                default: return false;
            }
        }
        return hasStructure;
    }

    private static bool SafeNodes(JsonElement element, ref int nodes)
    {
        if (element.ValueKind != JsonValueKind.Array) return false;
        foreach (var item in element.EnumerateArray())
            if (!SafeNode(item, ref nodes)) return false;
        return true;
    }

    private sealed record ExplainResponse(
        [property: JsonPropertyName("planEjson")] string PlanEjson,
        [property: JsonPropertyName("verbosity")] string Verbosity);
}
