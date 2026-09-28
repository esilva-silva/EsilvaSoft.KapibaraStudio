using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed partial class AgentToolRegistry
{
    private async Task<AgentToolInvocationResult> InvokeCountAsync(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseCountArguments(argumentsJson, out var connectionId, out var database, out var collection, out var query))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (principal is null || !IsCompleteInvocationContext(context) || destination is null ||
            !IsValidDestination(destination, context!) || outputScope != AgentOutputDataScope.DocumentValues ||
            _count is null)
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

        AgentMongoCountResult counted;
        try
        {
            var allowedTimeMs = (int)Math.Clamp(_executionTimeout.TotalMilliseconds, 1, 30_000);
            counted = await AwaitWithCancellationAsync(_count.CountAsync(profile,
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
        if (counted is null || !counted.TargetVerified || !IsCanonicalCountEjson(counted.CountEjson))
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } outputFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, outputFailure);
        var finalLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalLoad.DenialReason);
        if (finalLoad.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        var json = JsonSerializer.Serialize(new CountResponse(counted.CountEjson, false), SerializerOptions);
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json, profile);
    }

    private static bool TryParseCountArguments(string? json, out Guid connectionId, out string? database,
        out string? collection, out AgentMongoCountQuery? query)
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
            var filter = "{}";
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
                    case "filterEjson" when property.Value.ValueKind == JsonValueKind.String &&
                        IsLiteralEjsonDocument(property.Value.GetString(), rejectCode: true):
                        filter = property.Value.GetString()!;
                        break;
                    case "maxTimeMs" when property.Value.ValueKind == JsonValueKind.Number &&
                        property.Value.TryGetInt32(out var parsedMaxTime) && parsedMaxTime is >= 1 and <= 30_000:
                        maxTimeMs = parsedMaxTime;
                        break;
                    default: return false;
                }
            }
            if (connectionId == Guid.Empty || database is null || collection is null) return false;
            query = new(database, collection, filter, maxTimeMs);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static bool IsCanonicalCountEjson(string? text)
    {
        if (text is null || text.Length is < 19 or > 39 ||
            !text.StartsWith("{\"$numberLong\":\"", StringComparison.Ordinal) ||
            !text.EndsWith("\"}", StringComparison.Ordinal)) return false;
        var digits = text[16..^2];
        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value) &&
            value >= 0 && string.Equals(digits, value.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private sealed record CountResponse(
        [property: JsonPropertyName("countEjson")] string CountEjson,
        [property: JsonPropertyName("estimated")] bool Estimated);
}
