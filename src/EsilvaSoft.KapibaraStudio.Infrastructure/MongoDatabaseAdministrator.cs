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
        var command = new BsonDocument
        {
            ["collMod"] = request.View,
            ["pipeline"] = new BsonArray(context.ParsePipeline(request.PipelineJson))
        };
        await context.CreateClient()
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task ConfigureCollectionValidationAsync(MongoOperationContext context, ConnectionProfile profile, CollectionValidationRequest request, CancellationToken cancellationToken)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        var command = new BsonDocument
        {
            ["collMod"] = request.Collection,
            ["validator"] = context.ParseDocument(request.ValidatorJson, "validador"),
            ["validationLevel"] = request.ValidationLevel.ToString().ToLowerInvariant(),
            ["validationAction"] = request.ValidationAction.ToString().ToLowerInvariant()
        };
        await context.CreateClient()
            .GetDatabase(request.Database)
            .RunCommandAsync<BsonDocument>(command, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public static async Task<CollectionValidationInfo> GetCollectionValidationAsync(MongoOperationContext context, string database, string collection, CancellationToken cancellationToken)
    {
        EnsureUserCollection(database, collection);
        using var cursor = await context.CreateClient()
            .GetDatabase(database)
            .ListCollectionsAsync(new ListCollectionsOptions { Filter = new BsonDocument("name", collection) }, cancellationToken)
            .ConfigureAwait(false);
        var definition = (await cursor.ToListAsync(cancellationToken).ConfigureAwait(false)).SingleOrDefault();
        if (definition is null)
        {
            throw new InvalidOperationException($"A coleção {collection} não foi encontrada.");
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
