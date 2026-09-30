using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class MetadataInvalidationTests
{
    [Test]
    public async Task WorkspaceOperationsPublishInvalidationsOnlyAfterSuccess()
    {
        using var context = new WorkspaceTestContext();
        var bus = new MetadataInvalidationBus();
        var published = new List<MetadataInvalidation>();
        bus.Published += (_, e) => published.Add(e.Invalidation);
        var mongo = DispatchProxy.Create<IMongoWorkspaceService, MongoTestProxy>();
        ((MongoTestProxy)mongo).Handler = (method, _) => method switch
        {
            "CreateIndexAsync" => Task.FromResult("a_1"),
            "InsertAsync" => Task.FromResult(new DocumentMutationResult(1, 1)),
            "DropCollectionAsync" => Task.FromException(new InvalidOperationException("falha no servidor")),
            _ => (object)Task.CompletedTask
        };
        var workspace = new WorkspaceService(context.Repository, context.Repository, context.Repository, context.Repository, context.Repository, mongo, context.Scripts,
            new MemoryTextFiles(), new SessionConnectionSecretStore(), metadataInvalidation: bus);
        var profile = ConnectionProfile.Create("A", "mongodb://host");

        await workspace.CreateCollectionAsync(profile, new("loja", "clientes"));
        await workspace.RenameCollectionAsync(profile, new("loja", "clientes", "clientes2"));
        await workspace.CreateIndexAsync(profile, new("loja", "clientes2", "{\"a\":1}"));
        await workspace.InsertAsync(profile, "novo", "eventos", "{}");
        await workspace.DropDatabaseAsync(profile, new("antigo", "antigo"));
        Assert.ThrowsAsync<InvalidOperationException>(() => workspace.DropCollectionAsync(profile, new("loja", "clientes2", "clientes2")));

        Assert.That(published, Is.EqualTo(new MetadataInvalidation[]
        {
            new(profile.Id, MetadataChange.Collections, "loja", "clientes"),
            new(profile.Id, MetadataChange.Collections, "loja", "clientes"),
            new(profile.Id, MetadataChange.Collections, "loja", "clientes2"),
            new(profile.Id, MetadataChange.Indexes, "loja", "clientes2"),
            new(profile.Id, MetadataChange.Databases, Strength: InvalidationStrength.Soft),
            new(profile.Id, MetadataChange.Collections, "novo", Strength: InvalidationStrength.Soft),
            new(profile.Id, MetadataChange.Databases, "antigo")
        }), "A failed drop publishes nothing.");
    }

    [Test]
    public async Task ConsoleWritesPublishInvalidationsForTheirNamespace()
    {
        using var workspace = new SyntheticDirectory();
        var directory = workspace.Path;
        var path = Path.Combine(directory, "workspace.db");
        using var repository = new LiteDbConnectionProfileRepository(path);
        var profile = ConnectionProfile.Create("Dev", "mongodb://localhost", "loja");
        await repository.SaveAsync(profile);
        var bus = new MetadataInvalidationBus();
        var published = new List<MetadataInvalidation>();
        bus.Published += (_, e) => published.Add(e.Invalidation);
        var runtime = new ConsoleRuntime(repository, repository, new SessionConnectionSecretStore(), new AcknowledgingConsoleSession(), repository, repository, bus);
        var result = await runtime.ExecuteAsync(new(profile, "loja",
            "db.createCollection('novos'); db.pedidos.createIndex({a: 1}); db.clientes.insertOne({a: 1}); db.clientes.find({});"), (_, _) => Task.FromResult(true));
        Assert.That(result.Error, Is.Null, result.Error);
        Assert.That(published, Is.EqualTo(new MetadataInvalidation[]
        {
            new(profile.Id, MetadataChange.Collections, "loja", "novos"),
            new(profile.Id, MetadataChange.Indexes, "loja", "pedidos"),
            new(profile.Id, MetadataChange.Databases, Strength: InvalidationStrength.Soft),
            new(profile.Id, MetadataChange.Collections, "loja", Strength: InvalidationStrength.Soft)
        }), "Reads publish nothing.");
    }

    [Test]
    public void DistinctHostsWithTheSameNamespaceNamesNeverShareCachedSchema()
    {
        // Same database/collection names, different origin: PEND-K11-SECRET keeps ConnectionIdentity tied to the raw
        // saved connection string, so two profiles pointing at different hosts must never answer from one another's cache.
        using var cache = new MetadataCache(new FakeMetadataSource());
        var profileA = ConnectionProfile.Create("A", "mongodb://host-a/database");
        var profileB = ConnectionProfile.Create("B", "mongodb://host-b/database");
        var identityA = ConnectionIdentity.From(profileA);
        var identityB = ConnectionIdentity.From(profileB);
        Assert.That(identityA, Is.Not.EqualTo(identityB));

        cache.PutCollections(profileA, "loja", ["clientes"]);
        cache.PutCollections(profileB, "loja", ["pedidos"]);

        Assert.Multiple(() =>
        {
            Assert.That(cache.GetCollections(identityA, "loja", MetadataAccess.Peek).Value!.Select(c => c.Name), Is.EqualTo(Expect.Words("clientes")));
            Assert.That(cache.GetCollections(identityB, "loja", MetadataAccess.Peek).Value!.Select(c => c.Name), Is.EqualTo(Expect.Words("pedidos")));
        });
    }

    [Test]
    public async Task ExplorerLoadsWriteThroughAndFeedNamesWithoutRemoteMetadataCalls()
    {
        using var context = new WorkspaceTestContext();
        context.Mongo.Handler = (method, _) => method switch
        {
            "GetDatabaseNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["loja", "auditoria"]),
            "GetCollectionNamesAsync" => Task.FromResult<IReadOnlyList<string>>(["clientes", "pedidos"]),
            "GetIndexesAsync" => Task.FromResult<IReadOnlyList<string>>(["{\"name\":\"email_1\",\"key\":{\"email\":1}}"]),
            _ => throw new NotSupportedException(method)
        };
        var source = new FakeMetadataSource();
        using var cache = new MetadataCache(source);
        var profile = ConnectionProfile.Create("A", "mongodb://host");
        await context.Repository.SaveAsync(profile);
        using var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, metadata: cache);
        await workspace.InitializeAsync();
        await workspace.OpenConnectionAsync(profile);
        var root = workspace.Roots.Single();
        var identity = ConnectionIdentity.From(root.Profile);
        Assert.That(cache.IsConnected(identity), Is.True);
        Assert.That(cache.GetDatabases(identity, MetadataAccess.Peek).Value, Is.EqualTo(Expect.Words("loja auditoria")));

        var database = root.Children.First(); await database.LoadAsync();
        var collection = database.Children.First(); await collection.LoadAsync();
        await collection.Children.Single(node => node.Kind == ExplorerNodeKind.Indexes).LoadAsync();
        workspace.BindActiveTab(root.Profile, "loja", "clientes");
        var tab = workspace.ActiveTab!;
        Assert.Multiple(() =>
        {
            Assert.That(tab.CaptureSyntaxContext().Names, Does.Contain(new SyntaxNamespace("A", "loja", "clientes")).And.Contain(new SyntaxNamespace("A", "loja", "clientes", "email_1")));
            Assert.That(tab.KnownAutocompleteNames(), Does.Contain("clientes").And.Contain("auditoria").And.Contain("A"));
        });

        workspace.Disconnect(root);
        Assert.Multiple(() =>
        {
            Assert.That(cache.IsConnected(identity), Is.False);
            Assert.That(cache.GetDatabases(identity, MetadataAccess.Peek).Value, Is.Null);
            Assert.That(tab.KnownSyntaxNamespaces().Select(item => item.Database), Has.All.Empty, "Disconnected metadata leaves only profile names.");
            Assert.That(source.Calls, Is.Zero, "Explorer data is never loaded a second time.");
        });
    }

    [Test]
    public async Task SchemaSamplingOptInRoundTripsAndInvalidPreferencesAreRejected()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("A", "mongodb://host");
        await context.Repository.SaveAsync(profile);
        using (var cache = new MetadataCache(new FakeMetadataSource()))
        using (var workspace = new WorkspaceViewModel(context.Workspace, context.Repository, metadata: cache))
        {
            await workspace.InitializeAsync();
            await workspace.SetSchemaSamplingAllowedAsync(profile.Id, true);
        }
        using var restoredCache = new MetadataCache(new FakeMetadataSource());
        using var restored = new WorkspaceViewModel(context.Workspace, context.Repository, metadata: restoredCache);
        await restored.InitializeAsync();
        Assert.That(restoredCache.SchemaSamplingProfiles, Is.EqualTo(new[] { profile.Id }));
        Assert.Throws<InvalidDataException>(() => new WorkspacePreferences { SchemaSamplingProfileIds = [Guid.Empty] }.ValidateMetadata());
    }
}

internal sealed class AcknowledgingConsoleSession : IConsoleDatabaseSessionFactory, IConsoleDatabaseSession
{
    public IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs) => this;

    public Task<string> ExecuteAsync(ConsoleOperation operation, CancellationToken cancellationToken) =>
        Task.FromResult(operation.Method is "find" or "aggregate" ? """{"value":[],"truncated":false}""" : """{"value":{"acknowledged":true},"truncated":false}""");

    public void Dispose() { }
}
