using System.Text.Json;
using System.Runtime.ExceptionServices;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalMongoViewMaterializationIntegrationTests
{
    private const string OptInVariable = "KAPIBARA_F6_LIVE_MONGO";
    private const string ConnectionVariable = "KAPIBARA_F6_MONGO_URI";

    [Test]
    public async Task MaterializesOnlyGuidOwnedSyntheticViewDocumentsAndPreservesViewCatalog()
    {
        var optIn = Environment.GetEnvironmentVariable(OptInVariable);
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.Equals(optIn, "1", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore($"Homologação Mongo local opt-in: defina {OptInVariable}=1 e {ConnectionVariable}; nenhum banco foi acessado.");

        var mongoUrl = MongoUrl.Create(connectionString);
        Assert.That(mongoUrl.DatabaseName, Is.EqualTo("sample_mflix"), "A URI precisa apontar para sample_mflix.");
        Assert.That(mongoUrl.Username, Is.Null.Or.Empty, "O teste não persiste credenciais nos artefatos.");
        Assert.That(mongoUrl.Password, Is.Null.Or.Empty, "O teste não persiste credenciais nos artefatos.");
        Assert.That(mongoUrl.Server.Host, Is.AnyOf("localhost", "127.0.0.1", "::1"),
            "Este teste aceita somente MongoDB loopback local.");

        const string databaseName = "sample_mflix";
        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);
        var initialNames = await database.ListCollectionNames().ToListAsync();
        TestContext.Progress.WriteLine($"Namespaces iniciais de {databaseName}: {string.Join(", ", initialNames.Order(StringComparer.Ordinal))}");
        var initialViewDefinitions = initialNames.Contains("system.views", StringComparer.Ordinal)
            ? await ReadViewCatalogAsync(database)
            : [];
        var marker = Guid.NewGuid().ToString("N");
        var sourceName = "f6_mat_source_" + marker;
        var viewName = "f6_mat_view_" + marker;
        var destinationName = "f6_mat_out_" + marker;
        var proposedNames = new[] { sourceName, viewName, destinationName };
        if (proposedNames.Any(name => initialNames.Contains(name, StringComparer.Ordinal)))
            Assert.Fail("Um namespace GUID já existia; nenhuma escrita foi iniciada.");

        var profile = ConnectionProfile.Create("F6 view materialization synthetic", connectionString, databaseName);
        var workspace = new MongoWorkspaceService(new LocalMongoDatabaseExportFileAccess(
            Path.Combine(Path.GetTempPath(), "KapibaraStudio.F6.Materialize", marker)));
        var sourceCreated = false;
        var viewCreated = false;
        var destinationMayExist = false;
        string? expectedViewDefinition = null;
        var expectedDocuments = new[]
        {
            new BsonDocument("_id", marker + "-1").Add("_f6Run", marker).Add("active", true).Add("sequence", 1),
            new BsonDocument("_id", marker + "-2").Add("_f6Run", marker).Add("active", true).Add("sequence", 2)
        };

        Exception? operationFailure = null;
        try
        {
            await database.CreateCollectionAsync(sourceName);
            sourceCreated = true;
            await database.GetCollection<BsonDocument>(sourceName).InsertManyAsync(expectedDocuments);

            var viewPipeline = "[{\"$match\":{\"_f6Run\":\"" + marker + "\",\"active\":true}}]";
            await workspace.CreateCollectionAsync(profile,
                new CollectionCreateRequest(databaseName, viewName, ViewOn: sourceName, ViewPipelineJson: viewPipeline));
            viewCreated = true;

            var viewDefinition = await workspace.GetCollectionDefinitionAsync(profile, databaseName, viewName);
            expectedViewDefinition = viewDefinition;
            var request = new ViewMaterializationRequest(databaseName, viewName, destinationName, "[]",
                destinationName, viewDefinition, "{}");
            destinationMayExist = true;
            var result = await workspace.MaterializeViewAsync(profile, request);

            var actualDocuments = await database.GetCollection<BsonDocument>(destinationName)
                .Find(FilterDefinition<BsonDocument>.Empty).Sort(new BsonDocument("sequence", 1)).ToListAsync();
            var destinationDefinition = await workspace.GetCollectionDefinitionAsync(profile, databaseName, destinationName);
            using var definitionJson = JsonDocument.Parse(destinationDefinition);
            Assert.Multiple(() =>
            {
                Assert.That(result.ReplacedExisting, Is.False);
                Assert.That(definitionJson.RootElement.GetProperty("type").GetString(), Is.EqualTo("collection"));
                Assert.That(actualDocuments, Is.EqualTo(expectedDocuments));
                Assert.That(result.DestinationDefinitionJson, Is.EqualTo(destinationDefinition));
            });
        }
        catch (Exception exception)
        {
            operationFailure = exception;
        }

        var cleanupFailures = new List<Exception>();
        if (destinationMayExist)
            await RunCleanupStepAsync(() => DropOwnedCollectionAsync(database, destinationName, marker, expectedDocuments,
                allowEmpty: false), cleanupFailures);
        if (viewCreated)
            await RunCleanupStepAsync(async () =>
            {
                var currentDefinition = await workspace.GetCollectionDefinitionAsync(profile, databaseName, viewName);
                if (!string.Equals(currentDefinition, expectedViewDefinition, StringComparison.Ordinal))
                    throw new InvalidOperationException("A definição da view sintética mudou; limpeza recusada.");
                await database.DropCollectionAsync(viewName);
            }, cleanupFailures);
        if (sourceCreated)
            await RunCleanupStepAsync(() => DropOwnedCollectionAsync(database, sourceName, marker, expectedDocuments,
                allowEmpty: true), cleanupFailures);
        await RunCleanupStepAsync(async () =>
        {
            var currentNames = await database.ListCollectionNames().ToListAsync();
            var unexpectedNames = currentNames.Except(initialNames, StringComparer.Ordinal)
                .Where(name => !string.Equals(name, "system.views", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal);
            var removedNames = initialNames.Except(currentNames, StringComparer.Ordinal).Order(StringComparer.Ordinal);
            if (unexpectedNames.Any() || removedNames.Any())
            {
                throw new InvalidOperationException($"A limpeza deixou namespaces inesperados. Adicionados: [{string.Join(", ", unexpectedNames)}]. Removidos: [{string.Join(", ", removedNames)}]. Estado final: [{string.Join(", ", currentNames.Order(StringComparer.Ordinal))}].");
            }
            if (currentNames.Contains("system.views", StringComparer.Ordinal))
            {
                var currentViewDefinitions = await ReadViewCatalogAsync(database);
                if (!currentViewDefinitions.SequenceEqual(initialViewDefinitions))
                    throw new InvalidOperationException("O catálogo system.views não voltou às definições iniciais.");
                if (!initialNames.Contains("system.views", StringComparer.Ordinal))
                    TestContext.Progress.WriteLine("MongoDB manteve system.views vazio após remover a view de fixture; nenhuma definição ficou no catálogo.");
            }
            TestContext.Progress.WriteLine($"Namespaces finais de {databaseName}: {string.Join(", ", currentNames.Order(StringComparer.Ordinal))}");
        }, cleanupFailures);

        if (operationFailure is not null)
        {
            if (cleanupFailures.Count > 0)
                throw new AggregateException("A homologação falhou e a limpeza deixou itens pendentes.", [operationFailure, .. cleanupFailures]);
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }
        if (cleanupFailures.Count > 0)
            throw new AggregateException("A limpeza da fixture Mongo não foi concluída com segurança.", cleanupFailures);
    }

    private static async Task DropOwnedCollectionAsync(IMongoDatabase database, string name, string marker,
        IReadOnlyCollection<BsonDocument> expectedDocuments, bool allowEmpty)
    {
        var names = await database.ListCollectionNames().ToListAsync();
        if (!names.Contains(name, StringComparer.Ordinal))
            return;

        var documents = await database.GetCollection<BsonDocument>(name)
            .Find(FilterDefinition<BsonDocument>.Empty).Sort(new BsonDocument("sequence", 1)).ToListAsync();
        if (!allowEmpty && documents.Count == 0
            || documents.Any(document => !document.TryGetValue("_f6Run", out var run) || !run.IsString || run.AsString != marker)
            || documents.Any(document => !expectedDocuments.Contains(document)))
            throw new InvalidOperationException($"Não foi possível provar a propriedade de {database.DatabaseNamespace.DatabaseName}.{name}; limpeza recusada.");

        await database.DropCollectionAsync(name);
    }

    private static Task<List<BsonDocument>> ReadViewCatalogAsync(IMongoDatabase database) =>
        database.GetCollection<BsonDocument>("system.views").Find(FilterDefinition<BsonDocument>.Empty)
            .Sort(new BsonDocument("_id", 1)).ToListAsync();

    private static async Task RunCleanupStepAsync(Func<Task> cleanup, List<Exception> failures)
    {
        try
        {
            await cleanup();
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }
    }
}
