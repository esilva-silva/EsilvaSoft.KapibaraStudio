using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.UnitTests;

public sealed partial class ConsoleMongoIntegrationTests
{
    [Test, Category("MongoReal")]
    public async Task PhaseTwoTwelveStagesJoinArraysAndFacetUseRealServer()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.SlopStudio.slnx"))) root = root.Parent;
        var binaries = Path.Combine(root!.FullName, ".cache", "console-mongo", "server");
        var executable = Environment.GetEnvironmentVariable("SLOP_CONSOLE_MONGOD") ?? (Directory.Exists(binaries) ? Directory.EnumerateFiles(binaries, "mongod.exe", SearchOption.AllDirectories).FirstOrDefault() : null);
        if (executable is null) Assert.Ignore("Fixture MongoDB portátil ausente; defina SLOP_CONSOLE_MONGOD.");
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "aggregation-real-" + Guid.NewGuid().ToString("N"));
        var completed = false;
        try
        {
        using var server = await StartServer(executable!, directory);
        using var pool = new MongoClientPool();
        using var client = new MongoClient(server.Uri);
        var database = client.GetDatabase("advanced");
        await database.GetCollection<BsonDocument>("customers").InsertManyAsync([
            new() { ["_id"] = 1, ["name"] = "Ana" }, new() { ["_id"] = 2, ["name"] = "Bia" }
        ]);
        await database.GetCollection<BsonDocument>("orders").InsertManyAsync([
            new() { ["_id"] = 1, ["customer"] = 1, ["active"] = true, ["items"] = new BsonArray { 10, 20 } },
            new() { ["_id"] = 2, ["customer"] = 2, ["active"] = true, ["items"] = new BsonArray { 5 } },
            new() { ["_id"] = 3, ["customer"] = 1, ["active"] = false, ["items"] = new BsonArray { 100 } }
        ]);
        const string pipeline = """
            [
              { "$match": { "active": true } },
              { "$unwind": "$items" },
              { "$group": { "_id": "$customer", "total": { "$sum": "$items" } } },
              { "$lookup": { "from": "customers", "localField": "_id", "foreignField": "_id", "as": "customer" } },
              { "$unwind": "$customer" },
              { "$set": { "name": "$customer.name", "temporary": true } },
              { "$unset": "temporary" },
              { "$project": { "_id": 0, "name": 1, "total": 1 } },
              { "$facet": {
                  "page": [{ "$sort": { "total": -1 } }, { "$skip": 1 }, { "$limit": 1 }],
                  "count": [{ "$count": "customers" }]
              } }
            ]
            """;
        var profile = ConnectionProfile.Create("Aggregation real", server.Uri, "advanced", isReadOnly: true);
        var mongo = new MongoWorkspaceService(clients: pool);
        var page = await mongo.AggregateAsync(profile, new("advanced", "orders", pipeline));
        var document = BsonDocument.Parse(page.Documents.Single());
        Assert.That(document["page"].AsBsonArray, Has.Count.EqualTo(1));
        Assert.That(document["page"][0]["name"].AsString, Is.EqualTo("Bia"));
        Assert.That(document["page"][0]["total"].AsInt32, Is.EqualTo(5));
        Assert.That(document["page"][0].AsBsonDocument.Names, Is.EquivalentTo(new List<string> { "name", "total" }));
        Assert.That(document["count"][0]["customers"].AsInt32, Is.EqualTo(2));
        var plan = BsonDocument.Parse(await mongo.ExplainAggregationAsync(profile, new("advanced", "orders", pipeline)));
        Assert.That(plan["command"]["aggregate"].AsString, Is.EqualTo("orders"));
        Assert.That(plan["command"]["pipeline"].AsBsonArray, Has.Count.EqualTo(9));
        Assert.That(plan.ToJson(), Does.Contain("queryPlanner"));
        var diagnostic = Assert.ThrowsAsync<QueryDiagnosticException>(() => mongo.AggregateAsync(profile,
            new("advanced", "orders", "[{ $notAStage: { privateValue: 'do-not-echo' } }]")));
        Assert.That(diagnostic!.ServerCode, Is.GreaterThan(0));
        Assert.That(diagnostic.Message, Does.Contain("Stage desconhecido").And.Not.Contain("do-not-echo"));
        using (var consoleSession = new ConsoleDatabaseSessionFactory(pool).Create([profile], 100, 30000))
        {
            var consoleDiagnostic = Assert.ThrowsAsync<QueryDiagnosticException>(() => consoleSession.ExecuteAsync(
                new(profile.Id, "advanced", "orders", "aggregate", "[[{\"$notAStage\":{\"privateValue\":\"do-not-echo\"}}]]"), CancellationToken.None));
            Assert.That(consoleDiagnostic!.Message, Does.Contain("Stage desconhecido").And.Not.Contain("do-not-echo"));
        }
        Assert.ThrowsAsync<InvalidOperationException>(() => mongo.ExplainAggregationAsync(profile, new("advanced", "orders", "[{ $merge: 'shouldNotExist' }]")));
        Assert.ThrowsAsync<InvalidOperationException>(() => mongo.AggregateAsync(profile, new("advanced", "orders", "[{ $out: 'shouldNotExist' }]")));
        Assert.That(await database.ListCollectionNames().ToListAsync(), Does.Not.Contain("shouldNotExist"));
        Assert.That(await database.GetCollection<BsonDocument>("orders").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty), Is.EqualTo(3));
        using (var repository = new LiteDbConnectionProfileRepository(Path.Combine(directory, "partial-workspace.db")))
        {
            await repository.SaveAsync(profile);
            var secrets = new SessionConnectionSecretStore();
            var runtime = new ConsoleRuntime(repository, repository, secrets, new ConsoleDatabaseSessionFactory(pool), repository, repository);
            var workspace = new WorkspaceService(repository, repository, repository, repository, repository, mongo,
                new ControlledScripts(), new LocalScriptFileService(), secrets, repository, console: runtime, consoleHistory: repository);
            var tab = new WorkspaceTabViewModel(workspace) { Profile = profile, IsConnected = true, Database = "advanced",
                Text = "throw new Error('conteudo completo nao deve executar');" };
            var selectedPipeline = "db.orders.aggregate(" + pipeline + ")";
            await tab.ExecuteCommand.ExecuteAsync(selectedPipeline);
            Assert.That(tab.Errors, Is.Empty);
            Assert.That(tab.Results, Does.Contain("Bia"));
            Assert.That(tab.SelectedConsoleResult!.Database, Is.EqualTo("advanced"));
            Assert.That(tab.SelectedConsoleResult.Collection, Is.EqualTo("orders"));
            await tab.ExecuteCommand.ExecuteAsync("db.orders.aggregate([{ $notAStage: {} }])");
            Assert.That(tab.Errors, Does.Contain("Stage desconhecido").And.Not.Contain("conteudo completo"));
            var history = await repository.GetConsoleHistoryAsync();
            Assert.That(history, Has.Count.EqualTo(2));
            Assert.That(history.All(entry => entry.Script != tab.Text), Is.True, "Seleção inválida não pode executar nem registrar o conteúdo completo.");
        }
        TestContext.Out.WriteLine($"MongoDB {server.Version}: 12 stages, join, arrays e facet; página Bia/5, contagem 2; origem intacta e escrita bloqueada.");
        completed = true;
        }
        finally { CleanupDatabaseDirectory(directory, completed); }
    }
}
