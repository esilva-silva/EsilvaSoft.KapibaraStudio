using System.Collections.Concurrent;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests.Language;

[TestFixture]
public sealed class MetadataCacheGenerationTests
{
    private static readonly ConnectionProfile Profile = ConnectionProfile.Create("geracao", "mongodb://host-geracao");
    private static ConnectionIdentity Identity => ConnectionIdentity.From(Profile);

    [Test]
    public async Task SlowLoadFinishingAfterWriteThroughKeepsTheWrittenIndexes()
    {
        var source = new ScopedMetadataSource { Blocked = true, IgnoreCancellation = true };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        var changes = Record(cache);
        var key = new MetadataKey(Identity, MetadataScope.Indexes, "loja", "clientes");
        Assert.That(cache.GetIndexes(Identity, "loja", "clientes").Freshness, Is.EqualTo(MetadataFreshness.Loading));
        var pending = cache.RefreshAsync(key);
        await MetadataCacheTests.WaitUntilAsync(() => source.Calls.Count == 1);

        cache.PutIndexes(Profile, "loja", "clientes", [Index("escrito_1")]);
        var written = cache.GetIndexes(Identity, "loja", "clientes", MetadataAccess.Peek);
        Assert.That(written.IsRefreshing, Is.True, "The older load is still running when the Explorer writes through.");
        source.Release();
        await pending;

        var view = cache.GetIndexes(Identity, "loja", "clientes");
        Assert.Multiple(() =>
        {
            Assert.That(view.Value!.Select(index => index.Name), Is.EqualTo(Expect.Words("escrito_1")), "The late listing from the source is discarded.");
            Assert.That(view.Freshness, Is.EqualTo(MetadataFreshness.Fresh));
            Assert.That(view.IsRefreshing, Is.False, "A fresh written value does not schedule another load.");
            Assert.That(source.Calls, Has.Count.EqualTo(1));
            Assert.That(changes.Select(change => change.Key), Is.EqualTo(new[] { key, key }), "Write-through and the terminal state of the load are both announced for the key.");
        });
    }

    [Test]
    public async Task StrongInvalidationDuringLoadDiscardsTheLateListing()
    {
        var source = new ScopedMetadataSource { Blocked = true, IgnoreCancellation = true };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        var pending = cache.RefreshAsync(new(Identity, MetadataScope.Collections, "loja"));
        await MetadataCacheTests.WaitUntilAsync(() => source.Calls.Count == 1);
        cache.Invalidate(new(Profile.Id, MetadataChange.Collections, "loja"));
        source.Release();
        await pending;
        var view = cache.GetCollections(Identity, "loja", MetadataAccess.Peek);
        Assert.That(view.Value, Is.Null);
        Assert.That(view.Freshness, Is.EqualTo(MetadataFreshness.Unknown));
    }

    [Test]
    public async Task ReconnectingDuringAnExplicitSampleThatIgnoresCancellationStoresNothing()
    {
        var source = new ScopedMetadataSource { Blocked = true, IgnoreCancellation = true };
        var operations = new ApplicationOperationService();
        using var cache = new MetadataCache(source, operations);
        cache.Connect(Profile);
        var pending = cache.SampleSchemaAsync(Profile, "loja", "clientes");
        await MetadataCacheTests.WaitUntilAsync(() => source.Calls.Count == 1);
        cache.Disconnect(Profile.Id);
        cache.Connect(Profile);
        source.Release();

        Assert.That(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(cache.GetSampledSchema(Identity, "loja", "clientes", MetadataAccess.Peek).Value, Is.Null, "A new connection never receives a sample of the previous one.");
            Assert.That(operations.LastCompleted!.Status, Is.EqualTo(ApplicationOperationStatus.Cancelled));
            Assert.That(cache.SnapshotNamespaces(10), Is.Empty);
        });
    }

    [TestCase("invalidacao")]
    [TestCase("opt-out")]
    public async Task InvalidationOrOptOutDuringAnExplicitSampleStoresNothing(string change)
    {
        var source = new ScopedMetadataSource { Blocked = true, IgnoreCancellation = true };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        cache.SetSchemaSamplingAllowed(Profile.Id, true);
        var pending = cache.SampleSchemaAsync(Profile, "loja", "clientes");
        await MetadataCacheTests.WaitUntilAsync(() => source.Calls.Count == 1);
        if (change == "opt-out") cache.SetSchemaSamplingAllowed(Profile.Id, false);
        else cache.Invalidate(new(Profile.Id, MetadataChange.Collections, "loja", "clientes"));
        source.Release();

        Assert.That(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(cache.GetSampledSchema(Identity, "loja", "clientes", MetadataAccess.Peek).Value, Is.Null);
    }

    [Test]
    public async Task ExplicitSampleWithoutConcurrentChangesIsStoredAndAnnouncedForItsKey()
    {
        var source = new ScopedMetadataSource();
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        var changes = Record(cache);
        var schema = await cache.SampleSchemaAsync(Profile, "loja", "clientes");
        Assert.That(cache.GetSampledSchema(Identity, "loja", "clientes", MetadataAccess.Peek).Value, Is.SameAs(schema));
        Assert.That(changes.Single().Key, Is.EqualTo(new MetadataKey(Identity, MetadataScope.SampledSchema, "loja", "clientes")));
    }

    [Test]
    public async Task OptOutDuringAutomaticSampleDiscardsItAndStillPublishesTheTerminalChange()
    {
        var source = new ScopedMetadataSource { Blocked = true, IgnoreCancellation = true };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        cache.SetSchemaSamplingAllowed(Profile.Id, true);
        var changes = Record(cache);
        var key = new MetadataKey(Identity, MetadataScope.SampledSchema, "loja", "clientes");
        Assert.That(cache.GetSampledSchema(Identity, "loja", "clientes").Freshness, Is.EqualTo(MetadataFreshness.Loading));
        var pending = cache.RefreshAsync(key);
        await MetadataCacheTests.WaitUntilAsync(() => source.Calls.Count == 1);
        cache.SetSchemaSamplingAllowed(Profile.Id, false);
        source.Release();
        await pending;
        await MetadataCacheTests.WaitUntilAsync(() => !changes.IsEmpty);

        var view = cache.GetSampledSchema(Identity, "loja", "clientes");
        Assert.Multiple(() =>
        {
            Assert.That(view.Value, Is.Null);
            Assert.That(view.IsRefreshing, Is.False, "Without consent the read does not schedule a new sample.");
            Assert.That(source.Calls, Has.Count.EqualTo(1));
            Assert.That(changes.Single().Key, Is.EqualTo(key));
        });
    }

    [Test]
    public async Task FailedLoadWithoutValuePublishesTheTerminalChangeAndTheListStopsLoading()
    {
        var source = new ScopedMetadataSource { Blocked = true, Failure = new InvalidOperationException("mongodb://segredo") };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        var changes = Record(cache);
        var catalog = new KnowledgeCatalog([new MetadataCatalogSource(cache)]);
        var query = new CatalogQuery(SymbolKinds.Collection, EditorDialects.Console) { Connection = Profile, Database = "loja" };

        Assert.That(catalog.Query(query).Completeness, Is.EqualTo(CatalogCompleteness.Loading));
        var pending = cache.RefreshAsync(new(Identity, MetadataScope.Collections, "loja"));
        await MetadataCacheTests.WaitUntilAsync(() => source.Calls.Count == 1);
        Assert.That(changes, Is.Empty, "Nothing is announced before the load ends.");
        source.Release();
        await pending;
        await MetadataCacheTests.WaitUntilAsync(() => !changes.IsEmpty);

        var refiltered = catalog.Query(query with { Access = MetadataAccess.Peek });
        Assert.Multiple(() =>
        {
            Assert.That(changes.Single().ProfileId, Is.EqualTo(Profile.Id));
            Assert.That(changes.Single().Key, Is.EqualTo(new MetadataKey(Identity, MetadataScope.Collections, "loja")));
            Assert.That(cache.GetCollections(Identity, "loja", MetadataAccess.Peek).Freshness, Is.EqualTo(MetadataFreshness.Failed));
            Assert.That(refiltered.Completeness, Is.EqualTo(CatalogCompleteness.Partial), "A failed scope is terminal, not loading.");
            Assert.That(refiltered.Candidates, Is.Empty);
        });
    }

    [Test]
    public async Task PeekSchedulesNoScopeAndReportsUnloadedScopesAsUnavailable()
    {
        var source = new ScopedMetadataSource();
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        cache.SetSchemaSamplingAllowed(Profile.Id, true);
        var catalog = new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(cache)]);
        var query = new CatalogQuery(SymbolKinds.Namespaces | SymbolKinds.Index | SymbolKinds.Field | SymbolKinds.CollectionMethod, EditorDialects.Console)
        {
            Connection = Profile, Connections = [Profile], Database = "loja", Collection = "clientes", Access = MetadataAccess.Peek
        };

        var result = catalog.Query(query);
        await Task.Delay(50);
        Assert.Multiple(() =>
        {
            Assert.That(new CatalogQuery(SymbolKinds.Field, EditorDialects.Console).Access, Is.EqualTo(MetadataAccess.LoadIfNeeded), "Existing callers keep their behavior.");
            Assert.That(result.Completeness, Is.EqualTo(CatalogCompleteness.Unavailable));
            Assert.That(result.Candidates.Select(candidate => candidate.Symbol.Kind), Does.Contain(SymbolKind.CollectionMethod).And.Contain(SymbolKind.Connection));
            Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).IsRefreshing, Is.False);
            Assert.That(cache.GetCollections(Identity, "loja", MetadataAccess.Peek).IsRefreshing, Is.False);
            Assert.That(cache.GetDefinition(Identity, "loja", "clientes", MetadataAccess.Peek).IsRefreshing, Is.False);
            Assert.That(cache.GetIndexes(Identity, "loja", "clientes", MetadataAccess.Peek).IsRefreshing, Is.False);
            Assert.That(cache.GetSampledSchema(Identity, "loja", "clientes", MetadataAccess.Peek).IsRefreshing, Is.False);
            Assert.That(source.Calls, Is.Empty);
        });
    }

    [TestCase(SymbolKinds.Database, "Databases")]
    [TestCase(SymbolKinds.Collection | SymbolKinds.View, "Collections")]
    [TestCase(SymbolKinds.Index, "Indexes")]
    [TestCase(SymbolKinds.Field, "Definition Indexes SampledSchema")]
    public async Task LoadIfNeededSchedulesOnlyTheScopesOfTheRequestedKinds(SymbolKinds kinds, string scopes)
    {
        var expected = Expect.Words(scopes).Select(name => Enum.Parse<MetadataScope>(name)).ToArray();
        var source = new ScopedMetadataSource();
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        cache.SetSchemaSamplingAllowed(Profile.Id, true);
        var catalog = new KnowledgeCatalog([new MetadataCatalogSource(cache)]);
        var query = new CatalogQuery(kinds, EditorDialects.Console) { Connection = Profile, Database = "loja", Collection = "clientes" };

        Assert.That(catalog.Query(query).Completeness, Is.EqualTo(CatalogCompleteness.Loading));
        await MetadataCacheTests.WaitUntilAsync(() => source.Calls.Count == expected.Length);
        await MetadataCacheTests.WaitUntilAsync(() => source.Completed == expected.Length);
        await Task.Delay(50);
        Assert.That(source.Calls, Is.EquivalentTo(expected));
        await MetadataCacheTests.WaitUntilAsync(() => catalog.Query(query with { Access = MetadataAccess.Peek }).Completeness == CatalogCompleteness.Complete);
        Assert.That(source.Calls, Has.Count.EqualTo(expected.Length), "Fresh scopes answer later queries from memory.");
    }

    [Test]
    public async Task FiftyConsumersOfOneKeyShareOneCallAndCancelOnlyTheirOwnWait()
    {
        var source = new ScopedMetadataSource { Blocked = true };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        var key = new MetadataKey(Identity, MetadataScope.Collections, "loja");
        var cancellations = Enumerable.Range(0, 50).Select(_ => new CancellationTokenSource()).ToArray();
        var waits = new Task[50];
        var views = new MetadataView<IReadOnlyList<CollectionEntry>>[50];
        try
        {
            Parallel.For(0, 50, index =>
            {
                if (index % 2 == 0) waits[index] = cache.RefreshAsync(key, cancellations[index].Token);
                else { views[index] = cache.GetCollections(Identity, "loja"); waits[index] = Task.CompletedTask; }
            });
            await MetadataCacheTests.WaitUntilAsync(() => source.Calls.Count == 1);
            var cancelled = Enumerable.Range(0, 50).Where(index => index % 4 == 0).ToArray();
            foreach (var index in cancelled) await cancellations[index].CancelAsync();
            foreach (var index in cancelled) Assert.That(async () => await waits[index], Throws.InstanceOf<OperationCanceledException>());
            Assert.That(views.Where((_, index) => index % 2 == 1).Select(view => view.Freshness), Has.All.EqualTo(MetadataFreshness.Loading));

            source.Release();
            await Task.WhenAll(waits.Where((_, index) => index % 4 != 0));
            var loaded = cache.GetCollections(Identity, "loja", MetadataAccess.Peek);
            Assert.Multiple(() =>
            {
                Assert.That(loaded.Value!.Select(entry => entry.Name), Is.EqualTo(Expect.Words("origem")), "Cancelling some waits never cancels the shared load.");
                Assert.That(loaded.Freshness, Is.EqualTo(MetadataFreshness.Fresh));
                Assert.That(source.Calls, Has.Count.EqualTo(1));
            });
        }
        finally
        {
            foreach (var cancellation in cancellations) cancellation.Dispose();
        }
    }

    private static ConcurrentQueue<MetadataChangedEventArgs> Record(MetadataCache cache)
    {
        var changes = new ConcurrentQueue<MetadataChangedEventArgs>();
        cache.Changed += (_, e) => changes.Enqueue(e);
        return changes;
    }

    private static IndexInfo Index(string name) => ExplorerMetadataService.ParseIndex("{\"name\":\"" + name + "\",\"key\":{\"a\":1}}");
}

/// <summary>Records the scope of every remote call; an optional gate holds all calls until released.</summary>
internal sealed class ScopedMetadataSource : IMongoMetadataSource
{
    private readonly ConcurrentQueue<MetadataScope> _calls = new();
    private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _completed;
    public bool Blocked { get; init; }
    public bool IgnoreCancellation { get; init; }
    public Exception? Failure { get; init; }
    public IReadOnlyList<MetadataScope> Calls => _calls.ToArray();
    public int Completed => Volatile.Read(ref _completed);

    public void Release() => _gate.TrySetResult();

    public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) =>
        Call<IReadOnlyList<string>>(MetadataScope.Databases, ["origem"], cancellationToken);
    public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum, CancellationToken cancellationToken) =>
        Call(MetadataScope.Databases, new BoundedMetadataResult<string>(["origem"], false), cancellationToken);
    public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) =>
        Call<IReadOnlyList<CollectionEntry>>(MetadataScope.Collections, [new("origem", CollectionKind.Unknown)], cancellationToken);
    public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken) =>
        Call(MetadataScope.Collections, new BoundedMetadataResult<CollectionEntry>([new("origem", CollectionKind.Unknown)], false), cancellationToken);
    public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) =>
        Call<CollectionDefinition?>(MetadataScope.Definition, new(collection, CollectionKind.Collection, """{"$jsonSchema":{"properties":{"Nome":{"bsonType":"string"}}}}"""), cancellationToken);
    public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) =>
        Call<IReadOnlyList<IndexInfo>>(MetadataScope.Indexes, [ExplorerMetadataService.ParseIndex("""{"name":"origem_1","key":{"Codigo":1}}""")], cancellationToken);
    public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) =>
        Call<IReadOnlyList<SampledDocument>>(MetadataScope.SampledSchema, [new([new("Amostrado", "string", [], [])])], cancellationToken);
    public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes, CancellationToken cancellationToken) =>
        Call(MetadataScope.SampledSchema, new ConcreteCollectionSchemaSampleResult(
            ConcreteCollectionSchemaSampleStatus.Sampled, [new([new("Amostrado", "string", [], [])])]), cancellationToken);

    private async Task<T> Call<T>(MetadataScope scope, T value, CancellationToken cancellationToken)
    {
        _calls.Enqueue(scope);
        try
        {
            if (Blocked) await (IgnoreCancellation ? _gate.Task : _gate.Task.WaitAsync(cancellationToken));
            if (Failure is { } failure) throw failure;
            return value;
        }
        finally { Interlocked.Increment(ref _completed); }
    }
}
