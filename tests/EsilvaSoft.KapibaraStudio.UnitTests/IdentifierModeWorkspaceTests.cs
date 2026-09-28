using System.Text.Json;
using System.Text.Json.Nodes;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Bson;
using static EsilvaSoft.KapibaraStudio.UnitTests.IdentifierModeTestFixture;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Identifier-mode behavior across session persistence, the workspace view models, console and import (see also <see cref="IdentifierModeTests"/> for the domain-level rules).</summary>
[TestFixture]
public sealed class IdentifierModeWorkspaceTests
{
    [TestCase("Standard")]
    [TestCase("JavaLegacy")]
    public async Task PreviousSessionKeepsItsUuidRepresentationAndGetsStandardIdentifierMode(string uuid)
    {
        var path = NewDatabasePath();
        WriteRawSession(path, SessionJsonWithoutIdentifierMode(preferences => preferences["UuidRepresentation"] = uuid));
        using var context = new WorkspaceTestContext();
        var expected = Enum.Parse<UuidRepresentation>(uuid);
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            var loaded = await repository.LoadSessionAsync();
            Assert.That((loaded.Preferences.IdentifierMode, loaded.Preferences.UuidRepresentation), Is.EqualTo((IdentifierRepresentationMode.Standard, expected)));
            using var workspace = new WorkspaceViewModel(context.Workspace, repository);
            await workspace.InitializeAsync();
            Assert.That((workspace.IdentifierMode, workspace.UuidRepresentation), Is.EqualTo((IdentifierRepresentationMode.Standard, expected)));
            await workspace.SetIdentifierModeAsync(IdentifierRepresentationMode.UuidV4);
        }
        Assert.That(ReadRawSession(path), Does.Contain("\"IdentifierMode\":\"UuidV4\"").And.Contain("\"UuidRepresentation\":\"" + uuid + "\"").And.Contain("\"Theme\":\"Escuro\""));
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            using var restored = new WorkspaceViewModel(context.Workspace, repository);
            await restored.InitializeAsync();
            Assert.Multiple(() =>
            {
                Assert.That((restored.IdentifierMode, restored.UuidRepresentation), Is.EqualTo((IdentifierRepresentationMode.UuidV4, expected)));
                Assert.That(restored.ActiveTab!.UuidPolicy.ResolveOptions(null), Is.EqualTo(Options(IdentifierRepresentationMode.UuidV4, expected)));
                Assert.That(restored.IdentifierPreferences.Mode, Is.EqualTo(IdentifierRepresentationMode.UuidV4));
            });
        }
    }

    [TestCase("\"StandardUuid\"")]
    [TestCase("7")]
    public async Task UnknownPersistedIdentifierModeIsVisibleAndNeverOverwritten(string value)
    {
        var path = NewDatabasePath();
        var original = SessionJsonWithoutIdentifierMode(preferences => preferences["IdentifierMode"] = JsonNode.Parse(value));
        WriteRawSession(path, original);
        using var context = new WorkspaceTestContext();
        using (var repository = new LiteDbConnectionProfileRepository(path))
        {
            Assert.CatchAsync(() => repository.LoadSessionAsync());
            using var workspace = new WorkspaceViewModel(context.Workspace, repository);
            await workspace.InitializeAsync();
            Assert.That(workspace.SessionStatus, Does.Contain("Não foi possível recuperar"));
            Assert.CatchAsync<InvalidOperationException>(() => workspace.SetIdentifierModeAsync(IdentifierRepresentationMode.ObjectId));
            Assert.That(workspace.ActiveTab!.UuidPolicy.Mode, Is.EqualTo(IdentifierRepresentationMode.ObjectId), "A escolha permanece aplicada somente em memória.");
            Assert.CatchAsync(() => repository.SaveSessionAsync(new WorkspaceSession()));
        }
        Assert.That(ReadRawSession(path), Is.EqualTo(original));
    }

    [Test]
    public async Task FailedModeSaveStaysVisibleAndThePreviewAdaptsToEachMode()
    {
        using var context = new WorkspaceTestContext();
        var repository = new FailingSessionRepository();
        using var workspace = new WorkspaceViewModel(context.Workspace, repository);
        await workspace.InitializeAsync();
        var preferences = workspace.IdentifierPreferences;
        Assert.That((preferences.Mode, preferences.IsObjectIdPreviewVisible, preferences.IsUuidPreviewVisible), Is.EqualTo((IdentifierRepresentationMode.Standard, true, true)));
        preferences.SelectedChoice = preferences.Choices.Single(choice => choice.Value == IdentifierRepresentationMode.UuidV4);
        await preferences.ApplyTask;
        Assert.Multiple(() =>
        {
            Assert.That(preferences.HasError, Is.True);
            Assert.That(preferences.Status, Does.Contain("não salvo").And.Contain("disco indisponível"));
            Assert.That(workspace.ActiveTab!.UuidPolicy.Mode, Is.EqualTo(IdentifierRepresentationMode.UuidV4));
            Assert.That(workspace.UuidRepresentation, Is.EqualTo(UuidRepresentation.Standard), "O modo não altera a representação UUID.");
            Assert.That(preferences.Description, Is.EqualTo(IdentifierRepresentationService.Description(IdentifierRepresentationMode.UuidV4)));
            Assert.That((preferences.ObjectIdPreview.Count, preferences.UuidPreview.Count, workspace.UuidPreferences.IsPreviewVisible), Is.EqualTo((0, 1, true)));
        });
        repository.FailSave = false;
        preferences.SelectedChoice = preferences.Choices.Single(choice => choice.Value == IdentifierRepresentationMode.ObjectId);
        await preferences.ApplyTask;
        Assert.Multiple(() =>
        {
            Assert.That((preferences.HasError, workspace.IdentifierMode), Is.EqualTo((false, IdentifierRepresentationMode.ObjectId)));
            Assert.That(preferences.ObjectIdPreview.Select(row => row.Code), Is.EqualTo(new[] { "ObjectId(\"" + Hex + "\")", Hex, Equivalent }));
            Assert.That((preferences.UuidPreview.Count, workspace.UuidPreferences.IsPreviewVisible), Is.EqualTo((0, false)));
        });
        await workspace.SetUuidRepresentationAsync(UuidRepresentation.CSharpLegacy);
        await workspace.SetIdentifierModeAsync(IdentifierRepresentationMode.Standard);
        preferences.Load(workspace.IdentifierMode);
        Assert.That(preferences.UuidPreview.Single().Code, Is.EqualTo("CGUUID(\"0F8FAD5B-D9CB-469F-A165-70867728950E\")"));
    }

    [Test]
    public async Task ModeChangeRerendersResultsButEditorCopiesAndExportKeepTheStoredTypes()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("A", "mongodb://host");
        string? replaced = null, precondition = null;
        context.Mongo.Handler = (method, args) =>
        {
            if (method == "QueryAsync") return Task.FromResult(new QueryPage([Mixed], TimeSpan.Zero, false));
            if (method == "ReplaceAsync") { precondition = (string)args[3]!; replaced = (string)args[4]!; return Task.FromResult(new DocumentMutationResult(1, 1)); }
            throw new InvalidOperationException(method);
        };
        var tab = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, Database = "db", Collection = "col", Mode = "Consulta JSON", Text = "{}", IsConnected = true };
        await tab.ExecuteCommand.ExecuteAsync(null);
        var standardResults = tab.Results;
        Assert.That(standardResults, Does.Contain("\"_id\": ObjectId(\"" + Hex + "\")"));
        Assert.That(tab.SelectedDocument!.IdentityUuidEquivalent, Is.Null);

        tab.UuidPolicy = new UuidDisplayPolicy(UuidRepresentation.Standard, null, IdentifierRepresentationMode.UuidV4);
        var document = tab.SelectedDocument!;
        var documentNode = tab.ResultTree.First(node => node.IsDocument);
        documentNode.EnsureChildren();
        Assert.Multiple(() =>
        {
            Assert.That(tab.Results, Is.EqualTo(standardResults), "O JSON exibido não muda de tipo com o modo.");
            Assert.That(document.IdentityText, Is.EqualTo("_id: ObjectId(\"" + Hex + "\") · UUID " + Equivalent));
            Assert.That(documentNode.Children.Single(node => node.Name == "_id").Value, Is.EqualTo("ObjectId(\"" + Hex + "\") · UUID " + Equivalent));
            Assert.That(document.Fields[0].Label, Is.EqualTo("_id: ObjectId(\"" + Hex + "\") · UUID " + Equivalent));
            Assert.That(document.Fields[0].Children, Is.Empty);
            Assert.That(document.IdentityValueText, Is.EqualTo("ObjectId(\"" + Hex + "\")"));
            Assert.That(document.IdentityUuidEquivalent, Is.EqualTo(Equivalent));
            Assert.That(document.IdentityScript, Is.EqualTo("db.getCollection(\"col\").find({ _id: ObjectId(\"" + Hex + "\") })"));
            Assert.That(document.Json, Is.EqualTo(Mixed));
            Assert.That(tab.Metrics, Does.Contain("IDs UUID v4 · UUID Standard"));
            Assert.That(QueryResultExportSerializer.Serialize(tab.Documents), Does.Contain("$oid").And.Not.Contain("ObjectId(").And.Not.Contain(Equivalent));
        });
        using var editor = await tab.CreateDocumentMutationAsync("Editar");
        Assert.That(editor.Text, Does.Contain("\"_id\": ObjectId(\"" + Hex + "\")").And.Not.Contain(Equivalent));
        await editor.ExecuteConfirmedAsync();
        var written = BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors(replaced!));
        Assert.Multiple(() =>
        {
            Assert.That(written, Is.EqualTo(BsonDocument.Parse(Mixed)));
            Assert.That(written["_id"].BsonType, Is.EqualTo(BsonType.ObjectId));
            Assert.That(precondition, Does.Contain("$oid").And.Not.Contain("ObjectId("));
        });
    }

    [Test]
    public async Task ExplorerScriptsToolsConsoleAndImportUseTheCentralRules()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Legacy", "mongodb://host/db");
        await context.Repository.SaveAsync(profile);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        await workspace.InitializeAsync();
        await workspace.SetProfileUuidRepresentationAsync(profile.Id, UuidRepresentation.CSharpLegacy);
        var node = new ExplorerNodeViewModel(context.Workspace, profile, "col", "db", "col");
        await workspace.SetIdentifierModeAsync(IdentifierRepresentationMode.UuidV4);
        Assert.That(workspace.OpenScript(node, ExplorerScriptOperation.Delete).Text, Does.Contain("_id: CGUUID(\"00000000-0000-4000-8000-000000000000\")"));
        await workspace.SetIdentifierModeAsync(IdentifierRepresentationMode.ObjectId);
        Assert.That(workspace.OpenScript(node, ExplorerScriptOperation.Update).Text, Does.Contain("_id: ObjectId(\"000000000000000000000000\") }").And.Not.Contain("UUID"));

        var tools = new MainWindowViewModel(context.Workspace, autoLoadCollections: false) { IdentifierMode = IdentifierRepresentationMode.Standard, UuidRepresentation = UuidRepresentation.Standard };
        tools.GenerateIdentifierCommand.Execute(null);
        var scripts = tools.IdentifierSnippet.Split('\n');
        Assert.Multiple(() =>
        {
            Assert.That(scripts, Has.Length.EqualTo(2));
            Assert.That(scripts[0], Does.Match("^ObjectId\\(\"[0-9a-f]{24}\"\\)$"));
            Assert.That(scripts[1], Does.Match("^UUID\\(\"[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}\"\\)$"));
            Assert.That(string.Join("\n", scripts.Select(IdentifierRepresentationService.RewriteConstructors)), Is.EqualTo(tools.IdentifierExtendedJsonSnippet));
            Assert.That(tools.IdentifierLabel, Does.StartWith("Identificadores Standard · UUID Standard"));
        });
        tools.IdentifierInput = Hex;
        tools.InterpretIdentifierCommand.Execute(null);
        Assert.That(tools.IdentifierInterpretation, Does.Contain("Tipo: ObjectId (inferido do texto)").And.Contain("UUID equivalente: " + Equivalent).And.Contain("Filtro: { _id: ObjectId(\"" + Hex + "\") }"));
        tools.IdentifierInput = "JUUID(\"bad\")";
        tools.InterpretIdentifierCommand.Execute(null);
        Assert.That(tools.IdentifierInterpretation, Does.Contain("JUUID(\"bad\") não contém um UUID válido"));

        workspace.BindActiveTab(profile, "db", "col");
        var display = new ResultDocumentViewModel(Mixed, 0, Options(IdentifierRepresentationMode.UuidV4, UuidRepresentation.CSharpLegacy)).DisplayJson;
        workspace.OpenDocumentInEditor(workspace.ActiveTab!, display);
        Assert.That(workspace.ActiveTab!.Text, Does.Contain("ObjectId(").And.Contain("CGUUID("));
        var runtime = new ConsoleRuntime(context.Repository, context.Repository, new SessionConnectionSecretStore(), new WorkspaceConsoleSession((IMongoWorkspaceService)context.Mongo), context.Repository, context.Repository);
        var result = await runtime.ExecuteAsync(new(profile, "db", workspace.ActiveTab.Text, SaveHistory: false), (_, _) => Task.FromResult(false));
        Assert.That(result.Error, Is.Null, result.Error);
        Assert.That(BsonDocument.Parse(result.Results[^1].Json), Is.EqualTo(BsonDocument.Parse(Mixed)));

        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "identifier-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "collection-001.extended.json");
            await File.WriteAllTextAsync(file, "[{\"_id\": ObjectId(\"" + Hex + "\"), \"t\": \"ObjectId('x')\"}, {\"_id\": new ObjectId('" + Hex.ToUpperInvariant() + "')}]");
            var documents = await MongoWorkspaceService.ReadExportDocumentsAsync(file, CancellationToken.None);
            Assert.That(documents[0]["_id"], Is.EqualTo(new BsonObjectId(ObjectId.Parse(Hex))));
            Assert.That(documents[1]["_id"], Is.EqualTo(documents[0]["_id"]));
            Assert.That(documents[0]["t"].AsString, Is.EqualTo("ObjectId('x')"));
            await File.WriteAllTextAsync(file, "[{\"_id\": ObjectId(\"12\")}]");
            Assert.That(Assert.ThrowsAsync<ArgumentException>(() => MongoWorkspaceService.ReadExportDocumentsAsync(file, CancellationToken.None))!.Message, Does.Contain("ObjectId(\"12\") não contém um ObjectId válido"));
        }
        finally { Directory.Delete(directory, true); }
        var service = new MongoWorkspaceService();
        Assert.That(Assert.ThrowsAsync<ArgumentException>(() => service.InsertAsync(ConnectionProfile.Create("Local", "mongodb://localhost:27017"), "catalogo", "clientes", "{ \"_id\": ObjectId(\"12\") }"))!.Message,
            Does.Contain("ObjectId(\"12\") não contém um ObjectId válido"), "Falha antes de abrir conexão.");
    }

    private static string NewDatabasePath() =>
        Path.Combine(TestContext.CurrentContext.WorkDirectory, "identifier-session-" + Guid.NewGuid().ToString("N"), "workspace.db");

    private static string SessionJsonWithoutIdentifierMode(Action<JsonObject>? change = null)
    {
        var session = JsonSerializer.SerializeToNode(new WorkspaceSession { Preferences = new() { Theme = "Escuro" } })!.AsObject();
        var preferences = session["Preferences"]!.AsObject();
        preferences.Remove(nameof(WorkspacePreferences.IdentifierMode));
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
