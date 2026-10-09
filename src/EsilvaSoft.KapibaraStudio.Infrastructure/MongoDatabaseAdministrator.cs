using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Cria, renomeia e remove bancos/coleções/views, e administra validação e estatísticas de esquema.</summary>
internal static class MongoDatabaseAdministrator
{
    public static async Task CreateDatabaseAsync(MongoOperationContext context, ConnectionProfile profile, DatabaseCreateRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        await context.CreateClient()
            .GetDatabase(request.Database)
            .CreateCollectionAsync(request.InitialCollection, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static async Task CreateCollectionAsync(MongoOperationContext context, ConnectionProfile profile, CollectionCreateRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var database = context.CreateClient().GetDatabase(request.Database);
        if (!string.IsNullOrWhiteSpace(request.ViewOn))
        {
            await EnsureViewSourceChainAsync(database, request.Collection, request.ViewOn, cancellationToken).ConfigureAwait(false);
            var command = new BsonDocument
            {
                ["create"] = request.Collection,
                ["viewOn"] = request.ViewOn,
                ["pipeline"] = new BsonArray(context.ParsePipeline(request.ViewPipelineJson!))
            };
            var collation = context.ParseOptionalDocument(request.CollationJson, "collation");
            if (collation is not null)
            {
                command["collation"] = collation;
            }
            await database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken).ConfigureAwait(false);
            return;
        }

        await database.CreateCollectionAsync(
            request.Collection,
            new CreateCollectionOptions<BsonDocument>
            {
                Capped = request.IsCapped,
                MaxSize = request.MaxSizeBytes,
                MaxDocuments = request.MaxDocuments,
                Collation = context.ParseOptionalCollation(request.CollationJson),
                ClusteredIndex = request.IsClustered
                    ? new ClusteredIndexOptions<BsonDocument>
                    {
                        Key = new BsonDocumentIndexKeysDefinition<BsonDocument>(context.ParseDocument(request.ClusteredIndexKeyJson!, "chave clustered")),
                        Unique = true
                    }
                    : null
            },
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task RenameCollectionAsync(MongoOperationContext context, ConnectionProfile profile, CollectionRenameRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        await context.CreateClient()
            .GetDatabase(request.Database)
            .RenameCollectionAsync(request.SourceCollection, request.TargetCollection, new RenameCollectionOptions { DropTarget = request.DropTarget }, cancellationToken).ConfigureAwait(false);
    }

    public static async Task UpdateViewAsync(MongoOperationContext context, ConnectionProfile profile, ViewUpdateRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var database = context.CreateClient().GetDatabase(request.Database);
        var definition = await FindCollectionDefinitionAsync(database, request.View, cancellationToken).ConfigureAwait(false);
        if (definition?.GetValue("type", string.Empty).AsString != "view")
        {
            throw new InvalidOperationException($"{request.View} não é uma view existente.");
        }

        var viewOn = request.SourceCollection
            ?? definition.GetValue("options", new BsonDocument()).AsBsonDocument.GetValue("viewOn", string.Empty).AsString;
        await EnsureViewSourceChainAsync(database, request.View, viewOn, cancellationToken).ConfigureAwait(false);

        var command = new BsonDocument
        {
            ["collMod"] = request.View,
            ["pipeline"] = new BsonArray(context.ParsePipeline(request.PipelineJson))
        };
        if (request.SourceCollection is not null)
        {
            command["viewOn"] = request.SourceCollection;
        }

        await database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public static async Task<ViewMaterializationResult> MaterializeViewAsync(
        MongoOperationContext context,
        ConnectionProfile profile,
        ViewMaterializationRequest request,
        CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var database = context.CreateClient().GetDatabase(request.Database);
        var viewDefinition = await FindCollectionDefinitionAsync(database, request.View, cancellationToken).ConfigureAwait(false);
        var destinationDefinition = await FindCollectionDefinitionAsync(database, request.Destination, cancellationToken).ConfigureAwait(false);
        if (viewDefinition?.GetValue("type", string.Empty).AsString != "view")
            throw new InvalidOperationException("A origem não é uma view existente.");
        if (!string.Equals(viewDefinition.ToJson(MongoJson.CanonicalSettings), request.ExpectedViewDefinitionJson, StringComparison.Ordinal)
            || !string.Equals(destinationDefinition?.ToJson(MongoJson.CanonicalSettings) ?? "{}",
                request.ExpectedDestinationDefinitionJson, StringComparison.Ordinal))
            throw new InvalidOperationException("A definição da view ou do destino mudou após a prévia. Revise novamente.");
        if (destinationDefinition is not null)
        {
            if (destinationDefinition.GetValue("type", string.Empty).AsString != "collection")
                throw new InvalidOperationException("O destino existente não é uma coleção comum.");
            var destinationOptions = destinationDefinition.GetValue("options", new BsonDocument()).AsBsonDocument;
            if ((destinationOptions.TryGetValue("capped", out var capped) && capped.IsBoolean && capped.AsBoolean)
                || destinationOptions.Contains("clusteredIndex")
                || destinationOptions.Contains("timeseries"))
                throw new NotSupportedException("A substituição de coleções capped, clustered ou timeseries não está liberada.");
        }

        var viewOptions = viewDefinition.GetValue("options", new BsonDocument()).AsBsonDocument;
        var source = viewOptions.GetValue("viewOn", string.Empty).AsString;
        var dependencies = await EnsureViewSourceChainAsync(database, request.View, source, cancellationToken).ConfigureAwait(false);
        if (dependencies.Chain.Contains(request.Destination, StringComparer.Ordinal))
            throw new InvalidOperationException("O destino faz parte da cadeia de origem da view.");

        foreach (var name in new[] { request.View }.Concat(dependencies.Chain))
        {
            var definition = string.Equals(name, request.View, StringComparison.Ordinal)
                ? viewDefinition
                : await FindCollectionDefinitionAsync(database, name, cancellationToken).ConfigureAwait(false);
            if (definition?.GetValue("type", string.Empty).AsString != "view")
                continue;
            var options = definition.GetValue("options", new BsonDocument()).AsBsonDocument;
            var pipeline = options.GetValue("pipeline", new BsonArray()).AsBsonArray;
            ViewMaterializationPipelineValidator.Validate(pipeline);
        }

        var stages = context.ParsePipeline(request.PipelineJson);
        ViewMaterializationPipelineValidator.Validate(new BsonArray(stages));
        var writeStages = stages.Append(new BsonDocument("$out", request.Destination)).ToArray();
        var writePipeline = PipelineDefinition<BsonDocument, BsonDocument>.Create(writeStages);
        using var cursor = await database.GetCollection<BsonDocument>(request.View)
            .AggregateAsync(writePipeline, new AggregateOptions { MaxTime = TimeSpan.FromMinutes(5), BatchSize = 100 }, cancellationToken)
            .ConfigureAwait(false);
        while (await cursor.MoveNextAsync(cancellationToken).ConfigureAwait(false))
        {
            // $out emits no documents; draining the cursor waits for the server operation to finish.
        }

        var observed = await FindCollectionDefinitionAsync(database, request.Destination, cancellationToken).ConfigureAwait(false);
        if (observed?.GetValue("type", string.Empty).AsString != "collection")
            throw new InvalidOperationException("O comando terminou, mas a coleção de destino não pôde ser verificada.");
        return new ViewMaterializationResult(
            destinationDefinition is not null,
            observed.ToJson(MongoJson.CanonicalSettings));
    }

    public static async Task ConfigureCollectionValidationAsync(MongoOperationContext context, ConnectionProfile profile, CollectionValidationRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var database = context.CreateClient().GetDatabase(request.Database);
        await MongoCollectionMaintenanceGuard.EnsureCurrentAsync(
            context, request.Preflight!, request.Database, request.Collection, cancellationToken).ConfigureAwait(false);

        var command = new BsonDocument
        {
            ["collMod"] = request.Collection,
            ["validator"] = context.ParseDocument(request.ValidatorJson, "validador"),
            ["validationLevel"] = request.ValidationLevel.ToString().ToLowerInvariant(),
            ["validationAction"] = request.ValidationAction.ToString().ToLowerInvariant()
        };
        await database.RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken).ConfigureAwait(false);
        var observed = await MongoCollectionMaintenanceGuard.ReadPostflightAsync(
            context, request.Database, request.Collection, cancellationToken).ConfigureAwait(false);
        var expectedValidator = context.ParseDocument(request.ValidatorJson, "validador");
        BsonDocument observedValidator;
        try
        {
            observedValidator = BsonDocument.Parse(observed.Validation.ValidatorJson);
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException(
                "O comando collMod foi enviado, mas a releitura do validador é inválida; o efeito pode ter ocorrido.",
                exception);
        }
        if (!expectedValidator.Equals(observedValidator)
            || observed.Validation.ValidationLevel != request.ValidationLevel
            || observed.Validation.ValidationAction != request.ValidationAction)
            throw new InvalidOperationException("O comando collMod foi enviado, mas a configuração relida difere da solicitada; o efeito pode ter ocorrido.");
    }

    public static async Task<CollectionValidationInfo> GetCollectionValidationAsync(MongoOperationContext context, string database, string collection, CancellationToken cancellationToken)
    {
        EnsureUserCollection(database, collection);
        var definition = await FindCollectionDefinitionAsync(context.CreateClient().GetDatabase(database), collection, cancellationToken).ConfigureAwait(false);
        if (definition?.GetValue("type", string.Empty).AsString != "collection")
        {
            throw new InvalidOperationException($"{collection} não é uma coleção existente.");
        }

        var options = definition.GetValue("options", new BsonDocument()).AsBsonDocument;
        var validator = options.GetValue("validator", new BsonDocument());
        var validatorJson = validator.IsBsonDocument ? validator.AsBsonDocument.ToJson(MongoJson.CanonicalSettings) : "{}";
        var level = ParseValidationLevel(options.GetValue("validationLevel", "strict").AsString);
        var action = ParseValidationAction(options.GetValue("validationAction", "error").AsString);
        return new CollectionValidationInfo(validatorJson, level, action);
    }

    public static async Task DropCollectionAsync(MongoOperationContext context, ConnectionProfile profile, CollectionDropRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        await context.CreateClient()
            .GetDatabase(request.Database)
            .DropCollectionAsync(request.Collection, cancellationToken).ConfigureAwait(false);
    }

    public static async Task DropDatabaseAsync(MongoOperationContext context, ConnectionProfile profile, DatabaseDropRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        await context.CreateClient().DropDatabaseAsync(request.Database, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<string> GetDatabaseStatsAsync(MongoOperationContext context, string database, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        var stats = await context.CreateClient()
            .GetDatabase(database)
            .RunCommandAsync<BsonDocument>(new BsonDocument("dbStats", 1), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return stats.ToJson(MongoJson.CanonicalSettings);
    }

    public static async Task<string> GetCollectionDefinitionAsync(MongoOperationContext context, string database, string collection, CancellationToken cancellationToken)
    {
        using var cursor = await context.CreateClient().GetDatabase(database).ListCollectionsAsync(
            new ListCollectionsOptions { Filter = new BsonDocument("name", collection) }, cancellationToken).ConfigureAwait(false);
        var entries = await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
        return entries.FirstOrDefault()?.ToJson(MongoJson.CanonicalSettings) ?? "{}";
    }

    public static async Task<string> GetCollectionStatsAsync(MongoOperationContext context, string database, string collection, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        var stats = await context.CreateClient()
            .GetDatabase(database)
            .RunCommandAsync<BsonDocument>(new BsonDocument("collStats", collection), cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return stats.ToJson(MongoJson.CanonicalSettings);
    }

    private static void EnsureUserCollection(string database, string collection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        if (string.IsNullOrWhiteSpace(collection) || collection.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A coleção informada não pode ser administrada pela interface.", nameof(collection));
        }
    }

    private static async Task<BsonDocument?> FindCollectionDefinitionAsync(
        IMongoDatabase database,
        string collection,
        CancellationToken cancellationToken)
    {
        using var cursor = await database.ListCollectionsAsync(
            new ListCollectionsOptions { Filter = new BsonDocument("name", collection) },
            cancellationToken).ConfigureAwait(false);
        return (await cursor.ToListAsync(cancellationToken).ConfigureAwait(false)).SingleOrDefault();
    }

    private static async Task<ViewDependencyResolution> EnsureViewSourceChainAsync(
        IMongoDatabase database,
        string view,
        string source,
        CancellationToken cancellationToken)
    {
        var dependencies = await ViewDependencyChain.ResolveAsync(
            view,
            source,
            async (name, token) =>
            {
                var definition = await FindCollectionDefinitionAsync(database, name, token).ConfigureAwait(false);
                if (definition is null)
                {
                    return null;
                }

                var isView = definition.GetValue("type", string.Empty).AsString == "view";
                var viewOn = isView
                    ? definition.GetValue("options", new BsonDocument()).AsBsonDocument.GetValue("viewOn", string.Empty).AsString
                    : null;
                return new ViewNamespaceDefinition(isView, viewOn);
            },
            cancellationToken).ConfigureAwait(false);

        if (dependencies.CycleAt is not null)
        {
            throw new InvalidOperationException($"A origem da view contém um ciclo em {dependencies.CycleAt}.");
        }

        if (dependencies.MissingAt is not null)
        {
            throw new InvalidOperationException($"A origem da view não foi encontrada ou não possui definição válida: {dependencies.MissingAt}.");
        }

        if (dependencies.IsTruncated)
        {
            throw new InvalidOperationException($"A cadeia de origem da view excede {ViewDependencyChain.MaximumDepth} níveis; a alteração foi recusada.");
        }

        return dependencies;
    }

    private static CollectionValidationLevel ParseValidationLevel(string value) =>
        value.ToLowerInvariant() switch
        {
            "off" => CollectionValidationLevel.Off,
            "strict" => CollectionValidationLevel.Strict,
            "moderate" => CollectionValidationLevel.Moderate,
            _ => throw new InvalidOperationException($"O servidor retornou validationLevel não suportado: {value}.")
        };

    private static CollectionValidationAction ParseValidationAction(string value) =>
        value.ToLowerInvariant() switch
        {
            "error" => CollectionValidationAction.Error,
            "warn" => CollectionValidationAction.Warn,
            _ => throw new InvalidOperationException($"O servidor retornou validationAction não suportado: {value}.")
        };
}
