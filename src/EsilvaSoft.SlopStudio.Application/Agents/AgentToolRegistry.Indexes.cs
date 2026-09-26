using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed partial class AgentToolRegistry
{
    private async Task<AgentToolInvocationResult> InvokeIndexesAsync(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseGetIndexesArguments(argumentsJson, out var connectionId, out var database, out var collection))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (principal is null || !IsCompleteInvocationContext(context) || destination is null ||
            !IsValidDestination(destination, context!) || outputScope != AgentOutputDataScope.Metadata ||
            _indexes is null)
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
            // A stable profile generation does not pin values resolved from ENV or a vault at dispatch.
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
        AgentPermissionDecision decision;
        try
        {
            decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(
                new AgentPermissionRequest(principal, AgentPermission.ReadMetadata, AgentToolRisk.ReadOnly,
                    scope, policy.Revision, profile.IsReadOnly, context, profile.SourceGenerationId!.Value,
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

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } preflightFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, preflightFailure);
        var beforeRead = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (beforeRead.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, beforeRead.DenialReason);
        if (beforeRead.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        AgentMongoIndexPage page;
        try
        {
            page = await AwaitWithCancellationAsync(_indexes.GetIndexesAsync(profile, database!, collection!,
                _executionTimeout, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }
        if (page is null || !page.TargetVerified || page.Indexes is null || page.Indexes.Count > 200)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);

        var selected = new List<AgentMongoIndexSummary>(page.Indexes.Count);
        var truncated = page.Truncated;
        foreach (var index in page.Indexes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (index is null || !IsSafeIndexName(index.Name) || index.KeyFields is null ||
                index.KeyFields.Count is < 1 or > 32 || index.KeyFields.Any(field => !IsSafeIndexName(field)) ||
                index.KeyDirections is { } directions && (directions.Count != index.KeyFields.Count ||
                    directions.Any(direction => direction is not { Length: > 0 and <= 32 } ||
                        !direction.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) ||
                index.TtlSeconds is < 0 ||
                index.PartialFilterFields is { } partial && (partial.Count > 32 || partial.Any(field => !IsSafeIndexName(field))))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            var candidate = new IndexesResponse([.. selected, index], true, "OutputLimit");
            if (JsonSerializer.SerializeToUtf8Bytes(candidate, SerializerOptions).Length > MaximumOutputBytes)
            {
                if (selected.Count == 0) return AgentToolInvocationResult.Failure(ResultTooLarge);
                truncated = true;
                break;
            }
            selected.Add(index);
        }

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } outputFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, outputFailure);
        var finalLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalLoad.DenialReason);
        if (finalLoad.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        var json = JsonSerializer.Serialize(new IndexesResponse(selected, truncated,
            truncated && !page.Truncated ? "OutputLimit" : null), SerializerOptions);
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json, profile);
    }

    private static bool TryParseGetIndexesArguments(string? json, out Guid connectionId,
        out string? database, out string? collection)
    {
        connectionId = Guid.Empty;
        database = null;
        collection = null;
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name)) return false;
                switch (property.Name)
                {
                    case "connectionId" when property.Value.ValueKind == JsonValueKind.String &&
                        Guid.TryParseExact(property.Value.GetString(), "D", out var parsed) && parsed != Guid.Empty:
                        connectionId = parsed;
                        break;
                    case "database" when property.Value.ValueKind == JsonValueKind.String &&
                        IsSafeMetadataName(property.Value.GetString()):
                        database = property.Value.GetString();
                        break;
                    case "collection" when property.Value.ValueKind == JsonValueKind.String &&
                        IsSafeMetadataName(property.Value.GetString()):
                        collection = property.Value.GetString();
                        break;
                    default: return false;
                }
            }
            return connectionId != Guid.Empty && database is not null && collection is not null;
        }
        catch (JsonException) { return false; }
    }

    private static bool IsSafeIndexName(string? value)
    {
        if (value is not { Length: > 0 and <= 1_024 } || Utf8ByteCount(value) > 1_024 ||
            value.Any(char.IsControl)) return false;
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done) return false;
            remaining = remaining[consumed..];
        }
        return true;
    }

    private sealed record IndexesResponse(
        [property: JsonPropertyName("indexes")] IReadOnlyList<AgentMongoIndexSummary> Indexes,
        [property: JsonPropertyName("truncated")] bool Truncated,
        [property: JsonPropertyName("truncationReason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TruncationReason);
}
