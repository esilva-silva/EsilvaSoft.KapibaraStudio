using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Executes literal EJSON find queries without the trusted console's ENV or constructor rewriting.</summary>
public sealed class MongoAgentFindSource(
    IConnectionSecretStore secrets,
    IEnvironmentVaultRepository? environments,
    MongoClientPool clients) : IAgentMongoFindSource, IAgentMongoCountSource, IAgentMongoDistinctSource
{
    private const int MaximumDocumentBytes = 256 * 1024;
    private static readonly JsonWriterSettings CanonicalJson = new()
    {
        OutputMode = JsonOutputMode.CanonicalExtendedJson
    };

    public async Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(query);
        if (query.Limit is < 1 or > 100 || query.Skip is < 0 or > 10_000 ||
            query.MaxTimeMs is < 1 or > 30_000)
            throw new ArgumentOutOfRangeException(nameof(query));

        var filter = ParseLiteral(query.FilterEjson);
        var projection = query.ProjectionEjson is null ? null : ParseLiteral(query.ProjectionEjson);
        var sort = query.SortEjson is null ? null : ParseLiteral(query.SortEjson);
        return await FindParsedAsync(profile, query.Database, query.Collection, filter, projection, sort,
            query.Limit, query.Skip, query.MaxTimeMs, cancellationToken).ConfigureAwait(false);
    }

    public async Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(query);
        if (query.MaxTimeMs is < 1 or > 30_000)
            throw new ArgumentOutOfRangeException(nameof(query));
        var id = ParseLiteralValue(query.IdEjson);
        // $eq keeps a document-shaped ID literal; fields named like operators cannot change query semantics.
        var filter = CreateIdFilter(id);
        return await FindParsedAsync(profile, query.Database, query.Collection, filter, null, null,
            1, 0, query.MaxTimeMs, cancellationToken).ConfigureAwait(false);
    }

    private async Task<AgentMongoFindPage> FindParsedAsync(ConnectionProfile profile,
        string databaseName, string collectionName, BsonDocument filter, BsonDocument? projection,
        BsonDocument? sort, int limit, int skip, int maxTimeMs, CancellationToken cancellationToken)
    {
        // The operation context resolves only the connection string. Its dynamic JSON parser is never used here.
        var context = await MongoOperationContext.PrepareAsync(profile, secrets, environments, clients,
            cancellationToken).ConfigureAwait(false);
        var database = context.CreateClient().GetDatabase(databaseName);
        var originalUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, collectionName,
            cancellationToken).ConfigureAwait(false);
        if (originalUuid is null) return new([], false, false, false, false);

        var options = new FindOptions<BsonDocument>
        {
            Limit = limit + 1,
            Skip = skip,
            MaxTime = TimeSpan.FromMilliseconds(maxTimeMs),
            Projection = projection,
            Sort = sort,
            BatchSize = 1,
            Comment = "slopstudio:agent-find"
        };
        var documents = new List<string>(limit);
        var hasMore = false;
        var truncated = false;
        var resultTooLarge = false;
        var projectedBytes = 0;
        using (var cursor = await database.GetCollection<BsonDocument>(collectionName)
                   .FindAsync(filter, options, cancellationToken).ConfigureAwait(false))
        {
            while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var document in cursor.Current)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (documents.Count == limit)
                    {
                        hasMore = true;
                        break;
                    }
                    var length = document.ToBson().Length;
                    if (length > MaximumDocumentBytes || length > MaximumDocumentBytes - projectedBytes)
                    {
                        resultTooLarge = documents.Count == 0;
                        truncated = !resultTooLarge;
                        hasMore = truncated;
                        break;
                    }
                    var canonical = document.ToJson(CanonicalJson);
                    // Account for the wire representation too. JSON escaping is checked again by the registry.
                    var canonicalBytes = System.Text.Encoding.UTF8.GetByteCount(canonical);
                    if (canonicalBytes > MaximumDocumentBytes - projectedBytes)
                    {
                        resultTooLarge = documents.Count == 0;
                        truncated = !resultTooLarge;
                        hasMore = truncated;
                        break;
                    }
                    projectedBytes += Math.Max(length, canonicalBytes);
                    documents.Add(canonical);
                }
                if (hasMore || resultTooLarge) break;
            }
        }

        var currentUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, collectionName,
            cancellationToken).ConfigureAwait(false);
        if (currentUuid is null || !originalUuid.AsSpan().SequenceEqual(currentUuid))
            return new([], false, false, false, false);
        // The cursor asks for limit + 1: only an extra document proves another page. Exactly `limit`
        // documents with an exhausted cursor is a complete result, not a hint to page again.
        return new(documents, hasMore, truncated, true, resultTooLarge);
    }

    public async Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(query);
        if (query.MaxTimeMs is < 1 or > 30_000)
            throw new ArgumentOutOfRangeException(nameof(query));

        var filter = ParseLiteral(query.FilterEjson);
        var context = await MongoOperationContext.PrepareAsync(profile, secrets, environments, clients,
            cancellationToken).ConfigureAwait(false);
        var database = context.CreateClient().GetDatabase(query.Database);
        var originalUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, query.Collection,
            cancellationToken).ConfigureAwait(false);
        if (originalUuid is null) return new(string.Empty, false);

        var count = await database.GetCollection<BsonDocument>(query.Collection)
            .CountDocumentsAsync(filter, new CountOptions
            {
                MaxTime = TimeSpan.FromMilliseconds(query.MaxTimeMs),
                Comment = "slopstudio:agent-count"
            }, cancellationToken).ConfigureAwait(false);

        var currentUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, query.Collection,
            cancellationToken).ConfigureAwait(false);
        if (currentUuid is null || !originalUuid.AsSpan().SequenceEqual(currentUuid))
            return new(string.Empty, false);
        return new(CanonicalCountEjson(count), true);
    }

    public async Task<AgentMongoDistinctPage> DistinctAsync(ConnectionProfile profile,
        AgentMongoDistinctQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(query);
        if (!IsSafeDistinctField(query.Field) || query.MaximumValues is < 1 or > 100 ||
            query.MaxTimeMs is < 1 or > 30_000)
            throw new ArgumentOutOfRangeException(nameof(query));

        var filter = ParseLiteral(query.FilterEjson);
        var context = await MongoOperationContext.PrepareAsync(profile, secrets, environments, clients,
            cancellationToken).ConfigureAwait(false);
        var database = context.CreateClient().GetDatabase(query.Database);
        var originalUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, query.Collection,
            cancellationToken).ConfigureAwait(false);
        if (originalUuid is null) return new([], false, false, false);

        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(BuildDistinctPipeline(query, filter));
        var options = new AggregateOptions
        {
            MaxTime = TimeSpan.FromMilliseconds(query.MaxTimeMs),
            BatchSize = 1,
            AllowDiskUse = false,
            Comment = "slopstudio:agent-distinct"
        };
        var values = new List<string>(query.MaximumValues);
        var truncated = false;
        AgentMongoDistinctTruncationReason? truncationReason = null;
        var resultTooLarge = false;
        var usedBytes = 0;
        using (var cursor = await database.GetCollection<BsonDocument>(query.Collection)
                   .AggregateAsync(pipeline, options, cancellationToken).ConfigureAwait(false))
        {
            while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var row in cursor.Current)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (values.Count == query.MaximumValues)
                    {
                        truncated = true;
                        truncationReason = AgentMongoDistinctTruncationReason.ValueLimit;
                        break;
                    }
                    if (!row.TryGetValue("_id", out var id))
                        throw new FormatException("Resultado distinct sem valor BSON.");
                    var ejson = id.ToJson(CanonicalJson);
                    var itemBytes = System.Text.Encoding.UTF8.GetByteCount(ejson);
                    if (itemBytes > MaximumDocumentBytes - usedBytes)
                    {
                        resultTooLarge = values.Count == 0;
                        truncated = !resultTooLarge;
                        if (truncated) truncationReason = AgentMongoDistinctTruncationReason.OutputLimit;
                        break;
                    }
                    usedBytes += itemBytes;
                    values.Add(ejson);
                }
                if (truncated || resultTooLarge) break;
            }
        }

        var currentUuid = await MongoMetadataSource.ReadConcreteCollectionUuidAsync(database, query.Collection,
            cancellationToken).ConfigureAwait(false);
        if (currentUuid is null || !originalUuid.AsSpan().SequenceEqual(currentUuid))
            return new([], false, false, false);
        return new(values, truncated, true, resultTooLarge, truncationReason);
    }

    internal static BsonDocument[] BuildDistinctPipeline(AgentMongoDistinctQuery query, BsonDocument filter)
    {
        if (!IsSafeDistinctField(query.Field) || query.MaximumValues is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(query));
        return
        [
            new BsonDocument("$match", filter),
            new BsonDocument("$group", new BsonDocument("_id", "$" + query.Field)),
            new BsonDocument("$limit", query.MaximumValues + 1)
        ];
    }

    internal static bool IsSafeDistinctField(string? field) =>
        field is { Length: > 0 and <= 1_024 } &&
        System.Text.Encoding.UTF8.GetByteCount(field) <= 1_024 &&
        !field.Contains('$') && !field.Any(char.IsControl) &&
        field.Split('.').All(segment => !string.IsNullOrWhiteSpace(segment));

    internal static string CanonicalCountEjson(long count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        using var canonical = JsonDocument.Parse(new BsonInt64(count).ToJson(CanonicalJson));
        return JsonSerializer.Serialize(canonical.RootElement);
    }

    internal static BsonDocument ParseLiteral(string ejson)
    {
        // Defense in depth: the handler re-applies the registry's closed codec before BSON parsing.
        if (!AgentToolLiteralEjson.IsQueryFilter(ejson))
            throw new FormatException("Extended JSON fora do conjunto literal permitido.");
        using var parsed = JsonDocument.Parse(ejson, new JsonDocumentOptions { MaxDepth = 64 });
        if (parsed.RootElement.ValueKind != JsonValueKind.Object || HasDuplicateProperties(parsed.RootElement))
            throw new FormatException("É necessário um objeto Extended JSON sem propriedades duplicadas.");
        var document = BsonDocument.Parse(ejson);
        if (ContainsCode(document)) throw new FormatException("Extended JSON com código não é permitido.");
        return document;
    }

    internal static BsonValue ParseLiteralValue(string ejson)
    {
        if (!AgentToolLiteralEjson.IsLiteralValue(ejson))
            throw new FormatException("Extended JSON fora do conjunto literal permitido.");
        using var parsed = JsonDocument.Parse(ejson, new JsonDocumentOptions { MaxDepth = 64 });
        if (HasDuplicateProperties(parsed.RootElement))
            throw new FormatException("Extended JSON com propriedades duplicadas não é permitido.");
        var value = BsonSerializer.Deserialize<BsonValue>(ejson);
        if (ContainsCode(value)) throw new FormatException("Extended JSON com código não é permitido.");
        return value;
    }

    internal static BsonDocument CreateIdFilter(BsonValue id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return new BsonDocument("_id", new BsonDocument("$eq", id));
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
            return element.EnumerateArray().Any(HasDuplicateProperties);
        if (element.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return element.EnumerateObject().Any(property =>
            !seen.Add(property.Name) || HasDuplicateProperties(property.Value));
    }

    private static bool ContainsCode(BsonValue value) => value switch
    {
        BsonJavaScript or BsonJavaScriptWithScope => true,
        BsonDocument document => document.Elements.Any(element =>
            element.Name is "$where" or "$function" or "$accumulator" or "$eval" or "$lookup" or "$out" or "$merge" ||
            ContainsCode(element.Value)),
        BsonArray array => array.Any(ContainsCode),
        _ => false
    };
}
