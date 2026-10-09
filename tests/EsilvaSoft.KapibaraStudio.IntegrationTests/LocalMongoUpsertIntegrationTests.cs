using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalMongoUpsertIntegrationTests
{
    private const string OptInVariable = "KAPIBARA_F6_LIVE_MONGO";
    private const string ConnectionVariable = "KAPIBARA_F6_MONGO_URI";

    [Test]
    public async Task UpsertReplacesMatchingSyntheticDocumentAndInsertsNewOneInSampleMflix()
    {
        var optIn = Environment.GetEnvironmentVariable(OptInVariable);
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.Equals(optIn, "1", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore($"Homologação Mongo local opt-in: defina {OptInVariable}=1 e {ConnectionVariable}; nenhum banco foi acessado.");

        var mongoUrl = MongoUrl.Create(connectionString);
        Assert.That(mongoUrl.DatabaseName, Is.EqualTo("sample_mflix"), "A URI precisa apontar para sample_mflix.");
        Assert.That(mongoUrl.Username, Is.Null.Or.Empty);
        Assert.That(mongoUrl.Password, Is.Null.Or.Empty);
        Assert.That(mongoUrl.Server.Host, Is.AnyOf("localhost", "127.0.0.1", "::1"));

        const string targetDatabaseName = "sample_mflix";
        var marker = Guid.NewGuid().ToString("N");
        var sourceDatabaseName = "f6_upsert_src_" + marker;
        var collectionName = "f6_upsert_" + marker;
        var root = Path.Combine(Path.GetTempPath(), "KapibaraStudio.F6.LiveUpsert", marker);
        if (Directory.Exists(root)) Assert.Fail("A pasta temporária aleatória já existe; nenhum recurso foi criado.");

        var client = new MongoClient(connectionString);
        var initialDatabaseNames = await client.ListDatabaseNames().ToListAsync();
        if (initialDatabaseNames.Contains(sourceDatabaseName, StringComparer.Ordinal))
            Assert.Fail("O banco fonte aleatório já existe; nenhum recurso foi alterado.");
        var targetDatabase = client.GetDatabase(targetDatabaseName);
        var initialNames = await targetDatabase.ListCollectionNames().ToListAsync();
        if (initialNames.Contains(collectionName, StringComparer.Ordinal))
            Assert.Fail("A coleção aleatória já existe; nenhum recurso foi alterado.");

        var sourceCreated = false;
        var targetCreated = false;
        try
        {
            Directory.CreateDirectory(root);
            var mongo = new MongoWorkspaceService(new LocalMongoDatabaseExportFileAccess(root));
            var sourceProfile = ConnectionProfile.Create("F6 synthetic Upsert source", connectionString, sourceDatabaseName);
            var targetProfile = ConnectionProfile.Create("F6 sample_mflix Upsert target", connectionString, targetDatabaseName);

            var source = client.GetDatabase(sourceDatabaseName).GetCollection<BsonDocument>(collectionName);
            sourceCreated = true;
            await source.InsertManyAsync(
            [
                new BsonDocument { ["_id"] = marker, ["_f6Run"] = marker, ["imported"] = true },
                new BsonDocument { ["_id"] = marker + "-new", ["_f6Run"] = marker, ["newDocument"] = true }
            ]);

            var export = await mongo.ExportDatabaseAsync(sourceProfile, new DatabaseExportRequest(sourceDatabaseName));
            var target = targetDatabase.GetCollection<BsonDocument>(collectionName);
            targetCreated = true;
            await target.InsertOneAsync(new BsonDocument
            {
                ["_id"] = marker,
                ["_f6Run"] = marker,
                ["oldFieldToRemove"] = "synthetic-only"
            });

            var request = new DatabaseImportRequest(export.OutputDirectory, targetDatabaseName,
                DatabaseImportDuplicatePolicy.Upsert, targetDatabaseName);
            var preview = await mongo.PreviewDatabaseImportAsync(targetProfile, request);
            Assert.Multiple(() =>
            {
                Assert.That(preview.CanImport, Is.True);
                Assert.That(preview.DestinationIsEmpty, Is.False);
                Assert.That(preview.DestinationCanBeUpsertedInto, Is.True);
                Assert.That(preview.Items.Single(item => item.Namespace == collectionName).DestinationNamespaceExists, Is.True);
            });

            var result = await mongo.ImportDatabaseAsync(targetProfile, request with { PreviewFingerprint = preview.Fingerprint });
            var imported = await target.Find(new BsonDocument("_f6Run", marker)).ToListAsync();
            Assert.Multiple(() =>
            {
                Assert.That(result.DocumentCount, Is.EqualTo(2));
                Assert.That(imported, Has.Count.EqualTo(2));
                Assert.That(imported.Single(document => document["_id"] == marker).Contains("oldFieldToRemove"), Is.False);
                Assert.That(imported.Single(document => document["_id"] == marker)["imported"].AsBoolean, Is.True);
                Assert.That(imported.Single(document => document["_id"] == marker + "-new")["newDocument"].AsBoolean, Is.True);
            });
        }
        finally
        {
            try
            {
                if (targetCreated)
                {
                    var target = targetDatabase.GetCollection<BsonDocument>(collectionName);
                    var docs = await target.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
                    if (docs.Count == 0 || docs.All(document => document.TryGetValue("_f6Run", out var value)
                        && value == marker))
                    {
                        await targetDatabase.DropCollectionAsync(collectionName);
                    }
                }
            }
            finally
            {
                if (sourceCreated)
                {
                    var sourceDb = client.GetDatabase(sourceDatabaseName);
                    var source = sourceDb.GetCollection<BsonDocument>(collectionName);
                    var docs = await source.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
                    if (docs.Count == 0 || docs.All(document => document.TryGetValue("_f6Run", out var value)
                        && value == marker))
                        await client.DropDatabaseAsync(sourceDatabaseName);
                }
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }

        var finalTargetNames = await targetDatabase.ListCollectionNames().ToListAsync();
        var finalDatabaseNames = await client.ListDatabaseNames().ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(finalTargetNames.Order(StringComparer.Ordinal), Is.EqualTo(initialNames.Order(StringComparer.Ordinal)),
                "Somente a coleção GUID sintética deveria ser removida de sample_mflix.");
            Assert.That(finalDatabaseNames, Does.Not.Contain(sourceDatabaseName));
            Assert.That(Directory.Exists(root), Is.False);
        });
    }
}
