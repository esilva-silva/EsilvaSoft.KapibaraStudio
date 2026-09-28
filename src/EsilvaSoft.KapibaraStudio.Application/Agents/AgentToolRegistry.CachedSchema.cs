using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// <c>get_cached_schema</c>: field names and types already known to the IDE, read only from the autocomplete cache
/// (<see cref="MetadataAccess.Peek"/>, which never schedules a load or a sample) or, when the cache has nothing, from the
/// learned schema store. It never touches MongoDB, never starts learning and never releases a value: only paths, BSON
/// type names and counts. An empty cache is a structured answer, not an error.
/// </summary>
public sealed partial class AgentToolRegistry
{
    internal const int MaximumCachedSchemaFields = 500;
    internal const int MaximumCachedSchemaDepth = 8;
    private const int MaximumCachedSchemaTypesPerField = 16;

    private const string GetCachedSchemaOutputSchema = """
        {"type":"object","additionalProperties":false,"required":["available","source","fields","truncated"],"properties":{"available":{"type":"boolean"},"source":{"type":"string","enum":["sampled","learned","none"]},"reason":{"type":"string","enum":["NoCachedSchema","LearnedSchemaUnavailable"]},"observedAt":{"type":"string","format":"date-time"},"freshness":{"type":"string","enum":["fresh","stale"]},"sampleSize":{"type":"integer","minimum":0},"fields":{"type":"array","maxItems":500,"items":{"type":"object","additionalProperties":false,"required":["path","types","primaryType"],"properties":{"path":{"type":"string"},"types":{"type":"array","maxItems":16,"items":{"type":"object","additionalProperties":false,"required":["type","count"],"properties":{"type":{"type":"string"},"count":{"type":"integer","minimum":0}}}},"primaryType":{"type":"string"},"occurrence":{"type":"number","minimum":0,"maximum":1}}}},"truncated":{"type":"boolean"}}}
        """;

    private async Task<AgentToolInvocationResult> InvokeCachedSchemaAsync(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseGetIndexesArguments(argumentsJson, out var connectionId, out var database, out var collection))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (!IsSessionCallBound(principal, context, destination, outputScope, GetCachedSchemaToolName, out _) ||
            _sessionTools?.MetadataCache is not { } cache)
            return AgentToolInvocationResult.Failure(PermissionDenied);

        var initialLoad = await LoadCurrentPolicyAsync(principal!, cancellationToken).ConfigureAwait(false);
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
            // Same fail-closed rule as the release gate: the grant pins the profile template, not ENV/vault values.
            if (HasDynamicMongoTarget(profile))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }

        AgentPermissionDecision decision;
        try
        {
            decision = await AwaitWithCancellationAsync(_permissions.EvaluateAsync(
                new AgentPermissionRequest(principal, AgentPermission.ReadSchema, AgentToolRisk.ReadOnly,
                    AgentNamespaceScope.ForCollection(connectionId, database!, collection!), policy.Revision,
                    profile.IsReadOnly, context, profile.SourceGenerationId!.Value, destination, outputScope),
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

        CachedSchemaResponse response;
        try
        {
            response = await ReadCachedSchemaAsync(cache, profile, database!, collection!, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } failure)
            return AgentToolInvocationResult.Failure(PermissionDenied, failure);
        var finalLoad = await LoadCurrentPolicyAsync(principal!, cancellationToken).ConfigureAwait(false);
        if (finalLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalLoad.DenialReason);
        if (finalLoad.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        var json = JsonSerializer.Serialize(response, SessionSerializerOptions);
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json, profile);
    }

    private async Task<CachedSchemaResponse> ReadCachedSchemaAsync(IMetadataCache cache, ConnectionProfile profile,
        string database, string collection, CancellationToken cancellationToken)
    {
        // Peek: reads what is already in memory; never schedules a load, a refresh or a sample.
        var sampled = cache.GetSampledSchema(ConnectionIdentity.From(profile), database, collection, MetadataAccess.Peek);
        if (sampled.Value is { } schema)
        {
            var builder = new CachedFieldBuilder();
            foreach (var node in schema.Descendants())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!builder.TryAdd(node.Path, node.Types.Select(static pair => (pair.Key, (long)pair.Value)),
                        node.PrimaryType, node.Occurrence))
                    break;
            }
            return new CachedSchemaResponse(true, "sampled", null, sampled.LoadedAt,
                sampled.Freshness == MetadataFreshness.Fresh ? "fresh" : "stale", schema.SampleSize, builder.Fields,
                builder.Truncated || schema.IsTruncated);
        }

        if (_sessionTools?.LearnedSchemas is not { } learned)
            return CachedSchemaResponse.None("NoCachedSchema");
        var read = await AwaitWithCancellationAsync(
            learned.ReadAvailabilityAsync(LearnedSchemaKey.Create(profile.Id, database, collection), cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (read.Availability == LearnedSchemaHydrationState.Unavailable)
            return CachedSchemaResponse.None("LearnedSchemaUnavailable");
        if (read.Availability != LearnedSchemaHydrationState.Available || read.Snapshot is not { } snapshot)
            return CachedSchemaResponse.None("NoCachedSchema");

        var fields = new CachedFieldBuilder();
        foreach (var field in snapshot.Fields)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var types = field.TypeObservations.Select(static pair => (pair.Key, pair.Value)).ToArray();
            var primary = types.Length == 0 ? "unknown"
                : types.OrderByDescending(static pair => pair.Item2).ThenBy(static pair => pair.Item1, StringComparer.Ordinal)
                    .First().Item1;
            if (!fields.TryAdd(string.Join('.', field.Path.Segments), types, primary, field.Frequency))
                break;
        }
        return new CachedSchemaResponse(true, "learned", null, snapshot.LastObservedUtc, null,
            (int)Math.Min(int.MaxValue, Math.Max(0, snapshot.CompleteDocumentObservations)), fields.Fields,
            fields.Truncated || snapshot.IsTruncated);
    }

    /// <summary>Accumulates fields within the count, depth, name and byte limits; anything dropped marks truncation.</summary>
    private sealed class CachedFieldBuilder
    {
        // Envelope room for the other members of the response.
        private int _bytes = 1_024;

        public List<CachedSchemaField> Fields { get; } = [];
        public bool Truncated { get; private set; }

        /// <returns>False when no further field fits.</returns>
        public bool TryAdd(string path, IEnumerable<(string Type, long Count)> types, string primaryType, double? occurrence)
        {
            if (Fields.Count == MaximumCachedSchemaFields)
            {
                Truncated = true;
                return false;
            }
            if (!IsSafeSchemaText(path, 1_024) || path.Split('.').Length > MaximumCachedSchemaDepth ||
                !IsSafeSchemaText(primaryType, 64))
            {
                Truncated = true;
                return true;
            }
            var typeList = new List<CachedSchemaType>();
            foreach (var (type, count) in types)
            {
                if (typeList.Count == MaximumCachedSchemaTypesPerField || !IsSafeSchemaText(type, 64))
                {
                    Truncated = true;
                    continue;
                }
                typeList.Add(new CachedSchemaType(type, Math.Max(0, count)));
            }
            var field = new CachedSchemaField(path, typeList, primaryType,
                occurrence is { } value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : null);
            var size = JsonSerializer.SerializeToUtf8Bytes(field, SessionSerializerOptions).Length + 1;
            if (_bytes + size > MaximumOutputBytes)
            {
                Truncated = true;
                return false;
            }
            _bytes += size;
            Fields.Add(field);
            return true;
        }
    }

    private static bool IsSafeSchemaText(string? value, int maximumLength)
    {
        if (value is not { Length: > 0 } || value.Length > maximumLength || value.Any(char.IsControl)) return false;
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done) return false;
            remaining = remaining[consumed..];
        }
        return true;
    }

    private sealed record CachedSchemaResponse(
        [property: JsonPropertyName("available")] bool Available,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("reason")] string? Reason,
        [property: JsonPropertyName("observedAt")] DateTimeOffset? ObservedAt,
        [property: JsonPropertyName("freshness")] string? Freshness,
        [property: JsonPropertyName("sampleSize")] int? SampleSize,
        [property: JsonPropertyName("fields")] IReadOnlyList<CachedSchemaField> Fields,
        [property: JsonPropertyName("truncated")] bool Truncated)
    {
        public static CachedSchemaResponse None(string reason) =>
            new(false, "none", reason, null, null, null, [], false);
    }

    private sealed record CachedSchemaField(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("types")] IReadOnlyList<CachedSchemaType> Types,
        [property: JsonPropertyName("primaryType")] string PrimaryType,
        [property: JsonPropertyName("occurrence")] double? Occurrence);

    private sealed record CachedSchemaType(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("count")] long Count);
}
