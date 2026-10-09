using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalMongoLiteDbStandaloneImportRestartIntegrationTests
{
    private const string OptInVariable = "KAPIBARA_F6_LIVE_MONGO";
    private const string ConnectionVariable = "KAPIBARA_F6_MONGO_URI";

    [Test]
    public async Task RestartedLiteDbOwnerReloadsReceiptAndImportsFromBeginningIntoDisposableDatabase()
    {
        var optIn = Environment.GetEnvironmentVariable(OptInVariable);
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.Equals(optIn, "1", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore($"Homologação Mongo local opt-in: defina {OptInVariable}=1 e {ConnectionVariable}; nenhum banco foi acessado.");

        var mongoUrl = MongoUrl.Create(connectionString);
        Assert.That(mongoUrl.DatabaseName, Is.EqualTo("sample_mflix"), "A URI precisa apontar para sample_mflix.");
        Assert.That(mongoUrl.Username, Is.Null.Or.Empty, "A homologação não persiste credenciais no LiteDB temporário.");
        Assert.That(mongoUrl.Password, Is.Null.Or.Empty, "A homologação não persiste credenciais no LiteDB temporário.");
        Assert.That(mongoUrl.Server.Host, Is.AnyOf("localhost", "127.0.0.1", "::1"),
            "Este teste opt-in aceita somente MongoDB loopback local.");

        var marker = Guid.NewGuid().ToString("N");
        const string targetDatabase = "sample_mflix";
        var targetCollection = "f6_restart_" + marker;
        var sourceIds = new[] { marker + "-1", marker + "-2" };
        var disposableRoot = Path.Combine(Path.GetTempPath(), "KapibaraStudio.F6.LiveRestart", marker);
        var sourcePath = Path.Combine(disposableRoot, "source.json");
        var workspacePath = Path.Combine(disposableRoot, "workspace.db");
        var mongoClient = new MongoClient(connectionString);
        var existingDatabases = await mongoClient.ListDatabaseNames().ToListAsync();
        var initialSampleMflixCollections = await mongoClient.GetDatabase(targetDatabase).ListCollectionNames().ToListAsync();
        if (initialSampleMflixCollections.Count > 0)
        {
            if (existingDatabases.Contains(targetDatabase, StringComparer.Ordinal))
                Assert.Ignore("sample_mflix já contém coleções; homologação interrompida antes de qualquer escrita.");
            Assert.Fail("sample_mflix não apareceu na listagem de bancos, mas retornou coleções; nenhuma escrita foi iniciada.");
        }

        // The standalone importer requires an empty destination database. Never inspect existing fixture documents;
        // if sample_mflix is absent or has no namespaces, the one GUID collection below is the only target.
        try
        {
            Directory.CreateDirectory(disposableRoot);
            var sourceText = JsonSerializer.Serialize(sourceIds.Select((id, index) => new
            {
                _id = id,
                _f6Run = marker,
                sequence = index + 1,
                title = "synthetic restart fixture"
            }));
            await File.WriteAllTextAsync(sourcePath, sourceText, new UTF8Encoding(false));
            var sourceDigest = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
            var schema = new TransferImportSchema(TransferImportFormat.JsonArray);
            var request = new StandaloneImportRequest(sourcePath, targetDatabase, targetCollection, schema,
                DatabaseImportDuplicatePolicy.Reject, targetDatabase);

            var profile = ConnectionProfile.Create("F6 restart local synthetic", connectionString, "sample_mflix");
            var checkpointId = Guid.NewGuid();
            var createdTime = DateTimeOffset.UtcNow;
            var previousReceipt = new ImportCheckpoint(ImportCheckpoint.CurrentVersion, checkpointId,
                ImportCheckpointKind.StandaloneFile, profile.Id, null,
                ImportCheckpointRecovery.Sha256OfText(Path.GetFullPath(sourcePath)), sourceDigest,
                PlanHash(request), targetDatabase, targetCollection,
                1, sourceIds.Length, 1, 0, 0, ImportCheckpointState.NeedsReview, createdTime);

            // Owner one persists profile and receipt; disposal models closing the owning LiteDB connection.
            using (var owner = new LiteDbConnectionProfileRepository(workspacePath, new InMemoryProfileSecretStore()))
            {
                await owner.SaveAsync(profile);
                var persistedProfile = (await owner.GetAllAsync()).Single(item => item.Id == profile.Id);
                previousReceipt = previousReceipt with { SourceGenerationId = persistedProfile.SourceGenerationId };
                await ((IImportCheckpointRepository)owner).CreateAsync(previousReceipt);
            }

            // A fresh owner instance must recover both the profile binding and the exact pending receipt.
            using var restartedOwner = new LiteDbConnectionProfileRepository(workspacePath, new InMemoryProfileSecretStore());
            var reloadedProfile = (await restartedOwner.GetAllAsync()).Single(item => item.Id == profile.Id);
            var reloadedReceipt = (await ((IImportCheckpointRepository)restartedOwner).GetPendingAsync())
                .Single(item => item.Id == checkpointId);
            Assert.That(reloadedReceipt, Is.EqualTo(previousReceipt));
            Assert.That(reloadedProfile.SourceGenerationId, Is.EqualTo(previousReceipt.SourceGenerationId));

            var files = new LocalMongoDatabaseExportFileAccess(disposableRoot);
            var mongo = new MongoWorkspaceService(files, importCheckpoints: restartedOwner);
            var confirmedRequest = request with { RestartCheckpointId = checkpointId };
            var decision = await mongo.InspectStandaloneImportRestartAsync(reloadedProfile, confirmedRequest, checkpointId);
            Assert.That(decision.CanRestartFromBeginning, Is.True, decision.ReasonCode);
            Assert.That(decision.ReasonCode, Is.EqualTo("restart-from-beginning"));

            var result = await mongo.ImportStandaloneAsync(reloadedProfile, confirmedRequest);
            Assert.That(result.DocumentCount, Is.EqualTo(sourceIds.Length));
            var imported = await mongoClient.GetDatabase(targetDatabase).GetCollection<BsonDocument>(targetCollection)
                .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.Multiple(() =>
            {
                Assert.That(imported, Has.Count.EqualTo(sourceIds.Length));
                Assert.That(imported.Select(document => document["_f6Run"].AsString), Is.All.EqualTo(marker));
                Assert.That(imported.Select(document => document["_id"].AsString), Is.EquivalentTo(sourceIds));
            });
            Assert.That(await ((IImportCheckpointRepository)restartedOwner).GetPendingAsync(), Is.Empty,
                "A importação concluída deve remover o recibo pendente.");
        }
        finally
        {
            try
            {
                await CleanupOwnedNamespaceAsync(mongoClient, targetDatabase, targetCollection, marker, sourceIds,
                    initialSampleMflixCollections);
            }
            finally
            {
                if (Directory.Exists(disposableRoot)) Directory.Delete(disposableRoot, recursive: true);
            }
        }
    }

    private static string PlanHash(StandaloneImportRequest request) =>
        ImportCheckpointRecovery.Sha256OfText(JsonSerializer.Serialize(new
        {
            request.TargetDatabase, request.TargetCollection, request.Schema, request.DuplicatePolicy
        }));

    private static async Task CleanupOwnedNamespaceAsync(MongoClient client, string databaseName,
        string collectionName, string marker, string[] sourceIds,
        List<string> initialCollectionNames)
    {
        var database = client.GetDatabase(databaseName);
        var collectionNames = await database.ListCollectionNames().ToListAsync();
        if (collectionNames.Count == 0) return;

        // Delete only the one namespace created by this test and only while every document proves ownership.
        if (collectionNames.Count != 1 || !string.Equals(collectionNames[0], collectionName, StringComparison.Ordinal))
            throw new InvalidOperationException("Namespace descartável contém objetos inesperados; limpeza automática recusada.");
        var documents = await database.GetCollection<BsonDocument>(collectionName)
            .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        if (documents.Count != sourceIds.Length || documents.Any(document => !document.TryGetValue("_f6Run", out var run)
                || !run.IsString || run.AsString != marker
                || !document.TryGetValue("_id", out var id) || !id.IsString || !sourceIds.Contains(id.AsString))
            || !documents.Select(document => document["_id"].AsString).ToHashSet(StringComparer.Ordinal).SetEquals(sourceIds))
            throw new InvalidOperationException("A propriedade do namespace não pôde ser provada por documentos sintéticos; limpeza recusada.");

        await database.DropCollectionAsync(collectionName);
        var restoredCollectionNames = await database.ListCollectionNames().ToListAsync();
        if (!restoredCollectionNames.ToHashSet(StringComparer.Ordinal).SetEquals(initialCollectionNames))
            throw new InvalidOperationException("A lista de coleções de sample_mflix não voltou ao estado observado antes do teste.");
    }
}
