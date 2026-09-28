using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Lista, cria, oculta/exibe e remove índices de uma coleção.</summary>
internal static class MongoIndexManager
{
    public static async Task<IReadOnlyList<string>> GetIndexesAsync(MongoOperationContext context, string database, string collection, CancellationToken cancellationToken)
    {
        using var cursor = await context.GetCollection(database, collection).Indexes.ListAsync(cancellationToken).ConfigureAwait(false);
        var indexes = new List<string>();

        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            indexes.AddRange(cursor.Current.Select(index => index.ToJson(MongoJson.CanonicalSettings)));
        }

        return indexes;
    }

    public static async Task<IReadOnlyList<string>> GetIndexUsageStatsAsync(MongoOperationContext context, string database, string collection, CancellationToken cancellationToken)
    {
        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create([new BsonDocument("$indexStats", new BsonDocument())]);
        using var cursor = await context.GetCollection(database, collection).AggregateAsync(pipeline, cancellationToken: cancellationToken).ConfigureAwait(false);
        var statistics = new List<string>();
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            statistics.AddRange(cursor.Current.Select(item => item.ToJson(MongoJson.CanonicalSettings)));
        }

        return statistics;
    }

    public static async Task<string> CreateIndexAsync(MongoOperationContext context, IndexCreateRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var keys = context.ParseDocument(request.KeysJson, "chaves do índice");

        if (keys.ElementCount == 0)
        {
            throw new ArgumentException("O índice precisa de pelo menos uma chave.", nameof(request));
        }

        var options = new CreateIndexOptions<BsonDocument>
        {
            Name = string.IsNullOrWhiteSpace(request.Name) ? null : request.Name.Trim(),
            Unique = request.IsUnique,
            Sparse = request.IsSparse,
            ExpireAfter = request.ExpireAfterSeconds is null ? null : TimeSpan.FromSeconds(request.ExpireAfterSeconds.Value),
            PartialFilterExpression = context.ParseOptionalDocument(request.PartialFilterJson, "filtro parcial"),
            Collation = context.ParseOptionalCollation(request.CollationJson),
            Hidden = request.IsHidden,
            WildcardProjection = context.ParseOptionalDocument(request.WildcardProjectionJson, "projeção wildcard")
        };
        var model = new CreateIndexModel<BsonDocument>(new BsonDocumentIndexKeysDefinition<BsonDocument>(keys), options);
        return await context.GetCollection(request.Database, request.Collection)
            .Indexes.CreateOneAsync(model, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task SetIndexVisibilityAsync(MongoOperationContext context, IndexVisibilityRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var command = new BsonDocument
        {
            ["collMod"] = request.Collection,
            ["index"] = new BsonDocument
            {
                ["name"] = request.Name.Trim(),
                ["hidden"] = request.IsHidden
            }
        };
        await context.CreateClient()
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task DropIndexAsync(MongoOperationContext context, IndexDropRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        await context.GetCollection(request.Database, request.Collection)
            .Indexes.DropOneAsync(request.Name.Trim(), cancellationToken)
            .ConfigureAwait(false);
    }
}
