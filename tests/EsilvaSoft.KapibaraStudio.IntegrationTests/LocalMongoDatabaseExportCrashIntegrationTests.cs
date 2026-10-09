using System.Diagnostics;
using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
[NonParallelizable]
public sealed class LocalMongoDatabaseExportCrashIntegrationTests
{
    private const string OptInVariable = "KAPIBARA_F6_LIVE_MONGO";
    private const string ConnectionVariable = "KAPIBARA_F6_MONGO_URI";
    private const string WorkerModeVariable = "KAPIBARA_F6_EXPORT_CRASH_WORKER";
    private const string RootVariable = "KAPIBARA_F6_EXPORT_CRASH_ROOT";
    private const string DatabaseVariable = "KAPIBARA_F6_EXPORT_CRASH_DATABASE";
    private const string CollectionVariable = "KAPIBARA_F6_EXPORT_CRASH_COLLECTION";
    private const string ReadyVariable = "KAPIBARA_F6_EXPORT_CRASH_READY";

    [Test]
    public async Task AbruptWorkerCrashAfterDurableExportMarkerRecoversOnlyOwnedIncompletePackage()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (!string.Equals(Environment.GetEnvironmentVariable(OptInVariable), "1", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore($"Teste opt-in: defina {OptInVariable}=1 e {ConnectionVariable}; nenhum banco foi acessado.");

        var url = MongoUrl.Create(connectionString);
        Assert.That(url.DatabaseName, Is.EqualTo("sample_mflix"), "A URI deve apontar para sample_mflix como base de homologação.");
        Assert.That(url.Username, Is.Null.Or.Empty, "Este smoke só aceita Mongo local sem credenciais.");
        Assert.That(url.Password, Is.Null.Or.Empty, "Este smoke só aceita Mongo local sem credenciais.");
        Assert.That(url.Server.Host, Is.AnyOf("localhost", "127.0.0.1", "::1"), "Este smoke só aceita loopback.");

        var run = Guid.NewGuid().ToString("N");
        var sourceDatabase = "f6_export_crash_" + run;
        var sourceCollection = "f6_synthetic_" + run;
        var root = Path.Combine(Path.GetTempPath(), "KapibaraStudio.F6.ExportCrash", run);
        var ready = Path.Combine(root, "worker-ready.txt");
        var committedPackageName = $"f6_preserve-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var unmarkedPackageName = $"f6_unmarked-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        var client = new MongoClient(connectionString);
        var databasesBefore = await client.ListDatabaseNames().ToListAsync();
        Assert.That(databasesBefore, Does.Not.Contain(sourceDatabase), "A base GUID já existe; fixture interrompida antes de escrever.");
        Assert.That(Directory.Exists(root), Is.False, "A pasta GUID já existe; fixture interrompida antes de escrever.");

        Process? child = null;
        var childExited = false;
        var sourceCreated = false;
        var packageMarkerSeen = false;
        var recoveryVerified = false;
        try
        {
            Directory.CreateDirectory(root);
            var exportsRoot = Path.Combine(root, "exports");
            var setup = new LocalMongoDatabaseExportFileAccess(exportsRoot);
            var committed = setup.CreateExportDirectory(committedPackageName);
            await setup.WriteAllTextAsync(Path.Combine(committed, "manifest.json"), "{\"FormatVersion\":3}");
            setup.MarkExportDirectoryCommitted(committed);
            var unmarked = Path.Combine(exportsRoot, unmarkedPackageName);
            Directory.CreateDirectory(unmarked);
            await File.WriteAllTextAsync(Path.Combine(unmarked, "user-note.txt"), "preserve");

            var collection = client.GetDatabase(sourceDatabase).GetCollection<BsonDocument>(sourceCollection);
            sourceCreated = true;
            await collection.InsertOneAsync(new BsonDocument
            {
                ["_id"] = run,
                ["_f6Run"] = run,
                ["payload"] = "synthetic export-crash fixture"
            });

            child = StartWorker(connectionString, root, sourceDatabase, sourceCollection, ready);
            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
            while (!File.Exists(ready) && !child.HasExited && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(25);

            Assert.That(File.Exists(ready), Is.True,
                child.HasExited
                    ? $"O worker saiu antes do handshake, código {child.ExitCode}."
                    : "O worker permaneceu ativo, mas não atingiu o limite antes do deadline.");
            var interruptedPackageName = await File.ReadAllTextAsync(ready);
            Assert.That(IsGeneratedNameForDatabase(interruptedPackageName, sourceDatabase), Is.True,
                "O handshake deve reportar o nome gerado pelo próprio exportador para a base GUID.");
            var interruptedDirectory = Path.GetFullPath(Path.Combine(exportsRoot, interruptedPackageName));
            Assert.That(Path.GetDirectoryName(interruptedDirectory), Is.EqualTo(Path.GetFullPath(exportsRoot)),
                "O nome publicado no handshake precisa permanecer diretamente sob a raiz temporária.");
            packageMarkerSeen = File.Exists(Path.Combine(interruptedDirectory, ".kapibara-export-in-progress"));
            Assert.That(packageMarkerSeen, Is.True, "O adapter deveria ter persistido o marker antes do handshake.");
            var collectionFile = Path.Combine(interruptedDirectory, "collection-001.extended.json");
            Assert.That(File.Exists(collectionFile), Is.True,
                "O worker deve pausar somente após o exporter fechar o arquivo da coleção sintética.");
            Assert.That(File.ReadAllText(collectionFile), Does.Contain(run),
                "O pacote parcial precisa conter apenas o documento sintético inserido pela fixture.");
            Assert.That(File.Exists(Path.Combine(interruptedDirectory, "manifest.json")), Is.False,
                "O pacote deve continuar sem manifesto de publicação final.");
            Assert.That(child.HasExited, Is.False, "O processo deve permanecer suspenso no limite determinístico.");

            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            childExited = true;
            Assert.That(child.ExitCode, Is.Not.Zero, "A terminação forçada deve encerrar o processo filho abruptamente.");

            // Uma instância nova não conhece as alocações em memória do worker. O próximo export dispara recovery.
            var restarted = new LocalMongoDatabaseExportFileAccess(exportsRoot);
            var recoveryTrigger = restarted.CreateExportDirectory("recovery-trigger");
            restarted.DeleteExportDirectory(recoveryTrigger);

            Assert.Multiple(() =>
            {
                Assert.That(Directory.Exists(Path.Combine(exportsRoot, interruptedPackageName)), Is.False,
                    "Recovery deve remover somente o pacote incompleto com marker próprio.");
                Assert.That(File.ReadAllText(Path.Combine(committed, "manifest.json")), Is.EqualTo("{\"FormatVersion\":3}"));
                Assert.That(File.Exists(Path.Combine(committed, ".kapibara-export-in-progress")), Is.False);
                Assert.That(File.ReadAllText(Path.Combine(unmarked, "user-note.txt")), Is.EqualTo("preserve"));
            });
            recoveryVerified = true;
        }
        finally
        {
            if (child is not null && !child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                childExited = true;
            }

            if (sourceCreated)
                await DropFixtureDatabaseOnlyIfOwnedAsync(client, sourceDatabase, sourceCollection, run);
            if (recoveryVerified && childExited && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        Assert.That(childExited, Is.True);
        Assert.That(packageMarkerSeen, Is.True);
        Assert.That(await client.ListDatabaseNames().ToListAsync(), Does.Not.Contain(sourceDatabase),
            "A base e os documentos sintéticos temporários devem ter sido removidos.");
        Assert.That(Directory.Exists(root), Is.False, "Todos os arquivos temporários da fixture devem ser removidos.");
    }

    [Test]
    public async Task ExportWorkerPausesAfterCollectionFileIsWrittenBeforeManifestPublication()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(WorkerModeVariable), "1", StringComparison.Ordinal))
            Assert.Ignore("Worker interno, iniciado somente pelo teste pai de crash.");

        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable)
            ?? throw new InvalidOperationException("URI local do worker não informada.");
        var root = Environment.GetEnvironmentVariable(RootVariable)
            ?? throw new InvalidOperationException("Raiz temporária do worker não informada.");
        var databaseName = Environment.GetEnvironmentVariable(DatabaseVariable)
            ?? throw new InvalidOperationException("Banco sintético do worker não informado.");
        var collectionName = Environment.GetEnvironmentVariable(CollectionVariable)
            ?? throw new InvalidOperationException("Coleção sintética do worker não informada.");
        var readyPath = Environment.GetEnvironmentVariable(ReadyVariable)
            ?? throw new InvalidOperationException("Handshake do worker não informado.");
        var access = new ExportDirectoryCapturingFileAccess(new LocalMongoDatabaseExportFileAccess(Path.Combine(root, "exports")));
        var mongo = new MongoWorkspaceService(access);
        var profile = ConnectionProfile.Create("F6 crash export synthetic", connectionString, databaseName);
        var collectionNames = await new MongoClient(connectionString).GetDatabase(databaseName)
            .ListCollectionNames().ToListAsync();
        if (!collectionNames.SequenceEqual([collectionName], StringComparer.Ordinal))
            throw new InvalidOperationException("O worker encontrou namespace diferente da coleção sintética criada pelo pai.");
        var progress = new InlineProgress<DatabaseExportProgress>(update =>
        {
            if (update.CompletedCollections <= 0) return;
            if (update.CompletedCollections != 1 || update.TotalCollections != 1 || update.TotalDocuments != 1)
                throw new InvalidOperationException("O worker não exportou exatamente a coleção/documento sintético esperado.");
            var packageDirectory = access.LastExportDirectory
                ?? throw new InvalidOperationException("O exportador ainda não criou a pasta do pacote.");
            var packageName = Path.GetFileName(packageDirectory);
            if (!File.Exists(Path.Combine(packageDirectory, "collection-001.extended.json")))
                throw new IOException("O exporter sinalizou coleção concluída antes de fechar o arquivo.");
            WriteDurableHandshake(readyPath, packageName);
            Thread.Sleep(Timeout.Infinite);
        });

        await mongo.ExportDatabaseAsync(profile, new DatabaseExportRequest(databaseName) { Progress = progress });
        Assert.Fail("O worker deveria permanecer suspenso até o processo pai encerrá-lo.");
    }

    private static Process StartWorker(string uri, string root, string database, string collection, string ready)
    {
        var startInfo = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
            ? host : "dotnet") { UseShellExecute = false, CreateNoWindow = true };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        startInfo.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName~ExportWorkerPausesAfterCollectionFileIsWrittenBeforeManifestPublication");
        startInfo.ArgumentList.Add("--logger:console;verbosity=minimal");
        startInfo.Environment[WorkerModeVariable] = "1";
        startInfo.Environment[ConnectionVariable] = uri;
        startInfo.Environment[RootVariable] = root;
        startInfo.Environment[DatabaseVariable] = database;
        startInfo.Environment[CollectionVariable] = collection;
        startInfo.Environment[ReadyVariable] = ready;
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o worker de crash da exportação.");
        return process;
    }

    private static async Task DropFixtureDatabaseOnlyIfOwnedAsync(MongoClient client, string databaseName,
        string collectionName, string marker)
    {
        var names = await client.GetDatabase(databaseName).ListCollectionNames().ToListAsync();
        if (names.Count == 0) return;
        if (!names.SequenceEqual([collectionName], StringComparer.Ordinal))
            throw new InvalidOperationException("A base de fixture contém namespace não esperado; preservada para inspeção.");
        var collection = client.GetDatabase(databaseName).GetCollection<BsonDocument>(collectionName);
        var documents = await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        if (documents.Any(document => !document.TryGetValue("_f6Run", out var run) || !run.IsString || run.AsString != marker))
            throw new InvalidOperationException("A base de fixture contém documento sem marcador de propriedade; preservada.");
        await client.DropDatabaseAsync(databaseName);
    }

    private static bool IsGeneratedNameForDatabase(string name, string databaseName)
    {
        if (!name.StartsWith(databaseName + "-", StringComparison.Ordinal)) return false;
        var suffixSeparator = name.LastIndexOf('-');
        if (suffixSeparator < 0 || !Guid.TryParseExact(name[(suffixSeparator + 1)..], "N", out _)) return false;
        var timestamp = name[(databaseName.Length + 1)..suffixSeparator];
        return DateTimeOffset.TryParseExact(timestamp, "yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None, out _);
    }

    private static void WriteDurableHandshake(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private sealed class ExportDirectoryCapturingFileAccess(LocalMongoDatabaseExportFileAccess inner)
        : IMongoDatabaseExportFileAccess
    {
        public string? LastExportDirectory { get; private set; }

        public string NormalizePath(string path) => inner.NormalizePath(path);

        public string CreateExportDirectory(string directoryName)
        {
            var directory = inner.CreateExportDirectory(directoryName);
            LastExportDirectory = directory;
            return directory;
        }

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);
        public bool FileExists(string path) => inner.FileExists(path);
        public Stream CreateNewFile(string path) => inner.CreateNewFile(path);
        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default) =>
            inner.WriteAllTextAsync(path, content, cancellationToken);
        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) =>
            inner.ReadAllTextAsync(path, cancellationToken);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
