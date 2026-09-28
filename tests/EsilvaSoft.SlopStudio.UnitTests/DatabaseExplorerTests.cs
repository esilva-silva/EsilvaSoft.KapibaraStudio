using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class DatabaseExplorerTests
{
    [Test]
    public async Task SrvTargetWaitsForDnsAndPreservesResolvedTxtAuthenticationAndTls()
    {
        var dns = new TaskCompletionSource<MongoUrl>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var routing = ConnectionRouting.ApplyAsync("mongodb+srv://user:password@cluster.example/db", "member.example:27017", (url, token) =>
        {
            Assert.That(url.Scheme, Is.EqualTo(MongoDB.Driver.Core.Configuration.ConnectionStringScheme.MongoDBPlusSrv));
            Assert.That(token, Is.EqualTo(cancellation.Token));
            return dns.Task;
        }, cancellation.Token);
        Assert.That(routing.IsCompleted, Is.False);
        dns.SetResult(new MongoUrl("mongodb://user:password@a.example:27017,b.example:27017/db?authSource=admin&replicaSet=resolved&tls=true"));
        var result = new MongoUrl(await routing);
        Assert.That(result.Server.Host, Is.EqualTo("member.example"));
        Assert.That(result.AuthenticationSource, Is.EqualTo("admin"));
        Assert.That(result.ReplicaSetName, Is.EqualTo("resolved"));
        Assert.That(result.UseTls, Is.True);
    }

    [Test]
    public async Task IndexRemovalUsesSelectedContextAndProtectsMandatoryIndex()
    {
        using var context = new WorkspaceTestContext();
        IndexDropRequest? removed = null;
        context.Mongo.Handler = (method, args) =>
        {
            if (method == "DropIndexAsync") { removed = (IndexDropRequest)args[1]!; return Task.CompletedTask; }
            return Task.FromResult<IReadOnlyList<string>>([]);
        };
        var profile = ConnectionProfile.Create("A", "mongodb://host");
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        var root = new ExplorerNodeViewModel(context.Workspace, profile, "A") { IsConnected = true };
        var indexes = new ExplorerNodeViewModel(context.Workspace, profile, "Índices", "db", "col", kind: ExplorerNodeKind.Indexes, parent: root);
        var index = new ExplorerNodeViewModel(context.Workspace, profile, "email_1", "db", "col", kind: ExplorerNodeKind.Index, parent: indexes,
            index: ExplorerMetadataService.ParseIndex("{\"name\":\"email_1\",\"key\":{\"email\":1}}"));
        await workspace.DropExplorerIndexAsync(index);
        Assert.That(removed, Is.EqualTo(new IndexDropRequest("db", "col", "email_1")));
        var mandatory = new ExplorerNodeViewModel(context.Workspace, profile, "_id_", "db", "col", kind: ExplorerNodeKind.Index, parent: indexes,
            index: ExplorerMetadataService.ParseIndex("{\"name\":\"_id_\",\"key\":{\"_id\":1}}"));
        Assert.ThrowsAsync<ArgumentException>(() => workspace.DropExplorerIndexAsync(mandatory));
    }

    [Test]
    public async Task ExplicitTargetSurvivesDraftRecoveryWithoutConnectingOrPersistingUri()
    {
        using var context = new WorkspaceTestContext(profileSecrets: new InMemoryProfileSecretStore());
        var profile = ConnectionProfile.Create("A", "mongodb://user:private@host/db");
        await context.Repository.SaveAsync(profile);
        using (var workspace = new WorkspaceViewModel(context.Workspace, context.Repository))
        {
            await workspace.InitializeAsync();
            workspace.BindActiveTab(profile with { TargetHost = "member:27017" }, "db", "col");
            workspace.ActiveTab!.Text = "{}";
            await workspace.SaveSessionAsync();
        }
        using var restored = new WorkspaceViewModel(context.Workspace, context.Repository);
        await restored.InitializeAsync();
        Assert.That(restored.ActiveTab!.Profile!.TargetHost, Is.EqualTo("member:27017"));
        Assert.That(restored.ActiveTab.IsConnected, Is.False);
        Assert.That(System.Text.Json.JsonSerializer.Serialize(await context.Repository.LoadSessionAsync()), Does.Not.Contain("private"));
    }

    [Test]
    public async Task OpeningResultInEditorDoesNotPersistResultDataInDrafts()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("A", "mongodb://host/db");
        await context.Repository.SaveAsync(profile);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        await workspace.InitializeAsync();
        workspace.BindActiveTab(profile, "db", "col");
        workspace.OpenDocumentInEditor(workspace.ActiveTab!, "{\"_id\":1,\"value\":\"private-result\"}");
        Assert.That(workspace.ActiveTab!.Text, Does.Contain("private-result"));
        await workspace.SaveSessionAsync();
        Assert.That(System.Text.Json.JsonSerializer.Serialize(await context.Repository.LoadSessionAsync()), Does.Not.Contain("private-result"));
    }

    [Test]
    public async Task SwitchingInstanceKeepsOldTabTargetAndRequiresExplicitRebinding()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("A", "mongodb://host/db");
        await context.Repository.SaveAsync(profile);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        await workspace.InitializeAsync(); await workspace.OpenConnectionAsync(profile);
        workspace.BindActiveTab(profile, "db", "col");
        var original = workspace.ActiveTab!;
        await workspace.SelectInstanceAsync(workspace.Roots[0], "member:27017");
        Assert.That(workspace.Roots[0].Profile.TargetHost, Is.EqualTo("member:27017"));
        Assert.That(original.Profile!.TargetHost, Is.Null);
        Assert.That(original.IsConnected, Is.False);
        workspace.BindActiveTab(workspace.Roots[0].Profile, "db", "col");
        Assert.That(original.Profile.TargetHost, Is.EqualTo("member:27017"));
        Assert.That(original.IsConnected, Is.True);
    }

    [Test]
    public void RoutingLabelRecognizesDirectUriAndEndpointOmitsCredentialsAndQuery()
    {
        var profile = ConnectionProfile.Create("A", "mongodb://user:private@host/db?directConnection=true&appName=a@b");
        Assert.That(profile.Endpoint, Is.EqualTo("host"));
        Assert.That(profile.RoutingLabel, Does.Contain("Instância configurada").And.Not.Contain("private"));
    }

    [Test]
    public async Task SavedConnectionsAppearWithoutNetworkAndChildrenLoadProgressively()
    {
        using var context = new WorkspaceTestContext();
        await context.Repository.SaveAsync(ConnectionProfile.Create("A", "mongodb://server"));
        await context.Repository.SaveAsync(ConnectionProfile.Create("B", "mongodb://other"));
        var calls = new List<string>();
        context.Mongo.Handler = (method, _) =>
        {
            calls.Add(method);
            return method switch
            {
                "GetDatabaseNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["database"]),
                "GetCollectionNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["collection"]),
                "GetIndexesAsync" => Task.FromResult<IReadOnlyList<string>>(["{\"name\":\"_id_\",\"key\":{\"_id\":1}}"]),
                _ => throw new InvalidOperationException(method)
            };
        };
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository);
        await workspace.InitializeAsync();
        Assert.That(workspace.Roots, Has.Count.EqualTo(2)); Assert.That(calls, Is.Empty);
        var root = workspace.Roots[0]; await root.LoadAsync();
        Assert.That(calls.Single(), Is.EqualTo("GetDatabaseNamesAsync"));
        var database = root.Children.Single(); await database.LoadAsync();
        var collection = database.Children.Single(); await collection.LoadAsync();
        var indexes = collection.Children.Single(n => n.Kind == ExplorerNodeKind.Indexes);
        Assert.That(calls, Has.Count.EqualTo(2));
        await indexes.LoadAsync();
        Assert.That(indexes.Children.Single().Index!.Name, Is.EqualTo("_id_"));
        Assert.That(calls, Does.Not.Contain("QueryAsync"));
        Assert.That(workspace.Roots[1].IsConnected, Is.False);
    }

    [Test]
    public async Task RefreshRetainsExpandedNodesAndDoesNotReloadSiblingConnection()
    {
        using var context = new WorkspaceTestContext();
        var root = new ExplorerNodeViewModel(context.Workspace, ConnectionProfile.Create("A", "mongodb://host"), "A");
        await root.LoadAsync();
        var database = root.Children[0]; await database.LoadAsync(); database.IsExpanded = true;
        var collection = database.Children[0]; await collection.LoadAsync(); collection.IsExpanded = true;
        await root.LoadAsync();
        Assert.That(root.Children[0], Is.SameAs(database));
        Assert.That(database.IsExpanded, Is.True);
        Assert.That(database.Children[0], Is.SameAs(collection));
        Assert.That(collection.IsExpanded, Is.True);
    }

    [Test]
    public async Task DisconnectRejectsLateLoadEvenWhenProviderIgnoresCancellation()
    {
        using var context = new WorkspaceTestContext();
        var completion = new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (_, _) => completion.Task;
        var root = new ExplorerNodeViewModel(context.Workspace, ConnectionProfile.Create("A", "mongodb://host"), "A");
        var loading = root.LoadAsync(); root.Invalidate(); completion.SetResult(["stale"]); await loading;
        Assert.That(root.IsConnected, Is.False);
        Assert.That(root.Children.All(n => n.Kind == ExplorerNodeKind.Placeholder), Is.True);
    }

    [Test]
    public async Task MetadataFromOldSelectionCannotOverwriteNewSelection()
    {
        using var context = new WorkspaceTestContext();
        var pending = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Mongo.Handler = (_, _) => pending.Task;
        var profile = ConnectionProfile.Create("A", "mongodb://host");
        var root = new ExplorerNodeViewModel(context.Workspace, profile, "A") { IsConnected = true };
        var database = new ExplorerNodeViewModel(context.Workspace, profile, "old", "old", isDatabase: true, parent: root);
        var index = new IndexInfo("current", "{}", "{}", "{\"name\":\"current\"}", false, false, "", "");
        var leaf = new ExplorerNodeViewModel(context.Workspace, profile, "current", "db", "col", kind: ExplorerNodeKind.Index, parent: root, index: index);
        var details = new ExplorerDetailsViewModel(context.Workspace);
        var old = details.SelectAsync(database); await details.SelectAsync(leaf);
        pending.SetResult("stale database stats"); await old;
        Assert.That(details.Text, Does.Contain("current").And.Not.Contain("stale"));
    }

    [Test]
    public void IndexMetadataRetainsCompoundDirectionsAndAdditionalOptions()
    {
        var index = ExplorerMetadataService.ParseIndex("""{"v":2,"name":"compound","key":{"a":1,"b":-1},"unique":true,"sparse":true,"expireAfterSeconds":60,"partialFilterExpression":{"active":true},"hidden":true,"collation":{"locale":"pt"}}""");
        Assert.That(index.Unique && index.Sparse, Is.True);
        var keys = BsonDocument.Parse(index.Keys);
        Assert.That(string.Join(",", keys.Names), Is.EqualTo("a,b")); Assert.That(keys["b"].ToInt32(), Is.EqualTo(-1));
        var options = BsonDocument.Parse(index.Options);
        Assert.That(options["hidden"].ToBoolean(), Is.True);
        Assert.That(options["expireAfterSeconds"].ToInt32(), Is.EqualTo(60));
        Assert.That(options.Contains("v"), Is.False);
    }

    [Test]
    public void TopologyDistinguishesPrimaryArbiterAndLoadBalancedRestriction()
    {
        var topology = ExplorerMetadataService.ParseTopology("""{"setName":"rs","me":"a:27017","primary":"a:27017","hosts":["a:27017","b:27017"],"arbiters":["c:27017"]}""");
        Assert.That(topology.Kind, Is.EqualTo("Replica Set"));
        Assert.That(topology.Instances.Single(i => i.Host == "a:27017").Role, Is.EqualTo("Primary"));
        Assert.That(topology.Instances.Single(i => i.Host == "c:27017").CanSelect, Is.False);
    }

    [Test]
    public void ExplicitRoutingRetainsAuthenticationAndTlsWithoutChangingSavedUri()
    {
        const string uri = "mongodb://user:password@a:27017,b:27017/database?authSource=admin&replicaSet=rs&tls=true";
        var result = new MongoUrl(ConnectionRouting.Apply(uri, "b:27017"));
        Assert.Multiple(() =>
        {
            Assert.That(result.Servers.Single().Host, Is.EqualTo("b"));
            Assert.That(result.DirectConnection, Is.True);
            Assert.That(result.AuthenticationSource, Is.EqualTo("admin"));
            Assert.That(result.UseTls, Is.True);
            Assert.That(result.Password, Is.EqualTo("password"));
            Assert.That(ConnectionRouting.Apply(uri, null), Is.EqualTo(uri));
        });
    }

    [Test]
    public void ScriptsEscapeNamespaceAndNeverRequirePropertyAccessForCollection()
    {
        var script = ExplorerScripts.Create(ExplorerScriptOperation.Delete, "db\";throw 1", "collection.with.dots");
        Assert.That(script, Does.Contain("getCollection(\"collection.with.dots\")"));
        Assert.That(script, Does.Not.Contain("getSiblingDB(\"db\";throw"));
        Assert.That(script, Does.Contain("deleteMany").And.Contain("Nada foi executado"));
    }

    [Test]
    public void StructuredDocumentPreservesExtendedJsonIdentityAndLargeNumbers()
    {
        const string json = """{"_id":{"$binary":{"base64":"AQIDBAUGBwgJCgsMDQ4PEA==","subType":"03"}},"large":{"$numberLong":"9223372036854775807"},"array":[1,{"name":"x"}]}""";
        var document = new ResultDocumentViewModel(json, 0);
        Assert.That(document.Json, Is.EqualTo(json));
        Assert.That(BsonDocument.Parse(document.IdentityFilter!)["_id"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidLegacy));
        Assert.That(document.Fields[1].Children[0].Json, Is.EqualTo("\"9223372036854775807\""));
        Assert.That(document.Fields[2].Children, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task EditLoadsFullDocumentAndUsesSnapshotPrecondition()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("A", "mongodb://host");
        MongoQuery? reload = null; string? replaced = null; string? filter = null;
        var queryCalls = 0;
        context.Mongo.Handler = (method, args) =>
        {
            if (method == "QueryAsync")
            {
                reload = (MongoQuery)args[1]!; queryCalls++;
                return Task.FromResult(new QueryPage(queryCalls == 1 ? ["{\"_id\":1}"] : ["{\"_id\":1,\"hidden\":\"preserve\"}"], TimeSpan.Zero, false));
            }
            if (method == "ReplaceAsync") { filter = (string)args[3]!; replaced = (string)args[4]!; return Task.FromResult(new DocumentMutationResult(0, 0)); }
            throw new InvalidOperationException(method);
        };
        var tab = new WorkspaceTabViewModel(context.Workspace) { Profile = profile, Database = "db", Collection = "col", Mode = "Consulta JSON", Text = "{}", Projection = "{\"_id\":1}", IsConnected = true };
        await tab.ExecuteCommand.ExecuteAsync(null);
        using var editor = await tab.CreateDocumentMutationAsync("Editar");
        Assert.That(reload!.ProjectionJson, Is.Null);
        Assert.That(editor.Text, Does.Contain("hidden"));
        await editor.ExecuteConfirmedAsync();
        Assert.That(replaced, Does.Contain("hidden"));
        Assert.That(BsonDocument.Parse(filter!)["$and"].AsBsonArray.Count, Is.EqualTo(2));
        Assert.That(editor.Succeeded, Is.False);
        Assert.That(editor.Status, Does.Contain("mudou ou foi removido"));
    }
}
