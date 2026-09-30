using System.Text.Json;
using System.Text.Json.Nodes;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class UuidWorkspaceTests
{
    private const string Sample = "00112233-4455-6677-8899-aabbccddeeff";
    private static string Binary(string hex, string subType) =>
        "{\"$binary\":{\"base64\":\"" + Convert.ToBase64String(Convert.FromHexString(hex)) + "\",\"subType\":\"" + subType + "\"}}";
    private static readonly string CSharpLegacyDocument = "{\"_id\":1,\"customerId\":" + Binary("33221100554477668899aabbccddeeff", "03") + ",\"standard\":" + Binary("00112233445566778899aabbccddeeff", "04") + "}";

    [Test]
    public async Task OldSessionWithoutUuidPreferenceLoadsStandardAndPersistsOverridesAsText()
    {
        var path = NewDatabasePath();
        WriteRawSession(path, OldSessionJson());
        using var context = new WorkspaceTestContext();
        var profileId = Guid.NewGuid();
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            Assert.That((await repository.LoadSessionAsync()).Preferences.UuidRepresentation, Is.EqualTo(UuidRepresentation.Standard));
            using var workspace = new WorkspaceViewModel(context.Workspace, repository);
            await workspace.InitializeAsync();
            Assert.That(workspace.UuidRepresentation, Is.EqualTo(UuidRepresentation.Standard));
            Assert.That(workspace.CaptureUuidPolicy().Overrides, Is.Empty);
            await workspace.SetUuidRepresentationAsync(UuidRepresentation.GoStandard);
            await workspace.SetProfileUuidRepresentationAsync(profileId, UuidRepresentation.JavaLegacy);
        }
        Assert.That(ReadRawSession(path), Does.Contain("\"UuidRepresentation\":\"GoStandard\"").And.Contain("\"JavaLegacy\"").And.Contain("\"Theme\":\"Escuro\""));
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            using var restored = new WorkspaceViewModel(context.Workspace, repository);
            await restored.InitializeAsync();
            Assert.Multiple(() =>
            {
                Assert.That(restored.CaptureUuidPolicy().Resolve(profileId), Is.EqualTo(UuidRepresentation.JavaLegacy));
                Assert.That(restored.CaptureUuidPolicy().Resolve(Guid.NewGuid()), Is.EqualTo(UuidRepresentation.GoStandard));
                Assert.That(restored.ActiveTab!.UuidPolicy.Resolve(profileId), Is.EqualTo(UuidRepresentation.JavaLegacy));
                Assert.That(restored.Theme, Is.EqualTo("Escuro"));
            });
        }
    }

    [TestCase("\"PythonLegacy\"")]
    [TestCase("9")]
    public async Task UnknownPersistedUuidPreferenceIsVisibleAndNeverOverwritten(string value)
    {
        var path = NewDatabasePath();
        var original = OldSessionJson(preferences => preferences["UuidRepresentation"] = JsonNode.Parse(value));
        WriteRawSession(path, original);
        using var context = new WorkspaceTestContext();
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            Assert.CatchAsync(() => repository.LoadSessionAsync());
            using var workspace = new WorkspaceViewModel(context.Workspace, repository);
            await workspace.InitializeAsync();
            Assert.That(workspace.SessionStatus, Does.Contain("Não foi possível recuperar"));
            Assert.CatchAsync<InvalidOperationException>(() => workspace.SetUuidRepresentationAsync(UuidRepresentation.CSharpLegacy));
            Assert.That(workspace.ActiveTab!.UuidPolicy.Global, Is.EqualTo(UuidRepresentation.CSharpLegacy), "A escolha permanece aplicada somente em memória.");
            Assert.CatchAsync(() => repository.SaveSessionAsync(new WorkspaceSession()));
        }
        Assert.That(ReadRawSession(path), Is.EqualTo(original));
    }

    [Test]
    public async Task FailedPreferenceSaveStaysVisibleInMemoryAndCanBeRetried()
    {
        using var context = new WorkspaceTestContext();
        var repository = new FailingSessionRepository();
        using var workspace = new WorkspaceViewModel(context.Workspace, repository);
        await workspace.InitializeAsync();
        workspace.UuidPreferences.SelectedChoice = workspace.UuidPreferences.Choices.Single(choice => choice.Value == UuidRepresentation.JavaLegacy);
        await workspace.UuidPreferences.ApplyTask;
        Assert.Multiple(() =>
        {
            Assert.That(workspace.UuidPreferences.HasError, Is.True);
            Assert.That(workspace.UuidPreferences.Status, Does.Contain("não salva").And.Contain("disco indisponível"));
            Assert.That(workspace.SessionStatus, Does.Contain("não salvo"));
            Assert.That(workspace.ActiveTab!.UuidPolicy.Global, Is.EqualTo(UuidRepresentation.JavaLegacy));
            Assert.That(workspace.UuidPreferences.Preview.Single(row => row.IsSelected).Code, Is.EqualTo("JUUID(\"00112233-4455-6677-8899-aabbccddeeff\")"));
        });
        repository.FailSave = false;
        await workspace.SetUuidRepresentationAsync(UuidRepresentation.JavaLegacy);
        Assert.That(workspace.SessionStatus, Does.Contain("atualizados"));
    }

    [Test]
    public async Task ConnectionsWithDifferentPoliciesRunConcurrentlyWithoutInterference()
    {
        using var context = new WorkspaceTestContext();
        var csharp = ConnectionProfile.Create("CSharp", "mongodb://a"); var java = ConnectionProfile.Create("Java", "mongodb://b");
        await context.Repository.SaveAsync(csharp); await context.Repository.SaveAsync(java);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        await workspace.InitializeAsync();
        await workspace.SetProfileUuidRepresentationAsync(csharp.Id, UuidRepresentation.CSharpLegacy);
        await workspace.SetProfileUuidRepresentationAsync(java.Id, UuidRepresentation.JavaLegacy);
        var started = new Dictionary<string, TaskCompletionSource> { ["a"] = new(TaskCreationOptions.RunContinuationsAsynchronously), ["b"] = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var responses = new Dictionary<string, TaskCompletionSource<QueryPage>> { ["a"] = new(TaskCreationOptions.RunContinuationsAsynchronously), ["b"] = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        context.Mongo.Handler = (_, args) =>
        {
            var database = ((MongoQuery)args[1]!).Database;
            started[database].TrySetResult();
            return responses[database].Task.WaitAsync((CancellationToken)args[2]!);
        };
        var a = OpenConsoleTab(workspace, csharp, "a"); var b = OpenConsoleTab(workspace, java, "b");
        var renderedA = new List<string>();
        a.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(a.Results)) renderedA.Add(a.Results); };
        var runA = a.ExecuteCommand.ExecuteAsync(null); var runB = b.ExecuteCommand.ExecuteAsync(null);
        await Task.WhenAll(started["a"].Task, started["b"].Task).WaitAsync(TimeSpan.FromSeconds(5));
        // Changed while A is running: A renders with its captured policy first and applies the change only after completion.
        await workspace.SetProfileUuidRepresentationAsync(csharp.Id, UuidRepresentation.JavaLegacy);
        responses["b"].SetResult(new([CSharpLegacyDocument], TimeSpan.Zero, false)); await runB;
        var resultB = b.Results;
        responses["a"].SetResult(new([CSharpLegacyDocument], TimeSpan.Zero, false)); await runA;
        Assert.Multiple(() =>
        {
            Assert.That(renderedA.Count(text => text.Contains("CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")", StringComparison.Ordinal)), Is.EqualTo(1));
            Assert.That(renderedA[^1], Does.Contain("JUUID(\"66774455-0011-2233-ffee-ddccbbaa9988\")"));
            Assert.That(resultB, Does.Contain("JUUID(\"66774455-0011-2233-ffee-ddccbbaa9988\")").And.Not.Contain("CGUUID"));
            Assert.That(b.Metrics, Does.Contain("UUID Java legacy"));
            Assert.That(b.SelectedDocument!.Fields.Single(field => field.Label.StartsWith("customerId", StringComparison.Ordinal)).Label, Is.EqualTo("customerId: JUUID(\"66774455-0011-2233-ffee-ddccbbaa9988\")"));
            Assert.That(b.SelectedDocument.Json, Is.EqualTo(CSharpLegacyDocument), "Os bytes originais permanecem a fonte canônica.");
        });
        await workspace.SetUuidRepresentationAsync(UuidRepresentation.GoStandard);
        Assert.That(b.Results, Is.EqualTo(resultB), "A preferência global não substitui a sobrescrita da conexão.");
        await workspace.SetProfileUuidRepresentationAsync(java.Id, null);
        Assert.That(b.Results, Does.Contain("GUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")"));
        Assert.That(b.Metrics, Does.Contain("1 UUID(s) legado(s) de origem desconhecida"));
    }

    [Test]
    public async Task DocumentTreeClipboardEditorAndExportUseTheirOwnFormats()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("A", "mongodb://host");
        string? replaced = null, precondition = null;
        context.Mongo.Handler = (method, args) =>
        {
            if (method == "QueryAsync") return Task.FromResult(new QueryPage([CSharpLegacyDocument], TimeSpan.Zero, false));
            if (method == "ReplaceAsync") { precondition = (string)args[3]!; replaced = (string)args[4]!; return Task.FromResult(new DocumentMutationResult(1, 1)); }
            throw new InvalidOperationException(method);
        };
        var tab = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, Database = "db", Collection = "col", Mode = "Consulta JSON", Text = "{}", IsConnected = true };
        tab.UuidPolicy = new UuidDisplayPolicy(UuidRepresentation.CSharpLegacy, null);
        await tab.ExecuteCommand.ExecuteAsync(null);
        var document = tab.SelectedDocument!;
        const string expected = "CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")";
        Assert.Multiple(() =>
        {
            Assert.That(document.DisplayJson, Is.EqualTo("{\"_id\":1,\"customerId\":" + expected + ",\"standard\":UUID(\"" + Sample + "\")}"));
            Assert.That(document.Fields[1].Label, Is.EqualTo("customerId: " + expected));
            Assert.That(document.Fields[1].Children, Is.Empty);
            Assert.That(tab.Results, Does.Contain(expected));
            Assert.That(QueryResultExportSerializer.Serialize(tab.Documents), Does.Contain("$binary").And.Not.Contain("CGUUID"));
        });
        using var editor = await tab.CreateDocumentMutationAsync("Editar");
        Assert.That(editor.Text, Does.Contain(expected));
        await editor.ExecuteConfirmedAsync();
        Assert.That(BsonDocument.Parse(UuidCodec.RewriteConstructors(replaced!)), Is.EqualTo(BsonDocument.Parse(CSharpLegacyDocument)));
        Assert.That(precondition, Does.Contain("$binary").And.Not.Contain("CGUUID"));
    }

    [Test]
    public async Task OpenedDocumentScriptRunsInConsoleAndRestoresIdenticalBytes()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("A", "mongodb://host/db");
        await context.Repository.SaveAsync(profile);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        await workspace.InitializeAsync();
        workspace.BindActiveTab(profile, "db", "col");
        var display = new ResultDocumentViewModel(CSharpLegacyDocument, 0, UuidRepresentation.JavaLegacy).DisplayJson;
        workspace.OpenDocumentInEditor(workspace.ActiveTab!, display);
        Assert.That(workspace.ActiveTab!.Text, Does.Contain("JUUID("));
        var runtime = new ConsoleRuntime(context.Repository, context.Repository, new SessionConnectionSecretStore(), new WorkspaceConsoleSession((IMongoWorkspaceService)context.Mongo), context.Repository, context.Repository);
        var result = await runtime.ExecuteAsync(new(profile, "db", workspace.ActiveTab.Text, SaveHistory: false), (_, _) => Task.FromResult(false));
        Assert.That(result.Error, Is.Null, result.Error);
        Assert.That(BsonDocument.Parse(result.Results[^1].Json), Is.EqualTo(BsonDocument.Parse(CSharpLegacyDocument)));
    }

    [Test]
    public void ToolsSnippetUsesTheConnectionRepresentationAndEquivalentCanonicalJson()
    {
        using var context = new WorkspaceTestContext();
        // UUID v4 mode isolates the UUID generator; the three modes are covered by IdentifierModeTests.
        var tools = new MainWindowViewModel(context.Workspace, autoLoadCollections: false) { UuidRepresentation = UuidRepresentation.JavaLegacy, IdentifierMode = IdentifierRepresentationMode.UuidV4 };
        tools.GenerateIdentifierCommand.Execute(null);
        Assert.Multiple(() =>
        {
            Assert.That(tools.IdentifierSnippet, Does.Match("^JUUID\\(\"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\"\\)$"));
            Assert.That(UuidCodec.RewriteConstructors(tools.IdentifierSnippet), Is.EqualTo(tools.IdentifierExtendedJsonSnippet));
            Assert.That(tools.IdentifierExtendedJsonSnippet, Does.Contain("\"subType\":\"03\""));
            Assert.That(tools.IdentifierLabel, Does.Contain("Java legacy").And.Contain("UUID v4"));
        });
    }

    [Test]
    public async Task ConnectionEditorStoresOverrideAndOverrideAloneKeepsExplorerConnected()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Legacy", "mongodb://localhost");
        await context.Repository.SaveAsync(profile);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        await workspace.InitializeAsync(); await workspace.OpenConnectionAsync(profile);
        var connections = new ConnectionsViewModel(context.Workspace, workspace);
        if (connections.Editor.LoadProfilesCommand.ExecutionTask is { } loading) await loading;
        connections.Editor.SelectedProfile = connections.Editor.Profiles.Single();
        connections.Editor.EditSelectedProfileCommand.Execute(null);
        Assert.That(connections.Uuid!.Value, Is.Null);
        Assert.That(connections.Uuid.Preview.Single(row => row.IsSelected).Representation, Is.EqualTo(UuidRepresentation.Standard));
        connections.Uuid.SelectedChoice = connections.Uuid.Choices.Single(choice => choice.Value == UuidRepresentation.CSharpLegacy);
        await connections.Editor.SaveProfileCommand.ExecuteAsync(null);
        Assert.That(workspace.GetProfileUuidRepresentation(profile.Id), Is.EqualTo(UuidRepresentation.CSharpLegacy));
        Assert.That((await context.Repository.LoadSessionAsync()).Preferences.ProfileUuidRepresentations[profile.Id], Is.EqualTo(UuidRepresentation.CSharpLegacy));
        connections.Editor.EditSelectedProfileCommand.Execute(null);
        Assert.That(connections.Uuid.Value, Is.EqualTo(UuidRepresentation.CSharpLegacy));

        using var fresh = new WorkspaceViewModel(context.Workspace, context.Repository);
        await fresh.InitializeAsync(); await fresh.OpenConnectionAsync(profile);
        await fresh.SetProfileUuidRepresentationAsync(profile.Id, UuidRepresentation.JavaLegacy);
        await fresh.ReloadProfilesAsync();
        Assert.That(fresh.Roots.Single().IsConnected, Is.True);
    }

    [Test]
    public async Task ImportFileAcceptsNamedConstructorsWithCanonicalBytes()
    {
        using var synthetic = new SyntheticDirectory();
        var directory = synthetic.Path;
            var file = Path.Combine(directory, "collection-001.extended.json");
            await File.WriteAllTextAsync(file, "[{\"_id\": 1, \"c\": CGUUID(\"" + Sample + "\"), \"texto\": \"JUUID('x')\"}, {\"_id\": 2, \"c\": " + Binary("33221100554477668899aabbccddeeff", "03") + "}]");
            var documents = await MongoWorkspaceService.ReadExportDocumentsAsync(new LocalMongoDatabaseExportFileAccess(), file, CancellationToken.None);
            Assert.That(documents[0]["c"], Is.EqualTo(documents[1]["c"]));
            Assert.That(documents[0]["c"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidLegacy));
            Assert.That(documents[0]["texto"].AsString, Is.EqualTo("JUUID('x')"));
            await File.WriteAllTextAsync(file, "[{\"_id\": 1, \"c\": JUUID(\"bad\")}]");
            Assert.That(Assert.ThrowsAsync<ArgumentException>(() => MongoWorkspaceService.ReadExportDocumentsAsync(new LocalMongoDatabaseExportFileAccess(), file, CancellationToken.None))!.Message, Does.Contain("JUUID(\"bad\")"));
    }

    [Test]
    public void InvalidUuidInEditorDocumentFailsBeforeOpeningConnection()
    {
        var profile = ConnectionProfile.Create("Local", "mongodb://localhost:27017");
        var service = new MongoWorkspaceService(new LocalMongoDatabaseExportFileAccess());
        Assert.That(Assert.ThrowsAsync<ArgumentException>(() => service.InsertAsync(profile, "catalogo", "clientes", "{ \"c\": GUUID(\"123\") }"))!.Message,
            Does.Contain("GUUID(\"123\") não contém um UUID válido"));
    }

    private static WorkspaceTabViewModel OpenConsoleTab(WorkspaceViewModel workspace, ConnectionProfile profile, string database)
    {
        workspace.NewTabCommand.Execute(null);
        var tab = workspace.ActiveTab!;
        tab.Profile = profile; tab.Database = database; tab.IsConnected = true; tab.Text = "db.Customers.find({})";
        return tab;
    }

    private static string NewDatabasePath() =>
        Path.Combine(TestContext.CurrentContext.WorkDirectory, "uuid-session-" + Guid.NewGuid().ToString("N"), "workspace.db");

    private static string OldSessionJson(Action<JsonObject>? change = null)
    {
        var session = JsonSerializer.SerializeToNode(new WorkspaceSession { Preferences = new() { Theme = "Escuro" } })!.AsObject();
        var preferences = session["Preferences"]!.AsObject();
        preferences.Remove(nameof(WorkspacePreferences.UuidRepresentation));
        preferences.Remove(nameof(WorkspacePreferences.ProfileUuidRepresentations));
        change?.Invoke(preferences);
        return session.ToJsonString();
    }

    private static void WriteRawSession(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct");
        database.GetCollection("workspaceSession").Upsert(new LiteDB.BsonDocument { ["_id"] = "current", ["json"] = json });
    }

    private static string ReadRawSession(string path)
    {
        using var database = new LiteDB.LiteDatabase($"Filename={path};Connection=direct");
        return database.GetCollection("workspaceSession").FindById("current")["json"].AsString;
    }
}
