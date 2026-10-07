using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Reads only an explicit allowlist of index metadata; BSON option values never leave this adapter.</summary>
public sealed class MongoAgentIndexSource(
    IConnectionSecretStore secrets,
    IEnvironmentVaultRepository? environments,
    IMongoClientPool clients,
    ISecretStore? credentialStore = null) : IAgentMongoIndexSource
{
    private const int MaximumIndexes = 200;
    private const int MaximumRawIndexBytes = 64 * 1024;
    private const int MaximumRawTotalBytes = 256 * 1024;
    private const int MaximumDefinitionFields = 32;
    private const int MaximumKeyFields = 32;

    public async Task<AgentMongoSearchIndexPage> GetSearchIndexesAsync(ConnectionProfile profile, string database,
        string collection, TimeSpan maximumExecutionTime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        if (maximumExecutionTime <= TimeSpan.Zero || maximumExecutionTime > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(maximumExecutionTime));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(maximumExecutionTime);
        var token = deadline.Token;
        if (profile.ConnectionString.Contains("${", StringComparison.Ordinal) ||
            profile.ConnectionString.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) ||
            profile.TargetHost?.Contains("${", StringComparison.Ordinal) == true ||
            profile.TargetHost?.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("Dynamic targets are not supported for agent index reads.");
        var context = await MongoOperationContext.PrepareAsync(profile, secrets, environments, clients,
            token, credentialStore).ConfigureAwait(false);
        var target = context.CreateClient().GetDatabase(database);
        var originalUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(target, collection, token).ConfigureAwait(false);
        if (originalUuid is null) return new([], false, false);
        var indexes = new List<AgentMongoSearchIndexSummary>();
        var rawBytes = 0;
        var truncated = false;
        // The driver builds a fixed $listSearchIndexes metadata pipeline. No user pipeline/filter is accepted.
        using var cursor = await target.GetCollection<BsonDocument>(collection).SearchIndexes.ListAsync(
            name: null, aggregateOptions: new AggregateOptions { BatchSize = 1, MaxTime = maximumExecutionTime }, cancellationToken: token)
            .ConfigureAwait(false);
        while (!truncated && await cursor.MoveNextAsync(token).ConfigureAwait(false))
        {
            foreach (var definition in cursor.Current)
            {
                token.ThrowIfCancellationRequested();
                if (indexes.Count == MaximumIndexes) { truncated = true; break; }
                var size = definition.ToBson().Length;
                if (size > MaximumRawIndexBytes || rawBytes + size > MaximumRawTotalBytes)
                    throw new FormatException("Search index metadata exceeds the byte limit.");
                rawBytes += size;
                indexes.Add(ProjectSearchIndex(definition));
            }
        }
        var currentUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(target, collection, token).ConfigureAwait(false);
        return currentUuid is not null && originalUuid.AsSpan().SequenceEqual(currentUuid)
            ? new(indexes, truncated, true) : new([], false, false);
    }

    internal static AgentMongoSearchIndexSummary ProjectSearchIndex(BsonDocument definition)
    {
        var name = Text("name", null);
        var type = Text("type", "search");
        var status = Text("status", "UNKNOWN");
        var paths = new HashSet<string>(StringComparer.Ordinal);
        if (definition.GetValue("latestDefinition", BsonNull.Value) is BsonDocument config)
        {
            if (config.GetValue("mappings", BsonNull.Value) is BsonDocument mappings &&
                mappings.GetValue("fields", BsonNull.Value) is BsonDocument fields)
                CollectFields(fields, "", 0);
            if (config.GetValue("fields", BsonNull.Value) is BsonArray vectorFields)
                foreach (var field in vectorFields.OfType<BsonDocument>())
                    if (field.GetValue("path", BsonNull.Value) is BsonString path) AddPath(path.AsString);
        }
        return new(name, type, status, Flag(definition, "queryable"), paths.Order(StringComparer.Ordinal).ToArray());

        string Text(string key, string? fallback) => definition.GetValue(key, BsonNull.Value) is BsonString text
            ? text.AsString : fallback ?? throw new FormatException("Invalid search index metadata.");
        void AddPath(string path)
        {
            if (path.Length is < 1 or > 1024 || path.Any(char.IsControl) || paths.Count >= 200 && !paths.Contains(path))
                throw new FormatException("Search index field paths exceed the limit.");
            paths.Add(path);
        }
        void CollectFields(BsonDocument fields, string prefix, int depth)
        {
            if (depth > 8) throw new FormatException("Search index mapping exceeds the depth limit.");
            foreach (var field in fields)
            {
                var path = prefix.Length == 0 ? field.Name : prefix + "." + field.Name;
                AddPath(path);
                IEnumerable<BsonDocument> mappings = field.Value is BsonDocument mapping ? [mapping]
                    : field.Value is BsonArray list ? list.OfType<BsonDocument>() : [];
                foreach (var mappingItem in mappings)
                    if (mappingItem.GetValue("fields", BsonNull.Value) is BsonDocument nested)
                        CollectFields(nested, path, depth + 1);
            }
        }
    }

    public async Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database,
        string collection, TimeSpan maximumExecutionTime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        if (maximumExecutionTime <= TimeSpan.Zero || maximumExecutionTime > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(maximumExecutionTime));
        using var sourceDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        sourceDeadline.CancelAfter(maximumExecutionTime);
        var operationToken = sourceDeadline.Token;
        if (profile.ConnectionString.Contains("${", StringComparison.Ordinal) ||
            profile.ConnectionString.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) ||
            profile.TargetHost?.Contains("${", StringComparison.Ordinal) == true ||
            profile.TargetHost?.Contains("ENV.get(", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("Dynamic targets are not supported for agent index reads.");
        var context = await MongoOperationContext.PrepareAsync(profile, secrets, environments, clients,
            operationToken, credentialStore).ConfigureAwait(false);
        var target = context.CreateClient().GetDatabase(database);
        var originalUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(target, collection,
            operationToken).ConfigureAwait(false);
        if (originalUuid is null) return new([], false, false);

        var indexes = new List<AgentMongoIndexSummary>(MaximumIndexes);
        var truncated = false;
        var indexLimitReached = false;
        var rawBytes = 0;
        // One definition per batch bounds the cursor's decoded batch; MongoDB may still send one
        // oversized definition, which is rejected before it enters the projected result.
        var options = CreateListOptions(maximumExecutionTime);
        using (var cursor = await target.GetCollection<BsonDocument>(collection).Indexes.ListAsync(options, operationToken)
                   .ConfigureAwait(false))
        {
            while (await cursor.MoveNextAsync(operationToken).ConfigureAwait(false))
            {
                foreach (var definition in cursor.Current)
                {
                    operationToken.ThrowIfCancellationRequested();
                    if (indexes.Count == MaximumIndexes)
                    {
                        truncated = true;
                        indexLimitReached = true;
                        break;
                    }
                    var projected = ProjectBounded(definition, MaximumRawTotalBytes - rawBytes,
                        out var definitionBytes, out var projectionTruncated);
                    rawBytes += definitionBytes;
                    indexes.Add(projected);
                    truncated |= projectionTruncated;
                }
                if (indexLimitReached) break;
            }
        }

        var currentUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(target, collection,
            operationToken).ConfigureAwait(false);
        if (currentUuid is null || !originalUuid.AsSpan().SequenceEqual(currentUuid))
            return new([], false, false);
        return new(indexes, truncated, true);
    }

    internal static AgentMongoIndexSummary Project(BsonDocument definition)
        => Project(definition, out _);

    private static AgentMongoIndexSummary Project(BsonDocument definition, out bool truncated)
    {
        truncated = false;
        if (!definition.TryGetValue("name", out var name) || !name.IsString ||
            !definition.TryGetValue("key", out var key) || key is not BsonDocument fields)
            throw new FormatException("Invalid index metadata.");
        var partialFields = definition.TryGetValue("partialFilterExpression", out var partial) &&
                            partial is BsonDocument filter
            ? PartialFilterPaths(filter, out truncated) : null;
        return new(name.AsString, fields.Names.ToArray(), Flag(definition, "unique"),
            Flag(definition, "sparse"), Flag(definition, "hidden"))
        {
            KeyDirections = fields.Values.Select(Direction).ToArray(),
            TtlSeconds = definition.TryGetValue("expireAfterSeconds", out var ttl) ? ProjectTtl(ttl) : null,
            // Only the paths: the partial filter's values (constants of the user's data) never leave this adapter.
            PartialFilterFields = partialFields
        };
    }

    private static long? ProjectTtl(BsonValue ttl)
    {
        // BSON Int64 is already the output type; routing it through double loses seconds above 2^53.
        if (ttl.IsInt64) return ttl.AsInt64 >= 0 ? ttl.AsInt64 : null;
        return ttl.IsNumeric && ttl.ToDouble() is var seconds && double.IsFinite(seconds) && seconds >= 0
            ? (long)Math.Min(seconds, long.MaxValue) : null;
    }

    // Numeric keys become "1"/"-1"; special index kinds keep their short token ("text", "2dsphere", "hashed"...).
    private static string Direction(BsonValue value) =>
        value.IsNumeric ? value.ToDouble() < 0 ? "-1" : "1" :
        value.IsString && value.AsString is { Length: > 0 and <= 32 } kind &&
            kind.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? kind : "other";

    private static string[] PartialFilterPaths(BsonDocument filter, out bool truncated)
    {
        var paths = new List<string>();
        var projectionTruncated = false;
        Collect(filter, 0);
        var distinct = paths.Distinct(StringComparer.Ordinal).Take(33).ToArray();
        truncated = projectionTruncated || distinct.Length > 32;
        return [.. distinct.Take(32)];

        void Collect(BsonDocument document, int depth)
        {
            if (depth > 4)
            {
                projectionTruncated = true;
                return;
            }
            foreach (var element in document)
            {
                if (paths.Count >= 64)
                {
                    projectionTruncated = true;
                    return;
                }
                if (element.Name is "$and" or "$or" or "$nor" && element.Value is BsonArray items)
                {
                    foreach (var item in items)
                        if (item is BsonDocument nested) Collect(nested, depth + 1);
                }
                else if (!element.Name.StartsWith('$') && element.Name.Length is > 0 and <= 1_024 &&
                         !element.Name.Any(char.IsControl))
                {
                    paths.Add(element.Name);
                }
            }
        }
    }

    internal static ListIndexesOptions CreateListOptions(TimeSpan maximumExecutionTime)
    {
        if (maximumExecutionTime <= TimeSpan.Zero || maximumExecutionTime > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(maximumExecutionTime));
        var options = new ListIndexesOptions { BatchSize = 1 };
        // The 3.11.1 XML documents Timeout, but its net6.0 binary does not expose it.
        // Set it when supplied by the loaded driver; the linked token enforces the same
        // deadline on the current binary and across all calls in this operation.
        var timeout = typeof(ListIndexesOptions).GetProperty("Timeout");
        if (timeout?.CanWrite == true &&
            (timeout.PropertyType == typeof(TimeSpan) ||
             Nullable.GetUnderlyingType(timeout.PropertyType) == typeof(TimeSpan)))
            timeout.SetValue(options, maximumExecutionTime);
        return options;
    }

    internal static AgentMongoIndexSummary ProjectBounded(BsonDocument definition, int remainingBytes,
        out int definitionBytes)
        => ProjectBounded(definition, remainingBytes, out definitionBytes, out _);

    internal static AgentMongoIndexSummary ProjectBounded(BsonDocument definition, int remainingBytes,
        out int definitionBytes, out bool truncated)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.ElementCount is < 2 or > MaximumDefinitionFields ||
            !definition.TryGetValue("key", out var key) || key is not BsonDocument fields ||
            fields.ElementCount is < 1 or > MaximumKeyFields)
            throw new FormatException("Index metadata exceeds the field limit.");
        definitionBytes = definition.ToBson().Length;
        if (definitionBytes > MaximumRawIndexBytes || definitionBytes > remainingBytes)
            throw new FormatException("Index metadata exceeds the byte limit.");
        return Project(definition, out truncated);
    }

    private static bool Flag(BsonDocument definition, string name) =>
        definition.TryGetValue(name, out var value) && value.IsBoolean && value.AsBoolean;
}
