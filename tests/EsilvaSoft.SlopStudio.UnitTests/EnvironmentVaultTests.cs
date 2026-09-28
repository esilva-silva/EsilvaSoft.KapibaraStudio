using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class EnvironmentVaultTests
{
    private string _path = null!;
    [SetUp] public void SetUp() => _path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"environments-{Guid.NewGuid():N}.db");
    [TearDown] public void TearDown() { if (File.Exists(_path)) File.Delete(_path); }

    [Test]
    public async Task DirectAndDynamicConnectionsCoexistAcrossEnvironmentChangesAndRestart()
    {
        var direct = ConnectionProfile.Create("Direta", "mongodb://user:password@server/database");
        var dynamic = ConnectionProfile.Create("Dinâmica", """mongodb://user:${ENV.get("MONGO_PASSWORD")}@server/database""");
        var legacy = ConnectionProfile.Create("Legada", "mongodb://user:${MONGODB_PASSWORD}@server/database");
        var store = new InMemoryProfileSecretStore();
        using (var repository = new LiteDbConnectionProfileRepository(_path, store))
        {
            await repository.SaveAsync(direct); await repository.SaveAsync(dynamic); await repository.SaveAsync(legacy);
            var vault = EnvironmentVault.CreateDefault();
            vault.Environments[0].Values["MONGO_PASSWORD"] = "dev:p@ss/#?%";
            vault.Environments[2].Values["MONGO_PASSWORD"] = "prod-password";
            repository.SaveEnvironments(vault);
            var first = new OperationEnvironment(repository, null, dynamic.Id);
            repository.SaveEnvironments(vault with { ActiveEnvironment = "Production" });
            var second = new OperationEnvironment(repository, null, dynamic.Id);
            Assert.Multiple(() =>
            {
                Assert.That(new MongoUrl(first.ResolveConnection(dynamic)).Password, Is.EqualTo("dev:p@ss/#?%"));
                Assert.That(new MongoUrl(second.ResolveConnection(dynamic)).Password, Is.EqualTo("prod-password"));
                Assert.That(first.ResolveConnection(direct), Is.EqualTo(direct.ConnectionString));
                Assert.That(second.ResolveConnection(direct), Is.EqualTo(direct.ConnectionString));
                Assert.That(first.Get("MONGO_PASSWORD"), Is.EqualTo("dev:p@ss/#?%"));
            });
        }
        using var reopened = new LiteDbConnectionProfileRepository(_path);
        Assert.That(reopened.LoadEnvironments().ActiveEnvironment, Is.EqualTo("Production"));
        Assert.That((await reopened.GetAllAsync()).Select(p => p.ConnectionString), Is.EquivalentTo(new[] { "mongodb://user@server/database", dynamic.ConnectionString, legacy.ConnectionString }));
    }

    [Test]
    public void MissingKeyFailsWithoutSubstitutingEmptyText()
    {
        var snapshot = EnvironmentVault.CreateDefault().Capture();
        Assert.That(() => snapshot.Get("MISSING"), Throws.InvalidOperationException.With.Message.Contains("Development"));
    }

    [Test]
    public void JsonValuesCannotInjectFieldsAndExtendedJsonIsPreserved()
    {
        const string value = "\"}, \"injected\": true, \"other\":{\"a\":\"";
        var json = DynamicValues.ResolveJson("""{"secret":ENV.get("key"),"literal":"ENV.get('key')","id":{"$oid":"64b000000000000000000001"},"large":{"$numberLong":"9223372036854775807"}}""", _ => value);
        var bson = BsonDocument.Parse(json);
        Assert.Multiple(() =>
        {
            Assert.That(bson["secret"].AsString, Is.EqualTo(value));
            Assert.That(bson.Contains("injected"), Is.False);
            Assert.That(bson["literal"].AsString, Is.EqualTo("ENV.get('key')"));
            Assert.That(bson["id"].BsonType, Is.EqualTo(BsonType.ObjectId));
            Assert.That(bson["large"].AsInt64, Is.EqualTo(long.MaxValue));
        });
    }

    [Test]
    public async Task CapturedEnvironmentSurvivesConcurrentUpdates()
    {
        using var repository = new LiteDbConnectionProfileRepository(_path);
        var vault = EnvironmentVault.CreateDefault();
        vault.Environments[0].Values["key"] = "before";
        repository.SaveEnvironments(vault);
        var captured = new OperationEnvironment(repository, null, Guid.NewGuid());
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Task.Run(async () => { await release.Task; return captured.Get("key"); });
        vault.Environments[0].Values["key"] = "after";
        repository.SaveEnvironments(vault); release.SetResult();
        Assert.That(await pending, Is.EqualTo("before"));
        Assert.That(new OperationEnvironment(repository, null, Guid.NewGuid()).Get("key"), Is.EqualTo("after"));
    }

    [TestCase("not json")]
    [TestCase("{\"Version\":99,\"ActiveEnvironment\":\"Development\",\"Environments\":[]}")]
    public void UnreadableVaultCannotBeOverwritten(string json)
    {
        using (var database = new LiteDB.LiteDatabase(_path))
            database.GetCollection("environmentVault").Upsert(new LiteDB.BsonDocument { ["_id"] = "current", ["json"] = json });
        using (var repository = new LiteDbConnectionProfileRepository(_path))
        {
            Assert.That(() => repository.LoadEnvironments(), Throws.Exception);
            Assert.That(() => repository.SaveEnvironments(EnvironmentVault.CreateDefault()), Throws.Exception);
        }
        using var check = new LiteDB.LiteDatabase(_path);
        Assert.That(check.GetCollection("environmentVault").FindById("current")["json"].AsString, Is.EqualTo(json));
    }

    [Test]
    public void RejectedSavePreservesPreviousEnvironment()
    {
        using var repository = new LiteDbConnectionProfileRepository(_path);
        repository.SaveEnvironments(EnvironmentVault.CreateDefault());
        Assert.That(() => repository.SaveEnvironments(EnvironmentVault.CreateDefault() with { ActiveEnvironment = "Missing" }), Throws.TypeOf<InvalidDataException>());
        Assert.That(repository.LoadEnvironments().ActiveEnvironment, Is.EqualTo("Development"));
    }

    [Test]
    public void ScriptCredentialsAndValuesStayOutOfArgumentsAndScriptFile()
    {
        var start = MongoshScriptExecutionService.BuildStartInfo("mongodb://user:private-value@server/database", "fixture.js", new Dictionary<string, string> { ["key"] = "private-value" });
        Assert.That(string.Join(" ", start.ArgumentList), Does.Not.Contain("private-value").And.Contain("--nodb"));
        Assert.That(start.Environment["SLOP_CONNECTION_URI"], Does.Contain("private-value"));
        var script = MongoshScriptTemplate.BuildScript("{}", "slop.results.emit({value: ENV.get('key')});", "database");
        Assert.That(script, Does.Not.Contain("private-value"));
        File.WriteAllText(Path.Combine(TestContext.CurrentContext.WorkDirectory, "environment-bootstrap.js"), script);
    }

    [Test]
    public async Task EnvironmentFormSavesCustomValuesAndReportsPersistenceFailure()
    {
        using var context = new WorkspaceTestContext();
        var model = new EnvironmentsViewModel(context.Workspace) { EnvironmentName = "Cliente A" };
        model.AddEnvironmentCommand.Execute(null);
        model.Key = "token"; model.Value = "secret"; model.SetValueCommand.Execute(null);
        await model.SaveCommand.ExecuteAsync(null);
        Assert.That(context.Repository.LoadEnvironments().Capture().Get("token"), Is.EqualTo("secret"));
        context.Repository.Dispose();
        await model.SaveCommand.ExecuteAsync(null);
        Assert.That(model.Status, Does.StartWith("Ambientes não salvos:"));
    }

    [Test]
    public async Task ConnectionFormPersistsDirectPasswordWithReservedCharacters()
    {
        var store = new InMemoryProfileSecretStore();
        using var context = new WorkspaceTestContext(profileSecrets: store);
        var model = new MainWindowViewModel(context.Workspace, autoLoadCollections: false)
        {
            NewProfileName = "Direta", NewProfileConnectionString = "mongodb://server/database",
            NewProfileUsername = "user", NewProfilePassword = "p@ss:/?#%"
        };
        await model.SaveProfileCommand.ExecuteAsync(null);
        var profile = (await context.Repository.GetAllAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(profile.ConnectionString, Is.EqualTo("mongodb://user@server/database"));
            Assert.That(profile.SecretReference, Is.Not.Null);
            Assert.That(new MongoUrl(store.Values[profile.SecretReference!]).Password, Is.EqualTo("p@ss:/?#%"));
            Assert.That(model.SelectedProfile?.ConnectionString, Is.EqualTo(profile.ConnectionString));
        });
    }

    [TestCase("create")]
    [TestCase("rename")]
    [TestCase("dropCollection")]
    [TestCase("dropDatabase")]
    public void AdministrativeEntryPointsResolveEnvironmentBeforeSendingCommands(string action)
    {
        using var repository = new LiteDbConnectionProfileRepository(_path);
        var key = "MISSING_" + Guid.NewGuid().ToString("N");
        var profile = ConnectionProfile.Create("Dinâmica", "mongodb://user:${ENV.get(\"" + key + "\")}@server/database");
        var service = new MongoWorkspaceService(environments: repository);
        var error = Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await (action switch
            {
                "create" => service.CreateDatabaseAsync(profile, new("database", "collection", "database")),
                "rename" => service.RenameCollectionAsync(profile, new("database", "collection", "renamed")),
                "dropCollection" => service.DropCollectionAsync(profile, new("database", "collection", "collection")),
                _ => service.DropDatabaseAsync(profile, new("database", "database"))
            });
        });
        Assert.That(error!.Message, Does.Contain(key).And.Contain("Development"));
    }

    [Test]
    public async Task SwitchingEnvironmentRejectsStaleOpeningAndPreservesRunningTab()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Origem", "mongodb://server/database");
        await context.Repository.SaveAsync(profile);
        using var model = new WorkspaceViewModel(context.Workspace, context.Repository);
        await model.InitializeAsync(); await model.OpenConnectionAsync(profile);
        model.BindActiveTab(profile, "database", "collection");
        var tab = model.ActiveTab!;
        tab.Mode = "Script"; // This scenario exercises the separate mongosh runner.
        var execution = tab.ExecuteCommand.ExecuteAsync("print('pending')");
        Assert.That(tab.IsRunning, Is.True);
        var identity = ConnectionIdentity.From(profile);
        model.Metadata.PutCollections(profile, "database", ["collection"]);
        Assert.That(model.Metadata.IsConnected(identity), Is.True, "Precondition: schema is cached for the still-open connection.");
        var opening = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (_, _) => opening.Task;
        var pending = model.OpenConnectionAsync(profile);
        model.InvalidateEnvironment();
        opening.SetResult(["old-database"]);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await pending);
        Assert.Multiple(() =>
        {
            Assert.That(model.Roots, Has.Count.EqualTo(1));
            Assert.That(model.Roots[0].IsConnected, Is.False);
            Assert.That(model.Roots[0].Children.All(n => n.Kind == ExplorerNodeKind.Placeholder), Is.True);
            Assert.That(tab.IsConnected, Is.False);
            Assert.That(tab.IsRunning, Is.True);
            // The invalidation must reach the metadata cache, not just the explorer/tab UI state: a stale schema
            // must never be served to autocomplete after the environment/credentials it was learned under changed.
            Assert.That(model.Metadata.IsConnected(identity), Is.False, "Invalidating the environment must disconnect the cache, not only the UI.");
            Assert.That(model.Metadata.GetCollections(identity, "database", MetadataAccess.Peek).Value, Is.Null);
        });
        tab.CancelCommand.Execute(null); await execution;
    }
}
