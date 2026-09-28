using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed partial class AgentToolRegistry
{
    private async Task<AgentToolInvocationResult> InvokeDistinctAsync(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseDistinctArguments(argumentsJson, out var connectionId, out var database, out var collection,
                out var query))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (principal is null || !IsCompleteInvocationContext(context) || destination is null ||
            !IsValidDestination(destination, context!) || outputScope != AgentOutputDataScope.DocumentValues ||
            _distinct is null)
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

        var generationId = profile.SourceGenerationId!.Value;
        var scope = AgentNamespaceScope.ForCollection(connectionId, database!, collection!);
        foreach (var permission in new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments })
        {
            AgentPermissionDecision decision;
            try
            {
                decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(
                    new AgentPermissionRequest(principal, permission, AgentToolRisk.ReadOnly, scope,
                        policy.Revision, profile.IsReadOnly, context, generationId, destination, outputScope),
                    cancellationToken), cancellationToken).ConfigureAwait(false);
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
        var preflightPolicy = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (preflightPolicy.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, preflightPolicy.DenialReason);
        if (preflightPolicy.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        AgentMongoDistinctPage page;
        try
        {
            var allowedTimeMs = (int)Math.Clamp(_executionTimeout.TotalMilliseconds, 1, 30_000);
            page = await AwaitWithCancellationAsync(_distinct.DistinctAsync(profile,
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
        if (page is null || !page.TargetVerified || page.ValuesEjson is null ||
            page.ValuesEjson.Count > query!.MaximumValues ||
            page.Truncated != (page.TruncationReason is not null) ||
            page.Truncated && page.ValuesEjson.Count == 0)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        if (page.ResultTooLarge) return AgentToolInvocationResult.Failure(ResultTooLarge);

        var returned = new List<string>(page.ValuesEjson.Count);
        var truncated = page.Truncated;
        var reason = page.TruncationReason;
        foreach (var value in page.ValuesEjson)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsValidDistinctValue(value))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            var candidate = new DistinctResponse([.. returned, value], true, "OutputLimit");
            if (Utf8ByteCount(JsonSerializer.Serialize(candidate, SerializerOptions)) > MaximumOutputBytes)
            {
                if (returned.Count == 0) return AgentToolInvocationResult.Failure(ResultTooLarge);
                truncated = true;
                reason = AgentMongoDistinctTruncationReason.OutputLimit;
                break;
            }
            returned.Add(value);
        }

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } outputFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, outputFailure);
        var finalLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalLoad.DenialReason);
        if (finalLoad.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        var json = JsonSerializer.Serialize(new DistinctResponse(returned, truncated,
            truncated ? reason.ToString() : null), SerializerOptions);
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json, profile);
    }

    private static bool TryParseDistinctArguments(string? json, out Guid connectionId, out string? database,
        out string? collection, out AgentMongoDistinctQuery? query)
    {
        connectionId = Guid.Empty;
        database = null;
        collection = null;
        query = null;
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 16 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? field = null;
            var filter = "{}";
            var maximumValues = 20;
            var maxTimeMs = 5_000;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!seen.Add(property.Name)) return false;
                switch (property.Name)
                {
                    case "connectionId" when property.Value.ValueKind == JsonValueKind.String &&
                        Guid.TryParseExact(property.Value.GetString(), "D", out var parsedConnection) &&
                        parsedConnection != Guid.Empty:
                        connectionId = parsedConnection;
                        break;
                    case "database" when property.Value.ValueKind == JsonValueKind.String &&
                        IsSafeMetadataName(property.Value.GetString()):
                        database = property.Value.GetString();
                        break;
                    case "collection" when property.Value.ValueKind == JsonValueKind.String &&
                        IsSafeMetadataName(property.Value.GetString()):
                        collection = property.Value.GetString();
                        break;
                    case "field" when property.Value.ValueKind == JsonValueKind.String &&
                        IsSafeDistinctFieldPath(property.Value.GetString()):
                        field = property.Value.GetString();
                        break;
                    case "filterEjson" when property.Value.ValueKind == JsonValueKind.String &&
                        IsLiteralEjsonDocument(property.Value.GetString(), rejectCode: true):
                        filter = property.Value.GetString()!;
                        break;
                    case "maximumValues" when property.Value.ValueKind == JsonValueKind.Number &&
                        property.Value.TryGetInt32(out var parsedMaximum) && parsedMaximum is >= 1 and <= 100:
                        maximumValues = parsedMaximum;
                        break;
                    case "maxTimeMs" when property.Value.ValueKind == JsonValueKind.Number &&
                        property.Value.TryGetInt32(out var parsedMaxTime) && parsedMaxTime is >= 1 and <= 30_000:
                        maxTimeMs = parsedMaxTime;
                        break;
                    default: return false;
                }
            }
            if (connectionId == Guid.Empty || database is null || collection is null || field is null) return false;
            query = new(database, collection, field, filter, maximumValues, maxTimeMs);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool IsSafeDistinctFieldPath(string? field)
    {
        if (field is not { Length: > 0 and <= 1_024 } || Utf8ByteCount(field) > 1_024 ||
            field.Contains('$') || field.Any(char.IsControl) ||
            field.Split('.').Any(string.IsNullOrWhiteSpace)) return false;
        var remaining = field.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done) return false;
            remaining = remaining[consumed..];
        }
        return true;
    }

    private static bool IsValidDistinctValue(string? json)
    {
        if (json is null || Utf8ByteCount(json) > MaximumOutputBytes) return false;
        try
        {
            using var _ = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            return true;
        }
        catch (JsonException) { return false; }
    }

    private sealed record DistinctResponse(
        [property: JsonPropertyName("valuesEjson")] IReadOnlyList<string> ValuesEjson,
        [property: JsonPropertyName("truncated")] bool Truncated,
        [property: JsonPropertyName("truncationReason"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TruncationReason);
}
