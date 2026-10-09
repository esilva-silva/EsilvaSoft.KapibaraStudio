using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalMongoDesktopImportRecoveryIntegrationTests
{
    private const string OptInVariable = "KAPIBARA_F6_LIVE_MONGO";
    private const string ConnectionVariable = "KAPIBARA_F6_MONGO_URI";

    [Test]
    public async Task DesktopCommandsRecoverReopenedOwnerAndRestartStandaloneImport()
    {
        var optIn = Environment.GetEnvironmentVariable(OptInVariable);
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.Equals(optIn, "1", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore($"Homologação Mongo local opt-in: defina {OptInVariable}=1 e {ConnectionVariable}; nenhum banco foi acessado.");

        var mongoUrl = MongoUrl.Create(connectionString);
        Assert.That(mongoUrl.DatabaseName, Is.EqualTo("sample_mflix"), "A URI precisa apontar para sample_mflix.");
        Assert.That(mongoUrl.Username, Is.Null.Or.Empty, "A homologação não persiste credenciais no LiteDB temporário.");
        Assert.That(mongoUrl.Password, Is.Null.Or.Empty, "A homologação não persiste credenciais no LiteDB temporário.");
        Assert.That(mongoUrl.Servers.Count(), Is.EqualTo(1), "Only one loopback server is permitted.");
        Assert.That(mongoUrl.Server.Host, Is.AnyOf("localhost", "127.0.0.1", "::1"),
            "Este teste opt-in aceita somente MongoDB loopback local.");

        var marker = Guid.NewGuid().ToString("N");
        var targetDatabase = "f6_desktop_recovery_" + marker;
        var targetCollection = "f6_desktop_restart_" + marker;
        var sourceIds = new[] { marker + "-1", marker + "-2" };
        var disposableRoot = Path.Combine(Path.GetTempPath(), "KapibaraStudio.F6.DesktopRestart", marker);
        var sourcePath = Path.Combine(disposableRoot, "source.json");
        var workspacePath = Path.Combine(disposableRoot, "workspace.db");
        var mongoClient = new MongoClient(connectionString);
        var existingDatabases = await mongoClient.ListDatabaseNames().ToListAsync();
        if (existingDatabases.Contains(targetDatabase, StringComparer.Ordinal))
            Assert.Ignore("O banco GUID de destino já existe; homologação interrompida antes de qualquer escrita.");
        var initialCollectionNames = await mongoClient.GetDatabase(targetDatabase).ListCollectionNames().ToListAsync();
        if (initialCollectionNames.Count > 0)
            Assert.Ignore("O destino GUID ganhou namespaces após o preflight; nenhuma escrita foi iniciada.");

        // The URI profile remains bound to sample_mflix; every request explicitly selects a new GUID database.
        // Only database names and metadata of our empty destination are inspected before writing.
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
            using (var restartedOwner = new LiteDbConnectionProfileRepository(workspacePath, new InMemoryProfileSecretStore()))
            {
                var reloadedProfile = (await restartedOwner.GetAllAsync()).Single(item => item.Id == profile.Id);
                var reloadedReceipt = (await ((IImportCheckpointRepository)restartedOwner).GetPendingAsync())
                    .Single(item => item.Id == checkpointId);
                Assert.That(reloadedReceipt, Is.EqualTo(previousReceipt));
                Assert.That(reloadedProfile.SourceGenerationId, Is.EqualTo(previousReceipt.SourceGenerationId));

                var files = new LocalMongoDatabaseExportFileAccess(disposableRoot);
                var mongo = new MongoWorkspaceService(files, importCheckpoints: restartedOwner);
                var workspace = new WorkspaceService(restartedOwner, restartedOwner, restartedOwner,
                    restartedOwner, restartedOwner, mongo, new RejectScriptExecution(), new LocalScriptFileService(),
                    new SessionConnectionSecretStore(), profilerCaptures: restartedOwner);
                var viewModel = new MainWindowViewModel(workspace, autoLoadCollections: false);
                if (viewModel.LoadProfilesCommand.ExecutionTask is { } loading) await loading;
                viewModel.SelectedProfile = reloadedProfile;
                viewModel.SelectedDatabase = targetDatabase;
                viewModel.StandaloneSourceFile = sourcePath;
                viewModel.StandaloneTargetCollection = targetCollection;
                viewModel.StandaloneUseJsonArray = true;
                await viewModel.PreviewStandaloneImportCommand.ExecuteAsync(null);
                Assert.That(viewModel.StandalonePreviewText, Is.Not.Empty);
                await viewModel.LoadImportCheckpointsCommand.ExecuteAsync(null);
                viewModel.SelectedImportCheckpoint = viewModel.PendingImportCheckpoints.Single();
                Assert.That(viewModel.SelectedImportCheckpoint.Checkpoint, Is.EqualTo(previousReceipt));
                viewModel.StandaloneConfirmation = targetDatabase;
                Assert.That(viewModel.CanImportStandalone, Is.False, "Recovery requires an explicit verification.");
                await viewModel.VerifyImportRecoveryCommand.ExecuteAsync(null);
                Assert.That(viewModel.CanImportStandalone, Is.True, viewModel.ImportRecoveryStatus);
                Assert.That(viewModel.ShowStandaloneRestartAction, Is.True);
                await viewModel.ImportStandaloneCommand.ExecuteAsync(null);
                Assert.That(viewModel.CanImportStandalone, Is.False, "Verification is consumed by dispatch.");
                var imported = await mongoClient.GetDatabase(targetDatabase).GetCollection<BsonDocument>(targetCollection)
                    .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
                Assert.Multiple(() =>
                {
                    Assert.That(imported, Has.Count.EqualTo(sourceIds.Length));
                    Assert.That(imported.Select(document => document["_f6Run"].AsString), Is.All.EqualTo(marker));
                    Assert.That(imported.Select(document => document["_id"].AsString), Is.EquivalentTo(sourceIds));
                });
                Assert.That(await ((IImportCheckpointRepository)restartedOwner).GetPendingAsync(), Is.Empty,
                    "Successful desktop import must complete the recovered receipt.");
                // A new receipt against the now occupied target must remain pending and block the desktop action.
                await ((IImportCheckpointRepository)restartedOwner).CreateAsync(previousReceipt);
                await viewModel.LoadImportCheckpointsCommand.ExecuteAsync(null);
                viewModel.SelectedImportCheckpoint = viewModel.PendingImportCheckpoints.Single();
                await viewModel.PreviewStandaloneImportCommand.ExecuteAsync(null);
                viewModel.StandaloneConfirmation = targetDatabase;
                await viewModel.VerifyImportRecoveryCommand.ExecuteAsync(null);
                Assert.That(viewModel.CanImportStandalone, Is.False, viewModel.ImportRecoveryStatus);
                Assert.That((await ((IImportCheckpointRepository)restartedOwner).GetPendingAsync()).Single(),
                    Is.EqualTo(previousReceipt), "Refused restart must preserve the receipt.");
                await ((IImportCheckpointRepository)restartedOwner).CompleteAsync(checkpointId);
                Assert.That(await ((IImportCheckpointRepository)restartedOwner).GetPendingAsync(), Is.Empty,
                    "A importação concluída deve remover o recibo pendente.");
            }
            using var finalOwner = new LiteDbConnectionProfileRepository(workspacePath, new InMemoryProfileSecretStore());
            Assert.That(await ((IImportCheckpointRepository)finalOwner).GetPendingAsync(), Is.Empty,
                "A third owner must observe durable completion.");
        }
        finally
        {
            try
            {
                await CleanupOwnedNamespaceAsync(mongoClient, targetDatabase, targetCollection, marker, sourceIds,
                    initialCollectionNames);
                var finalDatabases = await mongoClient.ListDatabaseNames().ToListAsync();
                Assert.That(finalDatabases, Is.EquivalentTo(existingDatabases), "Database names must return to their initial state.");
            }
            finally
            {
                if (Directory.Exists(disposableRoot)) Directory.Delete(disposableRoot, recursive: true);
            }
        }
    }

    private sealed class RejectScriptExecution : IScriptExecutionService
    {
        public Task<ScriptExecutionResult> ExecuteAsync(ConnectionProfile profile, string script,
            string? inputJson = null, string? database = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This recovery smoke never executes scripts.");
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
        if (documents.Count > sourceIds.Length || documents.Any(document => !document.TryGetValue("_f6Run", out var run)
                || !run.IsString || run.AsString != marker
                || !document.TryGetValue("_id", out var id) || !id.IsString || !sourceIds.Contains(id.AsString)))
            throw new InvalidOperationException("A propriedade do namespace não pôde ser provada por documentos sintéticos; limpeza recusada.");

        await database.DropCollectionAsync(collectionName);
        var restoredCollectionNames = await database.ListCollectionNames().ToListAsync();
        if (!restoredCollectionNames.ToHashSet(StringComparer.Ordinal).SetEquals(initialCollectionNames))
            throw new InvalidOperationException("A lista de coleções do destino GUID não voltou ao estado observado antes do teste.");
    }
}
