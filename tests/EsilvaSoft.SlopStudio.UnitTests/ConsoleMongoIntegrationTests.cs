using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed partial class ConsoleMongoIntegrationTests
{
    [Test, Category("MongoReal")]
    public async Task MvpPagesProtectedEditingAndStreamingExportUseRealServer()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.SlopStudio.slnx"))) root = root.Parent;
        var binaries = Path.Combine(root!.FullName, ".cache", "console-mongo", "server");
        var executable = Environment.GetEnvironmentVariable("SLOP_CONSOLE_MONGOD") ?? (Directory.Exists(binaries) ? Directory.EnumerateFiles(binaries, "mongod.exe", SearchOption.AllDirectories).FirstOrDefault() : null);
        if (executable is null) Assert.Ignore("Fixture MongoDB portátil ausente.");
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "mvp-real-" + Guid.NewGuid().ToString("N"));
        var completed = false;
        try
        {
        using var server = await StartServer(executable!, directory);
        using var repository = new LiteDbConnectionProfileRepository(Path.Combine(directory, "workspace.db"));
        using var pool = new MongoClientPool();
        var profile = ConnectionProfile.Create("MVP local", server.Uri, "mvp");
        var mongo = new MongoWorkspaceService(environments: repository, clients: pool);
        var workspace = new EsilvaSoft.SlopStudio.Application.WorkspaceService(repository, repository, repository, repository, repository, mongo,
            new ControlledScripts(), new LocalScriptFileService(), new SessionConnectionSecretStore());
        using var client = new MongoClient(server.Uri);
        var collection = client.GetDatabase("mvp").GetCollection<BsonDocument>("Projects");
        await collection.InsertManyAsync(Enumerable.Range(0, 5000).Select(i => new BsonDocument { ["_id"] = i, ["name"] = "Projeto " + i, ["exact"] = new BsonInt64(long.MaxValue), ["date"] = new BsonDateTime(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)) }));
        Assert.That(await mongo.GetDatabaseNamesAsync(profile), Does.Contain("mvp"));
        Assert.That(await mongo.GetCollectionNamesAsync(profile, "mvp"), Does.Contain("Projects"));
        var timer = Stopwatch.StartNew();
        var first = await workspace.QueryAsync(profile, new("mvp", "Projects", SortJson: "{\"_id\":1}", Limit: 100));
        var next = await workspace.QueryAsync(profile, new("mvp", "Projects", SortJson: "{\"_id\":1}", Limit: 100, Skip: 100));
        Assert.That(first.Documents, Has.Count.EqualTo(100));
        Assert.That(BsonDocument.Parse(next.Documents[0])["_id"].AsInt32, Is.EqualTo(100));
        var original = new EsilvaSoft.SlopStudio.Desktop.ViewModels.ResultDocumentViewModel(first.Documents[0], 0);
        using (var edit = new EsilvaSoft.SlopStudio.Desktop.ViewModels.DocumentMutationViewModel(workspace, profile, "mvp", "Projects", original, "Editar", rereadBeforeWrite: true))
        {
            edit.Text = original.FormattedJson.Replace("Projeto 0", "Editado", StringComparison.Ordinal);
            await edit.ExecuteConfirmedAsync();
            Assert.That(edit.Succeeded, Is.True, edit.Status);
        }
        using (var stale = new EsilvaSoft.SlopStudio.Desktop.ViewModels.DocumentMutationViewModel(workspace, profile, "mvp", "Projects", original, "Editar", rereadBeforeWrite: true))
        {
            await stale.ExecuteConfirmedAsync();
            Assert.That(stale.Conflict, Is.EqualTo(EsilvaSoft.SlopStudio.Desktop.ViewModels.DocumentWriteConflict.Changed));
        }
        var stored = await collection.Find(new BsonDocument("_id", 0)).SingleAsync();
        Assert.That(stored["name"].AsString, Is.EqualTo("Editado"));
        Assert.That(stored["exact"].AsInt64, Is.EqualTo(long.MaxValue));
        Assert.That(stored["date"].BsonType, Is.EqualTo(BsonType.DateTime));
        using var output = new MemoryStream();
        await QueryResultExportSerializer.WriteAsync(output, next.Documents, false);
        var exported = BsonSerializer.Deserialize<BsonArray>(System.Text.Encoding.UTF8.GetString(output.ToArray()));
        Assert.That(exported.Count, Is.EqualTo(100));
        Assert.That(exported[0]["exact"].AsInt64, Is.EqualTo(long.MaxValue));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await workspace.QueryAsync(profile, new("mvp", "Projects"), cancellation.Token));
        Assert.That(await collection.CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty), Is.EqualTo(5000));
        TestContext.Out.WriteLine($"MongoDB {server.Version}: duas páginas de 100/5000, edição com conflito, BSON, exportação e cancelamento em {timer.ElapsedMilliseconds} ms.");
        completed = true;
        }
        finally { CleanupDatabaseDirectory(directory, completed); }
    }

    [Test, Category("MongoReal")]
    public async Task RealDriverSupportsConsoleApiAcrossTwoDisposableServers()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.SlopStudio.slnx"))) root = root.Parent;
        var binaries = Path.Combine(root!.FullName, ".cache", "console-mongo", "server");
        var executable = Environment.GetEnvironmentVariable("SLOP_CONSOLE_MONGOD") ?? (Directory.Exists(binaries) ? Directory.EnumerateFiles(binaries, "mongod.exe", SearchOption.AllDirectories).FirstOrDefault() : null);
        if (executable is null) Assert.Ignore("Fixture MongoDB portátil ausente; defina SLOP_CONSOLE_MONGOD para homologação real.");
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "console-real-" + Guid.NewGuid().ToString("N"));
        var completed = false;
        try
        {
            using var first = await StartServer(executable!, Path.Combine(directory, "dev"));
            using var second = await StartServer(executable!, Path.Combine(directory, "prod"));
            using var repository = new LiteDbConnectionProfileRepository(Path.Combine(directory, "workspace.db"));
            var dev = ConnectionProfile.Create("Development", first.Uri, "CompanyDb");
            var prod = ConnectionProfile.Create("Production", second.Uri, "CompanyDb");
            await repository.SaveAsync(dev); await repository.SaveAsync(prod);
            var vault = repository.LoadEnvironments(); vault.Environments[0].Values["CUSTOMER"] = "Eduardo"; repository.SaveEnvironments(vault);
            var runtime = new ConsoleRuntime(repository, repository, new SessionConnectionSecretStore(), new ConsoleDatabaseSessionFactory(), repository, repository);
            var confirms = new List<string>();
            var script = """
                db.createCollection("Customers");
                db.Customers.insertOne({_id: 1, Name: ENV.get("CUSTOMER"), Active: true, Big: NumberLong("9223372036854775807")});
                db.Customers.insertMany([{_id: 2, Name: "Ana", Active: true}, {_id: 3, Name: "Bia", Active: false}]);
                db.Customers.find({Name: "Eduardo"});
                getConnection("Development").getDatabase("CompanyDb").getCollection("Customers").find({Name: "Eduardo"});
                ConnectionPool.Development.CompanyDb.Customers.find({Name: "Eduardo"});
                db.Customers.find({}).sort({_id: -1}).skip(1).limit(1).project({Name: 1});
                db.Customers.findOne({_id: 1});
                db.Customers.countDocuments({Active: true});
                db.Customers.estimatedDocumentCount();
                db.Customers.distinct("Name");
                db.Customers.aggregate([{$match: {Active: true}}, {$group: {_id: "$Active", total: {$sum: 1}}}]);
                db.Customers.updateOne({_id: 1}, {$set: {Changed: true}});
                db.Customers.updateMany({Active: true}, {$set: {Verified: true}});
                db.Customers.replaceOne({_id: 3}, {_id: 3, Name: "Changed"});
                db.Customers.createIndex({Name: 1}, {name: "name_idx", unique: true});
                db.Customers.dropIndex("name_idx");
                ConnectionPool.Production.CompanyDb.Customers.insertOne({_id: 10, Name: "Production"});
                const dev = getConnection("Development").getDatabase("CompanyDb").getCollection("Customers");
                const prod = getConnection("Production").getDatabase("CompanyDb").getCollection("Customers");
                const devCustomers = dev.find({}).limit(10);
                const prodCustomers = prod.find({}).limit(10);
                devCustomers;
                prodCustomers;
                console.log("Final:", db.Customers.countDocuments());
                db.Customers.deleteOne({_id: 3});
                db.Customers.deleteMany({Verified: true});
                db.Customers.countDocuments();
                """;
            var result = await runtime.ExecuteAsync(new(dev, "CompanyDb", script), (confirmation, _) => { confirms.Add(confirmation.Context); return Task.FromResult(true); });
            Assert.That(result.Error, Is.Null, result.Error);
            Assert.That(result.ConnectionsUsed, Has.Count.EqualTo(2));
            Assert.That(result.Results[3].Json, Does.Contain("9223372036854775807"));
            Assert.That(result.Results[3].Json, Is.EqualTo(result.Results[4].Json).And.EqualTo(result.Results[5].Json));
            Assert.That(result.Results[6].Documents, Has.Count.EqualTo(1));
            Assert.That(result.Results[6].Json, Does.Contain("Ana").And.Not.Contain("Active"));
            Assert.That(result.Results[^1].Json, Is.EqualTo("0"));
            Assert.That(result.Messages, Does.Contain("Final:"));
            Assert.That(confirms.Any(c => c.StartsWith("Production", StringComparison.Ordinal)), Is.True);
            using var prodClient = new MongoClient(second.Uri);
            Assert.That(await prodClient.GetDatabase("CompanyDb").GetCollection<BsonDocument>("Customers").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty), Is.EqualTo(1));
            var readOnly = prod with { IsReadOnly = true }; await repository.SaveAsync(readOnly);
            var rejected = await runtime.ExecuteAsync(new(dev, "CompanyDb", "ConnectionPool.Production.CompanyDb.Customers.deleteMany({_id: 10})"), (_, _) => Task.FromResult(true));
            Assert.That(rejected.Error, Does.Contain("somente leitura"));
            Assert.That((await repository.GetConsoleHistoryAsync())[0].Status, Is.EqualTo("Erro"));
            var typed = await runtime.ExecuteAsync(new(dev, "CompanyDb", """
                db.Types.insertOne({_id: ObjectId("64b000000000000000000001"), legacy: EJSON.parse('{"$binary":{"base64":"AQIDBAUGBwgJCgsMDQ4PEA==","subType":"03"}}'), decimal: NumberDecimal("1234567890.123456789"), date: new Date("2026-01-01T00:00:00Z")});
                db.Types.findOne({_id: ObjectId("64b000000000000000000001")});
                """), (_, _) => Task.FromResult(true));
            Assert.That(typed.Error, Is.Null, typed.Error);
            var bson = BsonDocument.Parse(typed.Results[1].Json);
            Assert.That(bson["legacy"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidLegacy));
            Assert.That(bson["decimal"].ToString(), Is.EqualTo("1234567890.123456789"));
            Assert.That(bson["date"].BsonType, Is.EqualTo(BsonType.DateTime));
            var uuids = await runtime.ExecuteAsync(new(dev, "CompanyDb", """
                db.Uuids.insertOne({_id: 1, s: UUID("00112233-4455-6677-8899-aabbccddeeff"), c: CGUUID("00112233-4455-6677-8899-AABBCCDDEEFF"), j: JUUID("00112233-4455-6677-8899-aabbccddeeff"), g: GUUID("00112233-4455-6677-8899-AABBCCDDEEFF"), texto: "CGUUID(\"00112233-4455-6677-8899-aabbccddeeff\")"});
                db.Uuids.countDocuments({c: CGUUID("00112233-4455-6677-8899-aabbccddeeff")});
                db.Uuids.countDocuments({c: JUUID("00112233-4455-6677-8899-aabbccddeeff")});
                db.Uuids.countDocuments({s: GUUID("00112233-4455-6677-8899-aabbccddeeff")});
                """), (_, _) => Task.FromResult(true));
            Assert.That(uuids.Error, Is.Null, uuids.Error);
            Assert.That(string.Join("|", uuids.Results.Skip(1).Select(r => r.Json)), Is.EqualTo("1|0|1"));
            using var devClient = new MongoClient(first.Uri);
            var storedUuids = await devClient.GetDatabase("CompanyDb").GetCollection<BsonDocument>("Uuids").Find(new BsonDocument("_id", 1)).SingleAsync();
            Assert.That(Convert.ToHexStringLower(storedUuids["s"].AsBsonBinaryData.Bytes), Is.EqualTo("00112233445566778899aabbccddeeff"));
            Assert.That(Convert.ToHexStringLower(storedUuids["c"].AsBsonBinaryData.Bytes), Is.EqualTo("33221100554477668899aabbccddeeff"));
            Assert.That(Convert.ToHexStringLower(storedUuids["j"].AsBsonBinaryData.Bytes), Is.EqualTo("7766554433221100ffeeddccbbaa9988"));
            Assert.That(storedUuids["g"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
            Assert.That(storedUuids["c"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidLegacy));
            Assert.That(storedUuids["texto"].BsonType, Is.EqualTo(BsonType.String));
            TestContext.Out.WriteLine("MongoDB real: " + first.Version + "; dois processos locais; CRUD, índices, cursores, agregação e contextos validados.");
            completed = true;
        }
        finally { CleanupDatabaseDirectory(directory, completed); }
    }

    private static async Task<Server> StartServer(string executable, string directory)
    {
        Directory.CreateDirectory(directory);
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        var logName = $"{Path.GetFileName(Path.GetDirectoryName(directory))}-{Path.GetFileName(directory)}";
        var logPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"mongod-{logName}.log");
        foreach (var argument in new[] { "--dbpath", directory, "--port", port.ToString(System.Globalization.CultureInfo.InvariantCulture), "--bind_ip", "127.0.0.1", "--logpath", logPath, "--wiredTigerCacheSizeGB", "0.25" }) start.ArgumentList.Add(argument);
        var process = Process.Start(start)!;
        var uri = $"mongodb://127.0.0.1:{port}/?directConnection=true&serverSelectionTimeoutMS=300";
        try
        {
            using var client = new MongoClient(uri);
            for (var attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    var info = await client.GetDatabase("admin").RunCommandAsync<BsonDocument>(new BsonDocument("buildInfo", 1));
                    return new(process, uri, info["version"].AsString);
                }
                catch (TimeoutException) { if (process.HasExited) throw new InvalidOperationException("mongod encerrou; log: " + logPath + Environment.NewLine + (File.Exists(logPath) ? File.ReadAllText(logPath) : "log ainda não criado")); await Task.Delay(100); }
            }
            throw new TimeoutException("mongod não iniciou dentro do limite.");
        }
        catch { if (!process.HasExited) process.Kill(true); process.WaitForExit(); process.Dispose(); throw; }
    }

    private static void CleanupDatabaseDirectory(string directory, bool testCompleted)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            if (Directory.Exists(directory)) throw new IOException("MongoDB temporário continuou presente após limpeza.");
        }
        catch (Exception exception)
        {
            if (testCompleted) throw new IOException($"Falha ao remover o dbpath temporário '{directory}'.", exception);
            TestContext.Error.WriteLine($"Limpeza do dbpath também falhou após falha de teste; preservando a falha original. Diretório: {directory}; erro: {exception.GetType().Name}: {exception.Message}");
        }
    }
    private sealed class Server(Process process, string uri, string version) : IDisposable
    {
        public string Uri { get; } = uri;
        public string Version { get; } = version;
        public void Stop() { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } }
        public void Dispose() { Stop(); process.Dispose(); }
    }
}
