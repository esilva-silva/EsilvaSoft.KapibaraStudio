using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>
/// Metadata commands for autocomplete. Listings use nameOnly/authorized options; schema sampling computes names and
/// BSON types on the server so document values never reach the IDE.
/// </summary>
public sealed class MongoMetadataSource(IConnectionSecretStore? secrets = null, IEnvironmentVaultRepository? environments = null,
    MongoClientPool? clients = null, ISecretStore? credentialStore = null)
    : IMongoMetadataSource
{
    internal const string SampleComment = "slopstudio:autocomplete-schema";
    internal const string AgentSampleComment = "slopstudio:agent-schema";
    private const int UnauthorizedCode = 13;
    private static readonly JsonWriterSettings CanonicalJson = new() { OutputMode = JsonOutputMode.CanonicalExtendedJson };

    public async Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        var client = await ClientAsync(profile, cancellationToken).ConfigureAwait(false);
        using var cursor = await client.ListDatabaseNamesAsync(new ListDatabaseNamesOptions { AuthorizedDatabases = true }, cancellationToken).ConfigureAwait(false);
        return (await cursor.ToListAsync(cancellationToken).ConfigureAwait(false)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(
        ConnectionProfile profile, int maximum, CancellationToken cancellationToken)
    {
        ValidateAgentMaximum(maximum);
        var client = await ClientAsync(profile, cancellationToken).ConfigureAwait(false);
        using var cursor = await client.ListDatabaseNamesAsync(
            new ListDatabaseNamesOptions { AuthorizedDatabases = true }, cancellationToken).ConfigureAwait(false);
        return await ReadBoundedNamesAsync(cursor, maximum, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var target = (await ClientAsync(profile, cancellationToken).ConfigureAwait(false)).GetDatabase(database);
        // Kind stays Unknown until a definition loads (WithKnownKinds fills it later); see PEND-K14-KIND in decisions.md.
        using var names = await target.ListCollectionNamesAsync(new ListCollectionNamesOptions { AuthorizedCollections = true }, cancellationToken).ConfigureAwait(false);
        return (await names.ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(name => new CollectionEntry(name, CollectionKind.Unknown)).OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public async Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(
        ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken)
    {
        ValidateAgentMaximum(maximum);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var target = (await ClientAsync(profile, cancellationToken).ConfigureAwait(false)).GetDatabase(database);
        using var cursor = await target.ListCollectionNamesAsync(
            new ListCollectionNamesOptions { AuthorizedCollections = true }, cancellationToken).ConfigureAwait(false);
        var result = await ReadBoundedNamesAsync(cursor, maximum, cancellationToken).ConfigureAwait(false);
        return new BoundedMetadataResult<CollectionEntry>(
            result.Items.Select(name => new CollectionEntry(name, CollectionKind.Unknown)).ToArray(), result.Overflow);
    }

    private static void ValidateAgentMaximum(int maximum)
    {
        if (maximum is < 1 or > 10_000) throw new ArgumentOutOfRangeException(nameof(maximum));
    }

    internal static async Task<BoundedMetadataResult<string>> ReadBoundedNamesAsync(
        IAsyncCursor<string> cursor, int maximum, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        ValidateAgentMaximum(maximum);
        var names = new List<string>(Math.Min(maximum, 200));
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var name in cursor.Current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (names.Count == maximum)
                    return new BoundedMetadataResult<string>(names, true);
                names.Add(name);
            }
        }
        return new BoundedMetadataResult<string>(names, false);
    }

    public async Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        var target = (await ClientAsync(profile, cancellationToken).ConfigureAwait(false)).GetDatabase(database);
        try
        {
            using var cursor = await target.ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", collection) }, cancellationToken).ConfigureAwait(false);
            return (await cursor.ToListAsync(cancellationToken).ConfigureAwait(false)).Select(ToDefinition).SingleOrDefault();
        }
        catch (MongoCommandException exception) when (exception.Code == UnauthorizedCode)
        {
            // Without the listCollections privilege type and validator stay unknown; names still come from authorizedCollections.
            return new CollectionDefinition(collection, CollectionKind.Unknown, null);
        }
    }

    public async Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        var client = await ClientAsync(profile, cancellationToken).ConfigureAwait(false);
        using var cursor = await client.GetDatabase(database).GetCollection<BsonDocument>(collection).Indexes.ListAsync(cancellationToken).ConfigureAwait(false);
        return (await cursor.ToListAsync(cancellationToken).ConfigureAwait(false)).Select(index => ExplorerMetadataService.ParseIndex(index.ToJson(CanonicalJson))).ToArray();
    }

    public async Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var client = await ClientAsync(profile, cancellationToken).ConfigureAwait(false);
        var aggregate = new AggregateOptions { MaxTime = TimeSpan.FromMilliseconds(options.MaxTimeMs), Comment = SampleComment, BatchSize = options.Size };
        using var cursor = await client.GetDatabase(database).GetCollection<BsonDocument>(collection)
            .AggregateAsync<BsonDocument>(BuildSamplePipeline(options), aggregate, cancellationToken).ConfigureAwait(false);
        return (await cursor.ToListAsync(cancellationToken).ConfigureAwait(false)).Select(ParseSample).ToArray();
    }

    public async Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(
        ConnectionProfile profile, string database, string collection, SchemaSampleOptions options,
        int maximumProjectedBytes, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (options.Size > 100 || maximumProjectedBytes is < 1 or > 256 * 1024)
            throw new ArgumentOutOfRangeException(nameof(maximumProjectedBytes));

        // Dynamic ENV/cofre resolution happens exactly once. Definition and sample stay on the same Mongo client.
        var client = await ClientAsync(profile, cancellationToken).ConfigureAwait(false);
        var target = client.GetDatabase(database);
        var originalUuid = await ReadConcreteCollectionUuidAsync(target, collection, cancellationToken)
            .ConfigureAwait(false);
        if (originalUuid is null)
            return new(ConcreteCollectionSchemaSampleStatus.TargetNotVerifiable, []);

        var aggregate = new AggregateOptions
        {
            MaxTime = TimeSpan.FromMilliseconds(options.MaxTimeMs),
            Comment = AgentSampleComment,
            BatchSize = 1
        };
        var samples = new List<SampledDocument>(options.Size);
        var projectedBytes = 0;
        using (var cursor = await target.GetCollection<BsonDocument>(collection)
                   .AggregateAsync<BsonDocument>(BuildSamplePipeline(options), aggregate, cancellationToken)
                   .ConfigureAwait(false))
        {
            while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var document in cursor.Current)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (samples.Count == options.Size)
                        return new(ConcreteCollectionSchemaSampleStatus.LimitExceeded, []);
                    var length = document.ToBson().Length;
                    if (length > maximumProjectedBytes - projectedBytes)
                        return new(ConcreteCollectionSchemaSampleStatus.LimitExceeded, []);
                    projectedBytes += length;
                    samples.Add(ParseSample(document));
                }
            }
        }

        // A replacement view or collection gets a different identity. Never release evidence if verification
        // becomes unavailable or the namespace no longer names the same concrete collection.
        var currentUuid = await ReadConcreteCollectionUuidAsync(target, collection, cancellationToken)
            .ConfigureAwait(false);
        if (currentUuid is null || !originalUuid.AsSpan().SequenceEqual(currentUuid))
            return new(ConcreteCollectionSchemaSampleStatus.TargetNotVerifiable, []);
        return new(ConcreteCollectionSchemaSampleStatus.Sampled, samples);
    }

    internal static async Task<byte[]?> ReadConcreteCollectionUuidAsync(
        IMongoDatabase target, string collection, CancellationToken cancellationToken)
    {
        try
        {
            using var definitions = await target.ListCollectionsAsync(
                new ListCollectionsOptions { Filter = new BsonDocument("name", collection) },
                cancellationToken).ConfigureAwait(false);
            byte[]? identity = null;
            while (await definitions.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var definition in definitions.Current)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (identity is not null ||
                        !definition.TryGetValue("name", out var name) || !name.IsString ||
                        !string.Equals(name.AsString, collection, StringComparison.Ordinal) ||
                        !definition.TryGetValue("type", out var kind) || !kind.IsString ||
                        !string.Equals(kind.AsString, "collection", StringComparison.Ordinal) ||
                        !definition.TryGetValue("info", out var infoValue) || infoValue is not BsonDocument info ||
                        !info.TryGetValue("uuid", out var uuidValue) || uuidValue is not BsonBinaryData uuid ||
                        uuid.SubType != BsonBinarySubType.UuidStandard || uuid.Bytes.Length != 16)
                        return null;
                    identity = uuid.Bytes.ToArray();
                }
            }
            return identity;
        }
        catch (MongoCommandException exception) when (exception.Code == UnauthorizedCode)
        {
            return null;
        }
    }

    /// <summary>$sample followed by a projection of key names and $type values up to the requested depth.</summary>
    internal static BsonDocument[] BuildSamplePipeline(SchemaSampleOptions options) =>
    [
        new("$sample", new BsonDocument("size", options.Size)),
        new("$project", new BsonDocument { ["_id"] = 0, ["f"] = Fields("$$ROOT", options.Depth, 0) })
    ];

    internal static SampledDocument ParseSample(BsonDocument document) =>
        new(document.TryGetValue("f", out var fields) && fields.IsBsonArray ? ParseFields(fields.AsBsonArray) : []);

    private static BsonDocument Fields(string variable, int depth, int level)
    {
        var item = "p" + level;
        var element = "i" + level;
        var value = "$$" + item + ".v";
        var entry = new BsonDocument { ["k"] = "$$" + item + ".k", ["t"] = new BsonDocument("$type", value) };
        if (depth > 1)
        {
            entry["c"] = Condition(new BsonDocument("$type", value), "object", Fields(value, depth - 1, level + 1));
            var elementType = new BsonDocument("$type", "$$" + element);
            entry["e"] = Condition(new BsonDocument("$type", value), "array", new BsonDocument("$map", new BsonDocument
            {
                ["input"] = new BsonDocument("$slice", new BsonArray { value, 5 }),
                ["as"] = element,
                ["in"] = new BsonDocument { ["t"] = elementType, ["c"] = Condition(elementType, "object", Fields("$$" + element, depth - 1, level + 1)) }
            }));
        }
        return new BsonDocument("$map", new BsonDocument
        {
            ["input"] = new BsonDocument("$objectToArray", variable),
            ["as"] = item,
            ["in"] = entry
        });
    }

    // $cond evaluates only the selected branch, so $objectToArray and $slice never see incompatible values.
    private static BsonDocument Condition(BsonDocument type, string expected, BsonDocument then) =>
        new("$cond", new BsonArray { new BsonDocument("$eq", new BsonArray { type, expected }), then, "$$REMOVE" });

    private static SampledField[] ParseFields(BsonArray fields) => fields.OfType<BsonDocument>()
        .Where(field => field.TryGetValue("k", out var name) && name.IsString)
        .Select(field => new SampledField(field["k"].AsString, field.GetValue("t", "unknown").ToString()!,
            field.TryGetValue("c", out var children) && children.IsBsonArray ? ParseFields(children.AsBsonArray) : [],
            field.TryGetValue("e", out var elements) && elements.IsBsonArray
                ? elements.AsBsonArray.OfType<BsonDocument>().Select(element => new SampledElement(element.GetValue("t", "unknown").ToString()!,
                    element.TryGetValue("c", out var nested) && nested.IsBsonArray ? ParseFields(nested.AsBsonArray) : [])).ToArray()
                : []))
        .ToArray();

    private static CollectionDefinition ToDefinition(BsonDocument document)
    {
        var validator = document.TryGetValue("options", out var options) && options.IsBsonDocument
            && options.AsBsonDocument.TryGetValue("validator", out var value) && value.IsBsonDocument ? value.ToJson(CanonicalJson) : null;
        return new(document["name"].AsString, KindOf(document), validator);
    }

    private static CollectionKind KindOf(BsonDocument document) => document.GetValue("type", "collection").ToString() switch
    {
        "view" => CollectionKind.View,
        "timeseries" => CollectionKind.TimeSeries,
        "collection" => CollectionKind.Collection,
        _ => CollectionKind.Unknown
    };

    private async Task<MongoClient> ClientAsync(ConnectionProfile profile, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.ConnectionString);
        var environment = new OperationEnvironment(environments, secrets, profile.Id, credentialStore);
        await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
        await environment.PrepareAsync(profile, cancellationToken).ConfigureAwait(false);
        return (clients ?? MongoClientPool.Shared).Get(MongoClientSettings.FromConnectionString(environment.ResolvedConnection));
    }
}
