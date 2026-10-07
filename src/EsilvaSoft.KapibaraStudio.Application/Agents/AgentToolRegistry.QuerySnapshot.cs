using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

public sealed partial class AgentToolRegistry
{
    private const string GetQueryResultsInputSchema = """
        {"type":"object","additionalProperties":false,"properties":{"resultIndex":{"type":"integer","minimum":0,"maximum":9999,"default":0},"skip":{"type":"integer","minimum":0,"maximum":100000,"default":0},"limit":{"type":"integer","minimum":1,"maximum":100,"default":20}}}
        """;
    private const string GetQueryResultsOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["available","executionId","tabId","resultIndex","resultSetCount","results","hasMore","truncated","sourceTruncated"],"properties":{"available":{"type":"boolean"},"executionId":{"type":["string","null"]},"tabId":{"type":["string","null"]},"resultIndex":{"type":"integer"},"resultSetCount":{"type":"integer"},"results":{"type":"array","maxItems":100,"items":{"type":"string"}},"hasMore":{"type":"boolean"},"truncated":{"type":"boolean"},"sourceTruncated":{"type":"boolean"}}}
        """;
    private const string GetQueryDiagnosticsOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["available","executionId","tabId","status","errorCode","messages","errors","durationMs","truncated"],"properties":{"available":{"type":"boolean"},"executionId":{"type":["string","null"]},"tabId":{"type":["string","null"]},"status":{"type":["string","null"]},"errorCode":{"type":["string","null"]},"messages":{"type":"string"},"errors":{"type":"string"},"durationMs":{"type":"number"},"truncated":{"type":"boolean"}}}
        """;

    private async Task<AgentToolInvocationResult> InvokeQuerySnapshotAsync(AgentPrincipal? principal,
        AgentInvocationContext? context, AgentOutputDestination? destination, AgentOutputDataScope? outputScope,
        string name, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseQuerySnapshotArguments(name, argumentsJson, out var resultIndex, out var skip, out var limit))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (!IsSessionCallBound(principal, context, destination, outputScope, name, out var scope) ||
            scope.Permissions?.DataSending.MongoDocuments != true || scope.WorkspaceContext is not { } workspace)
            return AgentToolInvocationResult.Failure(PermissionDenied);
        if (await ValidateQuerySnapshotReleaseAsync(principal!, context!, destination!, outputScope, name,
                workspace, cancellationToken).ConfigureAwait(false) is { } denial)
            return denial;

        var execution = workspace.QueryExecution;
        string json;
        if (name == GetQueryDiagnosticsToolName)
        {
            // Logs can contain document values, hence the same opt-in and connection gate as results.
            var messages = BoundedDiagnostic(execution?.Messages ?? "", out var messagesTruncated);
            var errors = BoundedDiagnostic(execution?.Errors ?? "", out var errorsTruncated);
            json = JsonSerializer.Serialize(new
            {
                available = execution is not null, executionId = execution?.ExecutionId.ToString("D"),
                tabId = execution?.TabId, status = execution?.Status, errorCode = execution?.ErrorCode,
                messages, errors, durationMs = execution?.Duration.TotalMilliseconds ?? 0,
                truncated = messagesTruncated || errorsTruncated
            }, SerializerOptions);
        }
        else
        {
            if (execution is not null && resultIndex >= execution.Results.Count &&
                (execution.Results.Count != 0 || resultIndex != 0))
                return AgentToolInvocationResult.Failure(InvalidArguments);
            var set = execution?.Results.ElementAtOrDefault(resultIndex);
            var selected = new List<string>();
            var truncated = false;
            var values = set?.ValuesEjson;
            foreach (var value in values?.Skip(skip).Take(limit) ?? [])
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Never split Extended JSON or silently convert BSON values to presentation strings.
                if (JsonSerializer.SerializeToUtf8Bytes(value).Length > MaximumOutputBytes - 4096 ||
                    JsonSerializer.SerializeToUtf8Bytes(selected.Append(value)).Length > MaximumOutputBytes - 4096)
                {
                    if (selected.Count == 0) return AgentToolInvocationResult.Failure(ResultTooLarge);
                    truncated = true;
                    break;
                }
                selected.Add(value);
            }
            json = JsonSerializer.Serialize(new
            {
                available = execution is not null, executionId = execution?.ExecutionId.ToString("D"),
                tabId = execution?.TabId, resultIndex, resultSetCount = execution?.Results.Count ?? 0,
                results = selected, hasMore = values is not null && skip + selected.Count < values.Count,
                truncated, sourceTruncated = set?.IsTruncated ?? false
            }, SerializerOptions);
        }
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        return AgentToolInvocationResult.SuccessWorkspaceContext(json, workspace);
    }

    private async Task<AgentToolInvocationResult?> ValidateQuerySnapshotReleaseAsync(AgentPrincipal principal,
        AgentInvocationContext context, AgentOutputDestination destination, AgentOutputDataScope? outputScope,
        string name, AgentWorkspaceContext workspace, CancellationToken cancellationToken)
    {
        if (await RevalidateSessionReleaseAsync(principal, context, destination, outputScope, name, cancellationToken)
                .ConfigureAwait(false) is { } sessionDenial)
            return sessionDenial;
        if (!IsCurrentWorkspaceSnapshot(principal, context, workspace) ||
            !IsSessionCallBound(principal, context, destination, outputScope, name, out var scope) ||
            scope.Permissions?.DataSending.MongoDocuments != true)
            return AgentToolInvocationResult.Failure(PermissionDenied);
        var load = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (load.Policy is null) return AgentToolInvocationResult.Failure(PermissionDenied, load.DenialReason);
        var execution = workspace.QueryExecution;
        if (execution is null) return null; // No query is started to fill an absent snapshot.
        if (execution.ExecutionId == Guid.Empty || execution.TabId != workspace.TabId || execution.Origins.Count == 0)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        var profiles = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        var origins = execution.Origins.Concat(execution.Results.Select(static result => result.Origin)).Distinct().ToArray();
        foreach (var origin in origins)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var matches = profiles.Where(profile => profile.Id == origin.ConnectionId).Take(2).ToArray();
            if (!scope.AllowsConnection(origin.ConnectionId) ||
                scope.Permissions.ConnectionScope == AgentConnectionScope.Selected &&
                scope.Permissions.SelectedConnectionIds?.Contains(origin.ConnectionId) != true ||
                origin.ConnectionId == Guid.Empty || origin.SourceGenerationId is null ||
                origin.SourceGenerationId == Guid.Empty || matches.Length != 1 ||
                matches[0].SourceGenerationId != origin.SourceGenerationId ||
                origin.Database is not null && !IsSafeMetadataName(origin.Database) ||
                origin.Collection is not null && (origin.Database is null || !IsSafeMetadataName(origin.Collection)))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            var namespaceScope = origin.Collection is not null
                ? AgentNamespaceScope.ForCollection(origin.ConnectionId, origin.Database!, origin.Collection)
                : origin.Database is not null ? AgentNamespaceScope.ForDatabase(origin.ConnectionId, origin.Database)
                : AgentNamespaceScope.ForConnection(origin.ConnectionId);
            foreach (var permission in name == GetQueryDiagnosticsToolName
                         ? new[] { AgentPermission.ReadDocuments, AgentPermission.ReadDiagnostics }
                         : new[] { AgentPermission.ReadDocuments })
            {
                var decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(new AgentPermissionRequest(
                    principal, permission, AgentToolRisk.ReadOnly, namespaceScope, load.Policy.Revision,
                    matches[0].IsReadOnly, context, origin.SourceGenerationId, destination, outputScope), cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                if (decision.PolicyRevision != load.Policy.Revision)
                    return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);
                if (!decision.IsAllowed)
                    return AgentToolInvocationResult.Failure(PermissionDenied, MapDenialReason(decision.Reason));
            }
        }
        // Evaluating grants may suspend while a connection is edited/deleted. Verify generations again after
        // the last evaluator await, including the final release that follows durable outcome persistence.
        var currentProfiles = await AwaitWithCancellationAsync(_profiles.GetAllAsync(cancellationToken), cancellationToken)
            .ConfigureAwait(false);
        foreach (var origin in origins)
        {
            var matches = currentProfiles.Where(profile => profile.Id == origin.ConnectionId).Take(2).ToArray();
            if (matches.Length != 1 || matches[0].SourceGenerationId != origin.SourceGenerationId)
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        }
        var final = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        return final.Policy is not null && final.Policy.Revision == load.Policy.Revision &&
               IsCurrentWorkspaceSnapshot(principal, context, workspace) &&
               IsSessionCallBound(principal, context, destination, outputScope, name, out var currentScope) &&
               currentScope.Permissions?.DataSending.MongoDocuments == true &&
               origins.All(origin => currentScope.AllowsConnection(origin.ConnectionId) &&
                   (currentScope.Permissions.ConnectionScope != AgentConnectionScope.Selected ||
                    currentScope.Permissions.SelectedConnectionIds?.Contains(origin.ConnectionId) == true))
            ? null : AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);
    }

    private static bool TryParseQuerySnapshotArguments(string name, string? json, out int resultIndex,
        out int skip, out int limit)
    {
        resultIndex = 0; skip = 0; limit = 20;
        if (name == GetQueryDiagnosticsToolName) return IsClosedEmptyObject(json);
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name) || !property.Value.TryGetInt32(out var value)) return false;
                switch (property.Name)
                {
                    case "resultIndex" when value is >= 0 and <= 9999: resultIndex = value; break;
                    case "skip" when value is >= 0 and <= 100000: skip = value; break;
                    case "limit" when value is >= 1 and <= 100: limit = value; break;
                    default: return false;
                }
            }
            return true;
        }
        catch (JsonException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static string BoundedDiagnostic(string value, out bool truncated)
    {
        truncated = Utf8ByteCount(value) > 16000;
        if (truncated)
        {
            var builder = new StringBuilder();
            var bytes = 0;
            foreach (var rune in value.EnumerateRunes())
            {
                if (bytes + rune.Utf8SequenceLength > 16000) break;
                builder.Append(rune); bytes += rune.Utf8SequenceLength;
            }
            value = builder.ToString();
        }
        return AgentSecretRedaction.Redact(value) ?? "";
    }
}
