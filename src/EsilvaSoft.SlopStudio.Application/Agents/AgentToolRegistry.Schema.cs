using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public sealed partial class AgentToolRegistry
{
    private const int MaximumSchemaFields = 200;
    private const int MaximumSchemaSampleSize = 100;
    private const int MaximumSampleProjectionBytes = 256 * 1024;

    private async Task<AgentToolInvocationResult> InvokeSchemaAsync(
        AgentPrincipal? principal, AgentInvocationContext? context, AgentOutputDestination? destination,
        AgentOutputDataScope? outputScope, string? argumentsJson, CancellationToken cancellationToken)
    {
        if (!TryParseSchemaArguments(argumentsJson, out var connectionId, out var database, out var collection, out var sampleSize))
            return AgentToolInvocationResult.Failure(InvalidArguments);
        if (principal is null || !IsCompleteInvocationContext(context) || destination is null ||
            !IsValidDestination(destination, context!) || outputScope != AgentOutputDataScope.Schema ||
            _metadata is null || _schemaSamplingConsent is null)
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
        foreach (var permission in new[] { AgentPermission.ReadSchema, AgentPermission.ExecuteReadQueries })
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

        var consent = new AgentSchemaSamplingRequest(principal.Id, context!.SessionId!.Value,
            context.TurnId!.Value, destination, connectionId, generationId, database!, collection!,
            sampleSize, policy.Revision);
        try
        {
            if (!await AwaitWithCancellationAsync(
                    _schemaSamplingConsent.HasLocalConsentAsync(consent, cancellationToken), cancellationToken)
                .ConfigureAwait(false))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PermissionMissing);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyUnavailable);
        }

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } preflightFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, preflightFailure);
        var beforeSample = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (beforeSample.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, beforeSample.DenialReason);
        if (beforeSample.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        ConcreteCollectionSchemaSampleResult sampled;
        try
        {
            var maximumTimeMs = (int)Math.Clamp(_executionTimeout.TotalMilliseconds, 1, 30_000);
            sampled = await AwaitWithCancellationAsync(_metadata.SampleConcreteCollectionSchemaBoundedAsync(
                profile, database!, collection!, new SchemaSampleOptions(sampleSize, Depth: 8, MaxTimeMs: maximumTimeMs),
                MaximumSampleProjectionBytes, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        }
        if (sampled is null || sampled.Items is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ExecutionFailed);
        if (sampled.Status == ConcreteCollectionSchemaSampleStatus.TargetNotVerifiable)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
        if (sampled.Status == ConcreteCollectionSchemaSampleStatus.LimitExceeded)
            return AgentToolInvocationResult.Failure(ResultTooLarge, AgentAuditDecisionReason.LimitExceeded);
        if (sampled.Status != ConcreteCollectionSchemaSampleStatus.Sampled || sampled.Items.Count > sampleSize ||
            sampled.Items.Any(item => item is null))
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);

        var observedAt = DateTimeOffset.UtcNow;
        var schema = new SchemaBuilder(maximumDepth: 8, maximumNodes: 4_000).AddSample(sampled.Items).Build();
        var fields = new List<SchemaFieldResponse>(MaximumSchemaFields);
        var envelopeBytes = JsonSerializer.SerializeToUtf8Bytes(
            new SchemaResponse([], sampled.Items.Count, observedAt, "sample", true), SerializerOptions).Length;
        var usedBytes = envelopeBytes;
        foreach (var (field, path) in EnumerateSchemaFields(schema.Root, "")
                     .OrderBy(item => item.Path, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var types = field.Types.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray();
            if (field.ObservedCount < 1 || field.ObservedCount > sampled.Items.Count ||
                types.Length == 0 || types.Any(type => !IsSafeBsonType(type)))
                return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.ValidationRejected);
            var item = new SchemaFieldResponse(path, types, field.ObservedCount);
            var itemBytes = JsonSerializer.SerializeToUtf8Bytes(item, SerializerOptions).Length +
                (fields.Count == 0 ? 0 : 1);
            if (fields.Count == MaximumSchemaFields || itemBytes > MaximumOutputBytes - usedBytes) break;
            fields.Add(item);
            usedBytes += itemBytes;
        }

        if (await RevalidateMetadataProfileAsync(profile, cancellationToken).ConfigureAwait(false) is { } outputFailure)
            return AgentToolInvocationResult.Failure(PermissionDenied, outputFailure);
        var finalLoad = await LoadCurrentPolicyAsync(principal, cancellationToken).ConfigureAwait(false);
        if (finalLoad.Policy is null)
            return AgentToolInvocationResult.Failure(PermissionDenied, finalLoad.DenialReason);
        if (finalLoad.Policy.Revision != policy.Revision)
            return AgentToolInvocationResult.Failure(PermissionDenied, AgentAuditDecisionReason.PolicyRevisionMismatch);

        var json = JsonSerializer.Serialize(new SchemaResponse(fields, sampled.Items.Count, observedAt, "sample", true),
            SerializerOptions);
        if (Utf8ByteCount(json) > MaximumOutputBytes) return AgentToolInvocationResult.Failure(ResultTooLarge);
        cancellationToken.ThrowIfCancellationRequested();
        return AgentToolInvocationResult.Success(json, profile);
    }

    private static bool TryParseSchemaArguments(string? json, out Guid connectionId, out string? database,
        out string? collection, out int sampleSize)
    {
        connectionId = Guid.Empty;
        database = null;
        collection = null;
        sampleSize = 20;
        if (json is null || Utf8ByteCount(json) > MaximumInputBytes) return false;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            var hasConnection = false;
            var hasDatabase = false;
            var hasCollection = false;
            var hasSampleSize = false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("connectionId") && !hasConnection &&
                    property.Value.ValueKind == JsonValueKind.String &&
                    Guid.TryParseExact(property.Value.GetString(), "D", out var parsedConnection) &&
                    parsedConnection != Guid.Empty)
                {
                    connectionId = parsedConnection;
                    hasConnection = true;
                }
                else if (property.NameEquals("database") && !hasDatabase &&
                         property.Value.ValueKind == JsonValueKind.String &&
                         IsSafeMetadataName(property.Value.GetString()))
                {
                    database = property.Value.GetString();
                    hasDatabase = true;
                }
                else if (property.NameEquals("collection") && !hasCollection &&
                         property.Value.ValueKind == JsonValueKind.String &&
                         IsSafeMetadataName(property.Value.GetString()))
                {
                    collection = property.Value.GetString();
                    hasCollection = true;
                }
                else if (property.NameEquals("sampleSize") && !hasSampleSize &&
                         property.Value.ValueKind == JsonValueKind.Number &&
                         property.Value.TryGetInt32(out var parsedSize) && parsedSize is >= 1 and <= MaximumSchemaSampleSize)
                {
                    sampleSize = parsedSize;
                    hasSampleSize = true;
                }
                else return false;
            }
            return hasConnection && hasDatabase && hasCollection;
        }
        catch (JsonException) { return false; }
    }

    private static bool IsSafeSchemaPath(string? path)
    {
        if (path is not { Length: > 0 and <= 1_024 } || Utf8ByteCount(path) > 1_024 ||
            CompletionPrivacy.ContainsSensitiveText(path) || path.Any(char.IsControl)) return false;
        var remaining = path.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done) return false;
            remaining = remaining[consumed..];
        }
        return true;
    }

    // Escape literal dots and backslashes so a field named "a.b" does not collide with a nested a.b path.
    private static IEnumerable<(FieldNode Field, string Path)> EnumerateSchemaFields(FieldNode parent, string prefix)
    {
        foreach (var field in parent.Children.Items)
        {
            if (!IsSafeSchemaPath(field.Name)) continue;
            var segment = field.Name.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace(".", "\\.", StringComparison.Ordinal);
            var path = prefix.Length == 0 ? segment : prefix + "." + segment;
            if (!IsSafeSchemaPath(path)) continue;
            yield return (field, path);
            foreach (var nested in EnumerateSchemaFields(field, path)) yield return nested;
        }
    }

    private static bool IsSafeBsonType(string? type) => type is
        "double" or "string" or "object" or "array" or "binData" or "undefined" or "objectId" or
        "bool" or "date" or "null" or "regex" or "dbPointer" or "javascript" or "symbol" or
        "javascriptWithScope" or "int" or "timestamp" or "long" or "decimal" or "minKey" or "maxKey";

    private sealed record SchemaResponse(
        [property: JsonPropertyName("fields")] IReadOnlyList<SchemaFieldResponse> Fields,
        [property: JsonPropertyName("sampleSize")] int SampleSize,
        [property: JsonPropertyName("observedAt")] DateTimeOffset ObservedAt,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("isPartial")] bool IsPartial);

    private sealed record SchemaFieldResponse(
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("bsonTypes")] IReadOnlyList<string> BsonTypes,
        [property: JsonPropertyName("observedCount")] int ObservedCount);
}
