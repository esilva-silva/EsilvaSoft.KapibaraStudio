using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalMongoDatabaseImportPreviewIntegrationTests
{
    private const string OptInVariable = "KAPIBARA_F6_LIVE_MONGO";
    private const string ConnectionVariable = "KAPIBARA_F6_MONGO_URI";

    [Test]
    public async Task PreviewShowsSyntheticManifestAndBlocksGuidOwnedNamespaceCollision()
    {
        var optIn = Environment.GetEnvironmentVariable(OptInVariable);
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.Equals(optIn, "1", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore($"Homologação Mongo local opt-in: defina {OptInVariable}=1 e {ConnectionVariable}; nenhum banco foi acessado.");

        var mongoUrl = MongoUrl.Create(connectionString);
        Assert.That(mongoUrl.DatabaseName, Is.EqualTo("sample_mflix"), "A URI precisa apontar para sample_mflix.");
        Assert.That(mongoUrl.Username, Is.Null.Or.Empty, "O teste não persiste credenciais no pacote temporário.");
        Assert.That(mongoUrl.Password, Is.Null.Or.Empty, "O teste não persiste credenciais no pacote temporário.");
        Assert.That(mongoUrl.Server.Host, Is.AnyOf("localhost", "127.0.0.1", "::1"),
            "Este teste opt-in aceita somente MongoDB loopback local.");

        const string targetDatabaseName = "sample_mflix";
        var client = new MongoClient(connectionString);
        var initialDatabaseNames = await client.ListDatabaseNames().ToListAsync();
        var initialTargetCollections = await client.GetDatabase(targetDatabaseName).ListCollectionNames().ToListAsync();
        if (initialTargetCollections.Count > 0)
            Assert.Ignore("sample_mflix já contém namespaces; homologação interrompida antes de qualquer escrita.");

        var marker = Guid.NewGuid().ToString("N");
        var sourceDatabaseName = "f6_preview_source_" + marker;
        var ownedCollectionName = "f6_preview_docs_" + marker;
        var disposableRoot = Path.Combine(Path.GetTempPath(), "KapibaraStudio.F6.LivePreview", marker);
        if (initialDatabaseNames.Contains(sourceDatabaseName, StringComparer.Ordinal)
            || Directory.Exists(disposableRoot))
            Assert.Fail("O namespace/pacote aleatório já existia; nenhum recurso foi criado.");

        var sourceCreated = false;
        var targetCollisionCreated = false;
        try
        {
            Directory.CreateDirectory(disposableRoot);
            var files = new LocalMongoDatabaseExportFileAccess(disposableRoot);
            var mongo = new MongoWorkspaceService(files);
            var sourceProfile = ConnectionProfile.Create("F6 preview synthetic source", connectionString, sourceDatabaseName);
            var targetProfile = ConnectionProfile.Create("F6 preview local target", connectionString, targetDatabaseName);

            var source = client.GetDatabase(sourceDatabaseName).GetCollection<BsonDocument>(ownedCollectionName);
            sourceCreated = true;
            await source.InsertOneAsync(new BsonDocument
            {
                ["_id"] = marker,
                ["_f6Run"] = marker,
                ["synthetic"] = true
            });

            var export = await mongo.ExportDatabaseAsync(sourceProfile, new DatabaseExportRequest(sourceDatabaseName));
            Assert.That(export.CollectionCount, Is.EqualTo(1));
            Assert.That(export.DocumentCount, Is.EqualTo(1));

            var manifestText = await File.ReadAllTextAsync(Path.Combine(export.OutputDirectory, "manifest.json"));
            using var manifest = JsonDocument.Parse(manifestText);
            var metadata = manifest.RootElement.GetProperty("Metadata");
            Assert.Multiple(() =>
            {
                Assert.That(metadata.GetProperty("Version").GetInt32(), Is.EqualTo(2));
                Assert.That(metadata.GetProperty("ServerVersion").GetProperty("Status").GetString(), Is.EqualTo("Available"));
                Assert.That(metadata.GetProperty("Topology").GetProperty("Status").GetString(), Is.EqualTo("Available"));
                Assert.That(metadata.GetProperty("Topology").GetProperty("Kind").GetString(), Is.EqualTo("Standalone"));
                Assert.That(metadata.GetProperty("FeatureCompatibilityVersion").GetProperty("Status").GetString(), Is.EqualTo("Available"));
                Assert.That(metadata.GetProperty("FeatureCompatibilityVersion").GetProperty("Version").GetString(), Is.EqualTo("8.0"));
                Assert.That(manifestText, Does.Not.Contain(mongoUrl.Server.Host));
            });

            var request = new DatabaseImportRequest(export.OutputDirectory, targetDatabaseName,
                DatabaseImportDuplicatePolicy.Reject, targetDatabaseName);
            var emptyDestinationPlan = await mongo.PreviewDatabaseImportAsync(targetProfile, request);
            Assert.Multiple(() =>
            {
                Assert.That(emptyDestinationPlan.CanImport, Is.True);
                Assert.That(emptyDestinationPlan.DestinationIsEmpty, Is.True);
                Assert.That(emptyDestinationPlan.TotalDocuments, Is.EqualTo(1));
                Assert.That(emptyDestinationPlan.Items, Has.Count.EqualTo(1));
                Assert.That(emptyDestinationPlan.Items[0].Namespace, Is.EqualTo(ownedCollectionName));
                Assert.That(emptyDestinationPlan.Items[0].DocumentCount, Is.EqualTo(1));
                Assert.That(emptyDestinationPlan.Items[0].DestinationNamespaceExists, Is.False);
            });

            var targetCollection = client.GetDatabase(targetDatabaseName).GetCollection<BsonDocument>(ownedCollectionName);
            targetCollisionCreated = true;
            await targetCollection.InsertOneAsync(new BsonDocument
            {
                ["_id"] = marker,
                ["_f6Run"] = marker,
                ["syntheticCollision"] = true
            });

            var collisionPlan = await mongo.PreviewDatabaseImportAsync(targetProfile, request);
            Assert.Multiple(() =>
            {
                Assert.That(collisionPlan.CanImport, Is.False);
                Assert.That(collisionPlan.DestinationIsEmpty, Is.False);
                Assert.That(collisionPlan.TotalDocuments, Is.EqualTo(1));
                Assert.That(collisionPlan.Items.Single(item => item.Namespace == ownedCollectionName)
                    .DestinationNamespaceExists, Is.True);
                Assert.That(collisionPlan.DestinationNamespaces, Does.Contain(ownedCollectionName));
            });
        }
        finally
        {
            try
            {
                if (targetCollisionCreated)
                    await DropOwnedCollectionAsync(client, targetDatabaseName, ownedCollectionName, marker,
                        "syntheticCollision", initialTargetCollections);
                else
                    await AssertCollectionSetRestoredAsync(client.GetDatabase(targetDatabaseName), initialTargetCollections);
            }
            finally
            {
                try
                {
                    if (sourceCreated)
                        await DropOwnedCollectionAsync(client, sourceDatabaseName, ownedCollectionName, marker,
                            "synthetic", []);
                }
                finally
                {
                    if (Directory.Exists(disposableRoot)) Directory.Delete(disposableRoot, recursive: true);
                }
            }
        }
    }

    private static async Task DropOwnedCollectionAsync(MongoClient client, string databaseName,
        string collectionName, string marker, string ownershipField, IReadOnlyCollection<string> initialCollections)
    {
        var database = client.GetDatabase(databaseName);
        var names = await database.ListCollectionNames().ToListAsync();
        if (!names.Contains(collectionName, StringComparer.Ordinal))
        {
            await AssertCollectionSetRestoredAsync(database, initialCollections);
            return;
        }

        var documents = await database.GetCollection<BsonDocument>(collectionName)
            .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        if (documents.Count != 1 || !documents[0].TryGetValue("_id", out var id) || !id.IsString
            || id.AsString != marker || !documents[0].TryGetValue("_f6Run", out var run) || !run.IsString
            || run.AsString != marker || !documents[0].TryGetValue(ownershipField, out var ownership)
            || !ownership.IsBoolean || !ownership.AsBoolean)
            throw new InvalidOperationException($"Não foi possível provar a propriedade de {databaseName}.{collectionName}; limpeza recusada.");

        await database.DropCollectionAsync(collectionName);
        await AssertCollectionSetRestoredAsync(database, initialCollections);
    }

    private static async Task AssertCollectionSetRestoredAsync(IMongoDatabase database,
        IReadOnlyCollection<string> initialCollections)
    {
        var current = await database.ListCollectionNames().ToListAsync();
        if (!current.ToHashSet(StringComparer.Ordinal).SetEquals(initialCollections))
            throw new InvalidOperationException($"A lista de namespaces de {database.DatabaseNamespace.DatabaseName} não voltou ao estado inicial.");
    }
}
