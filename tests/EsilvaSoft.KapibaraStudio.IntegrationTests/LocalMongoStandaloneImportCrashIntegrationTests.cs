using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalMongoStandaloneImportCrashIntegrationTests
{
    private const string OptInVariable = "KAPIBARA_F6_LIVE_MONGO";
    private const string ConnectionVariable = "KAPIBARA_F6_MONGO_URI";
    private const string WorkerVariable = "KAPIBARA_F6_IMPORT_CRASH_WORKER";
    private const string WorkspaceVariable = "KAPIBARA_F6_IMPORT_CRASH_WORKSPACE";
    private const string RootVariable = "KAPIBARA_F6_IMPORT_CRASH_ROOT";
    private const string ReadyVariable = "KAPIBARA_F6_IMPORT_CRASH_READY";
    private const string ProfileIdVariable = "KAPIBARA_F6_IMPORT_CRASH_PROFILE_ID";
    private const string CollectionVariable = "KAPIBARA_F6_IMPORT_CRASH_COLLECTION";

    [Test]
    public async Task AbruptCrashAfterBulkWritePreservesPreparedCheckpointAndBlocksRestart()
    {
        var uri = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (Environment.GetEnvironmentVariable(OptInVariable) != "1" || string.IsNullOrWhiteSpace(uri))
            Assert.Ignore($"Homologação Mongo local opt-in: defina {OptInVariable}=1 e {ConnectionVariable}; nenhum banco foi acessado.");

        var mongoUrl = MongoUrl.Create(uri);
        Assert.That(mongoUrl.DatabaseName, Is.EqualTo("sample_mflix"), "A URI precisa apontar para sample_mflix.");
        Assert.That(mongoUrl.Username, Is.Null.Or.Empty, "Este teste não aceita credenciais.");
        Assert.That(mongoUrl.Password, Is.Null.Or.Empty, "Este teste não aceita credenciais.");
        Assert.That(mongoUrl.Server.Host, Is.AnyOf("localhost", "127.0.0.1", "::1"), "Somente MongoDB loopback é aceito.");

        var marker = Guid.NewGuid().ToString("N");
        const string databaseName = "sample_mflix";
        var collectionName = "f6_import_crash_" + marker;
        var documentIds = new[] { marker + "-1", marker + "-2", marker + "-3" };
        var root = Path.Combine(Path.GetTempPath(), "KapibaraStudio.F6.ImportCrash", marker);
        var sourcePath = Path.Combine(root, "source.json");
        var workspacePath = Path.Combine(root, "workspace.db");
        var readyPath = Path.Combine(root, "bulk-write-complete.txt");
        var client = new MongoClient(uri);
        var initialDatabaseNames = await client.ListDatabaseNames().ToListAsync();
        var database = client.GetDatabase(databaseName);
        var initialCollectionNames = await database.ListCollectionNames().ToListAsync();

        // The importer treats system.* namespaces as empty. Only names are inspected; no existing
        // collection documents are read. Preserve these namespaces and skip before writes if any user
        // collection is present.
        if (initialCollectionNames.Any(name => !name.StartsWith("system.", StringComparison.OrdinalIgnoreCase)))
            Assert.Ignore("sample_mflix contém coleções de usuário; o teste exige destino vazio e foi interrompido antes de qualquer escrita.");
        Assert.That(initialCollectionNames, Does.Not.Contain(collectionName), "A coleção GUID não pode existir antes do teste.");

        var profile = ConnectionProfile.Create("F6 crash import local synthetic", uri, databaseName);
        var checkpointId = Guid.Empty;
        Process? child = null;
        try
        {
            Directory.CreateDirectory(root);
            var source = JsonSerializer.Serialize(documentIds.Select((id, index) => new
            {
                _id = id,
                _f6Run = marker,
                sequence = index + 1,
                title = "synthetic abrupt-import-crash fixture"
            }));
            await File.WriteAllTextAsync(sourcePath, source, new UTF8Encoding(false));

            using (var owner = new LiteDbConnectionProfileRepository(workspacePath, new InMemoryProfileSecretStore()))
            {
                await owner.SaveAsync(profile);
                profile = (await owner.GetAllAsync()).Single(item => item.Id == profile.Id);
            }

            child = StartWorker(uri, workspacePath, root, readyPath, profile.Id, collectionName);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
            while (!File.Exists(readyPath) && !child.HasExited && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(25);

            Assert.That(File.Exists(readyPath), Is.True, "O worker não concluiu BulkWrite nem alcançou o checkpoint antes do deadline.");
            checkpointId = Guid.ParseExact(await File.ReadAllTextAsync(readyPath), "N");
            Assert.That(child.HasExited, Is.False, "O worker saiu antes da terminação abrupta solicitada pelo teste.");
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.That(child.ExitCode, Is.Not.EqualTo(0), "O processo worker deve ter sido encerrado abruptamente.");

            var postCrashCollectionNames = await database.ListCollectionNames().ToListAsync();
            Assert.That(postCrashCollectionNames.ToHashSet(StringComparer.Ordinal)
                .SetEquals(initialCollectionNames.Append(collectionName)), Is.True,
                "Após o crash, as coleções iniciais devem permanecer intactas junto da única coleção sintética do teste.");
            var imported = await database.GetCollection<BsonDocument>(collectionName)
                .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.That(imported, Has.Count.EqualTo(documentIds.Length));
            Assert.That(imported.All(document => document.TryGetValue("_f6Run", out var run)
                && run.IsString && run.AsString == marker), Is.True, "Cada documento gravado precisa provar propriedade do teste.");
            Assert.That(imported.Select(document => document["_id"].AsString), Is.EquivalentTo(documentIds));

            using var reopenedOwner = new LiteDbConnectionProfileRepository(workspacePath, new InMemoryProfileSecretStore());
            var repository = (IImportCheckpointRepository)reopenedOwner;
            var pending = await repository.GetPendingAsync();
            var receipt = pending.Single(item => item.Id == checkpointId);
            Assert.Multiple(() =>
            {
                Assert.That(receipt.State, Is.EqualTo(ImportCheckpointState.Prepared));
                Assert.That(receipt.ProcessedDocuments, Is.Zero);
                Assert.That(receipt.InsertedDocuments, Is.Zero);
                Assert.That(receipt.ModifiedDocuments, Is.Zero);
                Assert.That(receipt.IgnoredDocuments, Is.Zero);
                Assert.That(receipt.TotalDocuments, Is.EqualTo(documentIds.Length));
                Assert.That(receipt.TargetDatabase, Is.EqualTo(databaseName));
                Assert.That(receipt.TargetCollection, Is.EqualTo(collectionName));
            });

            var recoveredProfile = (await reopenedOwner.GetAllAsync()).Single(item => item.Id == profile.Id);
            var request = new StandaloneImportRequest(sourcePath, databaseName, collectionName,
                new TransferImportSchema(TransferImportFormat.JsonArray), DatabaseImportDuplicatePolicy.Reject,
                databaseName) { RestartCheckpointId = checkpointId };
            var mongo = new MongoWorkspaceService(new LocalMongoDatabaseExportFileAccess(root), importCheckpoints: reopenedOwner);
            var decision = await mongo.InspectStandaloneImportRestartAsync(recoveredProfile, request, checkpointId);
            Assert.That(decision.CanRestartFromBeginning, Is.False);
            Assert.That(decision.ReasonCode, Is.EqualTo("target-not-empty"));

            // Cleanup follows a complete ownership proof: expected sole namespace, exact IDs, and synthetic marker.
            Assert.That(imported.Select(document => document["_id"].AsString).ToHashSet(StringComparer.Ordinal)
                .SetEquals(documentIds), Is.True);
            await database.DropCollectionAsync(collectionName);
            var restoredCollections = await database.ListCollectionNames().ToListAsync();
            Assert.That(restoredCollections.ToHashSet(StringComparer.Ordinal).SetEquals(initialCollectionNames), Is.True);
            var restoredDatabases = await client.ListDatabaseNames().ToListAsync();
            Assert.That(restoredDatabases.ToHashSet(StringComparer.Ordinal).SetEquals(initialDatabaseNames), Is.True,
                "A limpeza deve restaurar a lista inicial de bancos observada antes do teste.");
            await repository.CompleteAsync(checkpointId);
            Assert.That(await repository.GetPendingAsync(), Is.Empty, "A limpeza explícita deve remover o receipt do teste.");
        }
        finally
        {
            if (child is { HasExited: false })
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
            }
            child?.Dispose();
            if (child is null && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            // After Mongo mutation, preserve the receipt/workspace and synthetic collection on assertion or
            // verification failure so an operator can inspect the exact uncertain state instead of losing evidence.
        }
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    [Test]
    public async Task CrashWorkerPausesAfterBulkWriteBeforeCheckpointUpdate()
    {
        if (Environment.GetEnvironmentVariable(WorkerVariable) != "1")
            Assert.Ignore("Worker interno: executado apenas pelo teste pai de crash após BulkWrite.");

        var uri = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException("URI Mongo local não informada ao worker.");
        var workspace = RequiredEnvironment(WorkspaceVariable);
        var root = RequiredEnvironment(RootVariable);
        var ready = RequiredEnvironment(ReadyVariable);
        var profileId = Guid.Parse(RequiredEnvironment(ProfileIdVariable));
        var collectionName = RequiredEnvironment(CollectionVariable);

        using var owner = new LiteDbConnectionProfileRepository(workspace, new InMemoryProfileSecretStore());
        var profile = (await owner.GetAllAsync()).Single(item => item.Id == profileId);
        var checkpoints = (IImportCheckpointRepository)owner;
        var gate = new AfterBulkWriteGate(checkpoints, ready);
        var sourcePath = Path.Combine(root, "source.json");
        var request = new StandaloneImportRequest(sourcePath, "sample_mflix", collectionName,
            new TransferImportSchema(TransferImportFormat.JsonArray), DatabaseImportDuplicatePolicy.Reject, "sample_mflix");
        var mongo = new MongoWorkspaceService(new LocalMongoDatabaseExportFileAccess(root), importCheckpoints: gate);
        await mongo.ImportStandaloneAsync(profile with { ConnectionString = uri }, request);
        Assert.Fail("O worker deveria permanecer suspenso antes de persistir os contadores após BulkWrite.");
    }

    private static Process StartWorker(string uri, string workspace, string root, string ready, Guid profileId, string collection)
    {
        var startInfo = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        startInfo.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName~CrashWorkerPausesAfterBulkWriteBeforeCheckpointUpdate");
        startInfo.ArgumentList.Add("--logger:console;verbosity=minimal");
        startInfo.Environment[WorkerVariable] = "1";
        startInfo.Environment[ConnectionVariable] = uri;
        startInfo.Environment[WorkspaceVariable] = workspace;
        startInfo.Environment[RootVariable] = root;
        startInfo.Environment[ReadyVariable] = ready;
        startInfo.Environment[ProfileIdVariable] = profileId.ToString("D");
        startInfo.Environment[CollectionVariable] = collection;
        return Process.Start(startInfo) ?? throw new InvalidOperationException("Não foi possível iniciar o worker de importação.");
    }

    private static string RequiredEnvironment(string name) =>
        Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException($"Variável de worker ausente: {name}.");

    private sealed class AfterBulkWriteGate(IImportCheckpointRepository inner, string readyPath) : IImportCheckpointRepository
    {
        public Task<IReadOnlyList<ImportCheckpoint>> GetPendingAsync(CancellationToken cancellationToken = default) =>
            inner.GetPendingAsync(cancellationToken);
        public Task CreateAsync(ImportCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
            inner.CreateAsync(checkpoint, cancellationToken);
        public async Task UpdateAsync(ImportCheckpoint checkpoint, CancellationToken cancellationToken = default)
        {
            await File.WriteAllTextAsync(readyPath, checkpoint.Id.ToString("N"), cancellationToken);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public Task ResetForRestartAsync(ImportCheckpoint expected, ImportCheckpoint restarted, CancellationToken cancellationToken = default) =>
            inner.ResetForRestartAsync(expected, restarted, cancellationToken);
        public Task CompleteAsync(Guid id, CancellationToken cancellationToken = default) => inner.CompleteAsync(id, cancellationToken);
    }
}
