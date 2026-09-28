using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Executa consultas find/aggregate, contagens, valores distintos e seus respectivos explains.</summary>
internal static class MongoQueryExecutor
{
    public static async Task<QueryPage> QueryAsync(MongoOperationContext context, MongoQuery query, CancellationToken cancellationToken)
    {
        query.Validate();
        var startedAt = Stopwatch.GetTimestamp();
        var filter = context.ParseDocument(query.FilterJson, "filtro");
        var options = new FindOptions<BsonDocument>
        {
            Limit = query.Limit,
            Skip = query.Skip,
            MaxTime = query.MaxTimeMs is null ? null : TimeSpan.FromMilliseconds(query.MaxTimeMs.Value),
            Projection = context.ParseOptionalDocument(query.ProjectionJson, "projeção"),
            Sort = context.ParseOptionalDocument(query.SortJson, "ordenação"),
            Hint = context.ParseOptionalDocument(query.HintJson, "hint"),
            Comment = string.IsNullOrWhiteSpace(query.Comment) ? null : query.Comment.Trim(),
            BatchSize = Math.Min(query.BatchSize ?? 100, query.Limit),
            Collation = context.ParseOptionalCollation(query.CollationJson)
        };
        var collection = context.GetCollection(query.Database, query.Collection);
        using var cursor = await collection.FindAsync(filter, options, cancellationToken).ConfigureAwait(false);
        var documents = new List<string>(query.Limit);

        var characters = 0L;
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var document in cursor.Current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var json = document.ToJson(MongoJson.CanonicalSettings);
                characters += json.Length;
                if (characters > 8_000_000) throw new InvalidOperationException("A página excede 8 milhões de caracteres. Reduza limit ou use projeção para excluir campos grandes.");
                documents.Add(json);
            }
        }

        return new QueryPage(documents, Stopwatch.GetElapsedTime(startedAt), documents.Count == query.Limit);
    }

    public static async Task<CollectionCountResult> CountDocumentsAsync(MongoOperationContext context, CollectionCountRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var startedAt = Stopwatch.GetTimestamp();
        var collection = context.GetCollection(request.Database, request.Collection);
        TimeSpan? maxTime = request.MaxTimeMs is null ? null : TimeSpan.FromMilliseconds(request.MaxTimeMs.Value);
        var count = request.UseEstimatedCount
            ? await collection.EstimatedDocumentCountAsync(new EstimatedDocumentCountOptions { MaxTime = maxTime }, cancellationToken).ConfigureAwait(false)
            : await collection.CountDocumentsAsync(context.ParseDocument(request.FilterJson, "filtro"), new CountOptions { MaxTime = maxTime }, cancellationToken).ConfigureAwait(false);

        return new CollectionCountResult(count, request.UseEstimatedCount, Stopwatch.GetElapsedTime(startedAt));
    }

    public static async Task<DistinctValuesResult> GetDistinctValuesAsync(MongoOperationContext context, DistinctValuesRequest request, CancellationToken cancellationToken)
    {
        request.Validate();
        var startedAt = Stopwatch.GetTimestamp();
        var stages = new BsonDocument[]
        {
            new("$match", context.ParseDocument(request.FilterJson, "filtro")),
            new("$group", new BsonDocument("_id", "$" + request.Field)),
            new("$limit", request.MaximumValues + 1)
        };
        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(stages);
        TimeSpan? maxTime = request.MaxTimeMs is null ? null : TimeSpan.FromMilliseconds(request.MaxTimeMs.Value);
        using var cursor = await context.GetCollection(request.Database, request.Collection)
            .AggregateAsync(pipeline, new AggregateOptions { MaxTime = maxTime }, cancellationToken)
            .ConfigureAwait(false);
        var values = new List<string>(request.MaximumValues + 1);

        while (values.Count <= request.MaximumValues && await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var document in cursor.Current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                values.Add(document["_id"].ToJson(MongoJson.CanonicalSettings));
                if (values.Count > request.MaximumValues)
                {
                    break;
                }
            }
        }

        var isTruncated = values.Count > request.MaximumValues;
        if (isTruncated)
        {
            values.RemoveAt(values.Count - 1);
        }

        return new DistinctValuesResult(values, isTruncated, Stopwatch.GetElapsedTime(startedAt));
    }

    public static async Task<string> ExplainAsync(MongoOperationContext context, MongoQuery query, CancellationToken cancellationToken)
    {
        query.Validate();
        var commandBody = new BsonDocument
        {
            ["find"] = query.Collection,
            ["filter"] = context.ParseDocument(query.FilterJson, "filtro"),
            ["limit"] = query.Limit,
            ["skip"] = query.Skip
        };

        var projection = context.ParseOptionalDocument(query.ProjectionJson, "projeção");
        if (projection is not null)
        {
            commandBody["projection"] = projection;
        }

        var sort = context.ParseOptionalDocument(query.SortJson, "ordenação");
        if (sort is not null)
        {
            commandBody["sort"] = sort;
        }

        var hint = context.ParseOptionalDocument(query.HintJson, "hint");
        if (hint is not null)
        {
            commandBody["hint"] = hint;
        }

        if (query.MaxTimeMs is not null)
        {
            commandBody["maxTimeMS"] = query.MaxTimeMs.Value;
        }
        if (!string.IsNullOrWhiteSpace(query.Comment))
        {
            commandBody["comment"] = query.Comment.Trim();
        }
        if (query.BatchSize is not null)
        {
            commandBody["batchSize"] = query.BatchSize.Value;
        }
        var collation = context.ParseOptionalDocument(query.CollationJson, "collation");
        if (collation is not null)
        {
            commandBody["collation"] = collation;
        }

        var explain = new BsonDocument
        {
            ["explain"] = commandBody,
            ["verbosity"] = "executionStats"
        };
        var result = await context.CreateClient()
            .GetDatabase(query.Database)
            .RunCommandAsync<BsonDocument>(explain, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return result.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<QueryPage> AggregateAsync(MongoOperationContext context, AggregationQuery query, CancellationToken cancellationToken)
    {
        try { return await AggregateCoreAsync(context, query, cancellationToken).ConfigureAwait(false); }
        catch (MongoCommandException exception) { throw QueryServerDiagnostics.Describe(exception); }
    }

    private static async Task<QueryPage> AggregateCoreAsync(MongoOperationContext context, AggregationQuery query, CancellationToken cancellationToken)
    {
        query.Validate();
        var stages = context.ParsePipeline(query.PipelineJson);
        AggregationPipelineValidator.ValidateReadPipeline(new BsonArray(stages));
        var startedAt = Stopwatch.GetTimestamp();
        var pipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(stages);
        using var cursor = await context.GetCollection(query.Database, query.Collection)
            .AggregateAsync(pipeline, new AggregateOptions { BatchSize = Math.Min(query.Limit, 100), MaxTime = TimeSpan.FromMinutes(5) }, cancellationToken)
            .ConfigureAwait(false);
        var documents = new List<string>(Math.Min(query.Limit, 1_000));

        var characters = 0L;
        while (documents.Count < query.Limit && await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var document in cursor.Current)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (documents.Count == query.Limit)
                {
                    break;
                }

                var json = document.ToJson(MongoJson.CanonicalSettings);
                characters += json.Length;
                if (characters > 8_000_000) throw new InvalidOperationException("A página excede 8 milhões de caracteres. Reduza limit ou use projeção para excluir campos grandes.");
                documents.Add(json);
            }
        }

        return new QueryPage(documents, Stopwatch.GetElapsedTime(startedAt), documents.Count == query.Limit);
    }

    public static async Task<string> ExplainAggregationAsync(MongoOperationContext context, AggregationQuery query, CancellationToken cancellationToken)
    {
        try { return await ExplainAggregationCoreAsync(context, query, cancellationToken).ConfigureAwait(false); }
        catch (MongoCommandException exception) { throw QueryServerDiagnostics.Describe(exception); }
    }

    private static async Task<string> ExplainAggregationCoreAsync(MongoOperationContext context, AggregationQuery query, CancellationToken cancellationToken)
    {
        query.Validate();
        var pipeline = new BsonArray(context.ParsePipeline(query.PipelineJson));
        AggregationPipelineValidator.ValidateReadPipeline(pipeline);
        var command = new BsonDocument
        {
            ["explain"] = new BsonDocument { ["aggregate"] = query.Collection, ["pipeline"] = pipeline, ["cursor"] = new BsonDocument() },
            ["verbosity"] = "queryPlanner",
            ["maxTimeMS"] = 30000
        };
        var result = await context.CreateClient().GetDatabase(query.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken).ConfigureAwait(false);
        return result.ToJson(MongoJson.CanonicalSettings);
    }
}
