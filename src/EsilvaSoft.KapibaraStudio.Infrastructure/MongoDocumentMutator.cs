using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Insere, substitui, atualiza e remove documentos de uma coleção.</summary>
internal static class MongoDocumentMutator
{
    public static async Task<DocumentMutationResult> InsertAsync(MongoOperationContext context, string database, string collection, string documentJson, CancellationToken cancellationToken)
    {
        var document = context.ParseDocument(documentJson, "documento");
        var target = context.GetCollection(database, collection);
        await target.InsertOneAsync(document, cancellationToken: cancellationToken).ConfigureAwait(false);
        return new DocumentMutationResult(1, 1, document.TryGetValue("_id", out var id) ? id.ToJson(MongoJson.CanonicalSettings) : null);
    }

    public static async Task<long> InsertManyAsync(MongoOperationContext context, BulkInsertRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var array = context.ParseArray(request.DocumentsJson, "array de documentos");

        if (array.Count is < 1 or > 10_000 || array.Count > request.MaximumDocuments)
        {
            throw new ArgumentOutOfRangeException(nameof(request), $"O lote contém {array.Count} documentos; o limite configurado é {request.MaximumDocuments}.");
        }

        if (array.Any(value => !value.IsBsonDocument))
        {
            throw new ArgumentException("Cada item do lote precisa ser um documento BSON.", nameof(request));
        }

        var documents = array.Select(value => value.AsBsonDocument).ToArray();
        await context.GetCollection(request.Database, request.Collection)
            .InsertManyAsync(documents, new InsertManyOptions { IsOrdered = request.Ordered }, cancellationToken)
            .ConfigureAwait(false);
        return documents.LongLength;
    }

    public static async Task<DocumentMutationResult> ReplaceAsync(MongoOperationContext context, string database, string collection, string filterJson, string documentJson, CancellationToken cancellationToken)
    {
        var filter = context.ParseNonEmptyFilter(filterJson);
        var document = context.ParseDocument(documentJson, "documento");
        var result = await context.GetCollection(database, collection)
            .ReplaceOneAsync(filter, document, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return new DocumentMutationResult(result.MatchedCount, result.ModifiedCount);
    }

    public static async Task<DocumentMutationResult> UpdateAsync(MongoOperationContext context, DocumentUpdateRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var filter = context.ParseNonEmptyFilter(request.FilterJson);
        var options = new UpdateOptions
        {
            IsUpsert = request.Upsert,
            ArrayFilters = string.IsNullOrWhiteSpace(request.ArrayFiltersJson)
                ? null
                : context.ParsePipeline(request.ArrayFiltersJson)
                    .Select(arrayFilter => (ArrayFilterDefinition<BsonDocument>)new BsonDocumentArrayFilterDefinition<BsonDocument>(arrayFilter))
                    .ToArray()
        };
        var collection = context.GetCollection(request.Database, request.Collection);
        UpdateResult result;
        if (request.UpdateJson.TrimStart().StartsWith('['))
        {
            var pipeline = context.ParsePipeline(request.UpdateJson);
            result = await collection
                .UpdateOneAsync(filter, new PipelineUpdateDefinition<BsonDocument>(pipeline), options, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var update = context.ParseDocument(request.UpdateJson, "atualização");
            result = await collection
                .UpdateOneAsync(filter, new BsonDocumentUpdateDefinition<BsonDocument>(update), options, cancellationToken)
                .ConfigureAwait(false);
        }

        return new DocumentMutationResult(result.MatchedCount, result.ModifiedCount, result.UpsertedId?.ToString());
    }

    public static async Task<FindAndModifyResult> FindAndModifyAsync(MongoOperationContext context, DocumentUpdateRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var filter = context.ParseNonEmptyFilter(request.FilterJson);
        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            IsUpsert = request.Upsert,
            ReturnDocument = ReturnDocument.After,
            ArrayFilters = string.IsNullOrWhiteSpace(request.ArrayFiltersJson)
                ? null
                : context.ParsePipeline(request.ArrayFiltersJson)
                    .Select(value => (ArrayFilterDefinition<BsonDocument>)new BsonDocumentArrayFilterDefinition<BsonDocument>(value))
                    .ToArray()
        };
        var collection = context.GetCollection(request.Database, request.Collection);
        BsonDocument? document;
        if (request.UpdateJson.TrimStart().StartsWith('['))
        {
            document = await collection.FindOneAndUpdateAsync(filter, new PipelineUpdateDefinition<BsonDocument>(context.ParsePipeline(request.UpdateJson)), options, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            document = await collection.FindOneAndUpdateAsync(filter, new BsonDocumentUpdateDefinition<BsonDocument>(context.ParseDocument(request.UpdateJson, "atualização")), options, cancellationToken).ConfigureAwait(false);
        }

        return new FindAndModifyResult(document?.ToJson(MongoJson.CanonicalSettings));
    }

    public static async Task<DocumentMutationResult> DeleteAsync(MongoOperationContext context, string database, string collection, string filterJson, CancellationToken cancellationToken)
    {
        var filter = context.ParseNonEmptyFilter(filterJson);
        var result = await context.GetCollection(database, collection)
            .DeleteOneAsync(filter, cancellationToken)
            .ConfigureAwait(false);
        return new DocumentMutationResult(result.DeletedCount, result.DeletedCount);
    }

    public static async Task<DocumentMutationResult> DeleteManyAsync(MongoOperationContext context, string database, string collection, string filterJson, CancellationToken cancellationToken)
    {
        var filter = context.ParseNonEmptyFilter(filterJson);
        var result = await context.GetCollection(database, collection)
            .DeleteManyAsync(filter, cancellationToken)
            .ConfigureAwait(false);
        return new DocumentMutationResult(result.DeletedCount, result.DeletedCount);
    }
}
