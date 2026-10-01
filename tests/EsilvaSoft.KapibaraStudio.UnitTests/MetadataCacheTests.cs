using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MetadataCacheTests
{
    private static readonly ConnectionProfile Profile = ConnectionProfile.Create("servidor-alfa", "mongodb://host");
    private static ConnectionIdentity Identity => ConnectionIdentity.From(Profile);

    [Test]
    public void DisconnectedProfilesAndPeekNeverLoad()
    {
        var source = new FakeMetadataSource();
        using var cache = new MetadataCache(source);
        Assert.That(cache.GetDatabases(Identity).Freshness, Is.EqualTo(MetadataFreshness.Unknown));
        cache.Connect(Profile);
        Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).Freshness, Is.EqualTo(MetadataFreshness.Unknown));
        Assert.That(source.Calls, Is.Zero);
    }

    [Test]
    public async Task ConcurrentReadsShareOneLoadAndNeverBlock()
    {
        var source = new FakeMetadataSource { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        var views = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => cache.GetDatabases(Identity))));
        Assert.That(views.Select(view => view.Value), Has.All.Null, "Reads return immediately while the load is blocked.");
        Assert.That(views.Select(view => view.Freshness), Has.All.EqualTo(MetadataFreshness.Loading));
        await WaitUntilAsync(() => source.Calls == 1);
        source.Gate.SetResult();
        await cache.RefreshAsync(new(Identity, MetadataScope.Databases));
        var loaded = cache.GetDatabases(Identity);
        Assert.That(loaded.Value, Is.EqualTo(Expect.Words("loja auditoria")));
        Assert.That(loaded.Freshness, Is.EqualTo(MetadataFreshness.Fresh));
        Assert.That(source.Calls, Is.EqualTo(1));
    }

    [Test]
    public async Task StaleValueIsServedWhileASingleRefreshRuns()
    {
        var clock = new MetadataClock();
        var source = new FakeMetadataSource();
        using var cache = new MetadataCache(source, timeProvider: clock);
        cache.Connect(Profile);
        var key = new MetadataKey(Identity, MetadataScope.Databases);
        await cache.RefreshAsync(key);
        clock.Now += TimeSpan.FromMinutes(6);
        source.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        source.Databases = _ => ["loja"];
        var stale = cache.GetDatabases(Identity);
        var again = cache.GetDatabases(Identity);
        Assert.Multiple(() =>
        {
            Assert.That(stale.Value, Is.EqualTo(Expect.Words("loja auditoria")));
            Assert.That(stale.Freshness, Is.EqualTo(MetadataFreshness.Stale));
            Assert.That(stale.IsRefreshing, Is.True);
            Assert.That(again.Value, Is.Not.Null);
        });
        await WaitUntilAsync(() => source.Calls == 2);
        source.Gate.SetResult();
        await cache.RefreshAsync(key);
        Assert.That(cache.GetDatabases(Identity).Value, Is.EqualTo(Expect.Words("loja")));
        Assert.That(source.Calls, Is.EqualTo(2));
    }

    [Test]
    public async Task FailuresBackOffAndKeepThePreviousValueStale()
    {
        var clock = new MetadataClock();
        var source = new FakeMetadataSource();
        using var cache = new MetadataCache(source, timeProvider: clock);
        cache.Connect(Profile);
        var key = new MetadataKey(Identity, MetadataScope.Databases);
        await cache.RefreshAsync(key);
        clock.Now += TimeSpan.FromMinutes(6);
        source.Failure = new InvalidOperationException("host privado");
        await cache.RefreshAsync(key);
        var failed = cache.GetDatabases(Identity);
        Assert.That(failed.Value, Is.Not.Null);
        Assert.That(failed.Freshness, Is.EqualTo(MetadataFreshness.Stale));
        Assert.That(source.Calls, Is.EqualTo(2), "Within the backoff a read never retries.");
        clock.Now += TimeSpan.FromSeconds(31);
        source.Failure = null;
        cache.GetDatabases(Identity);
        await WaitUntilAsync(() => source.Calls == 3);
        var missing = new MetadataKey(Identity, MetadataScope.Collections, "loja");
        source.Failure = new InvalidOperationException();
        await cache.RefreshAsync(missing);
        Assert.That(cache.GetCollections(Identity, "loja", MetadataAccess.Peek).Freshness, Is.EqualTo(MetadataFreshness.Failed));
    }

    [Test]
    public void InvalidationsRemoveOrMarkExactlyTheAffectedKeys()
    {
        var bus = new MetadataInvalidationBus();
        using var cache = new MetadataCache(new FakeMetadataSource(), invalidations: bus);
        var index = ExplorerMetadataService.ParseIndex("""{"name":"email_1","key":{"email":1}}""");
        cache.PutDatabases(Profile, ["loja", "auditoria"]);
        cache.PutCollections(Profile, "loja", ["clientes", "pedidos"]);
        cache.PutCollections(Profile, "auditoria", ["eventos"]);
        cache.PutIndexes(Profile, "loja", "clientes", [index]);
        cache.PutIndexes(Profile, "loja", "pedidos", [index]);

        bus.Publish(new(Profile.Id, MetadataChange.Collections, "loja", "clientes"));
        Assert.Multiple(() =>
        {
            Assert.That(cache.GetCollections(Identity, "loja", MetadataAccess.Peek).Value, Is.Null);
            Assert.That(cache.GetIndexes(Identity, "loja", "clientes", MetadataAccess.Peek).Value, Is.Null);
            Assert.That(cache.GetIndexes(Identity, "loja", "pedidos", MetadataAccess.Peek).Value, Is.Not.Null);
            Assert.That(cache.GetCollections(Identity, "auditoria", MetadataAccess.Peek).Value, Is.Not.Null);
            Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).Freshness, Is.EqualTo(MetadataFreshness.Fresh));
        });

        bus.Publish(new(Profile.Id, MetadataChange.Databases, Strength: InvalidationStrength.Soft));
        Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).Freshness, Is.EqualTo(MetadataFreshness.Stale));
        Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).Value, Is.Not.Null);

        bus.Publish(new(Guid.NewGuid(), MetadataChange.Connection));
        Assert.That(cache.GetIndexes(Identity, "loja", "pedidos", MetadataAccess.Peek).Value, Is.Not.Null, "Another profile is never affected.");
        bus.Publish(new(Profile.Id, MetadataChange.Connection));
        Assert.That(cache.SnapshotNamespaces(100), Is.Empty);
    }

    [Test]
    public async Task DisconnectCancelsLoadsAndDiscardsLateResults()
    {
        var source = new FakeMetadataSource { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously), IgnoreCancellation = true };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        cache.GetDatabases(Identity);
        var pending = cache.RefreshAsync(new(Identity, MetadataScope.Databases));
        await WaitUntilAsync(() => source.Calls == 1);
        cache.Disconnect(Profile.Id);
        source.Gate.SetResult();
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(source.Completed, Is.EqualTo(1));
        Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).Value, Is.Null);
        Assert.That(cache.IsConnected(Identity), Is.False);
    }

    [Test]
    public void WriteThroughDropsRemovedScopesAndEvictsLeastRecentlyUsedCollections()
    {
        using var cache = new MetadataCache(new FakeMetadataSource(), options: new() { CollectionScopedEntriesPerConnection = 2 });
        var index = ExplorerMetadataService.ParseIndex("""{"name":"_id_","key":{"_id":1}}""");
        cache.PutDatabases(Profile, ["a", "b"]);
        cache.PutCollections(Profile, "a", ["c1", "c2", "c3"]);
        cache.PutIndexes(Profile, "a", "c1", [index]);
        cache.PutIndexes(Profile, "a", "c2", [index]);
        cache.GetIndexes(Identity, "a", "c1", MetadataAccess.Peek);
        cache.PutIndexes(Profile, "a", "c3", [index]);
        Assert.Multiple(() =>
        {
            Assert.That(cache.GetIndexes(Identity, "a", "c2", MetadataAccess.Peek).Value, Is.Null, "Least recently used entry is evicted.");
            Assert.That(cache.GetIndexes(Identity, "a", "c1", MetadataAccess.Peek).Value, Is.Not.Null);
            Assert.That(cache.GetIndexes(Identity, "a", "c3", MetadataAccess.Peek).Value, Is.Not.Null);
        });
        cache.PutDatabases(Profile, ["b"]);
        Assert.That(cache.GetCollections(Identity, "a", MetadataAccess.Peek).Value, Is.Null);
        Assert.That(cache.GetIndexes(Identity, "a", "c1", MetadataAccess.Peek).Value, Is.Null);
    }

    [Test]
    public async Task BackgroundLoadsOfDistinctKeysAreBoundedAcrossTheConnection()
    {
        var source = new FakeMetadataSource { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var cache = new MetadataCache(source, options: new() { MaximumConcurrentLoadsPerConnection = 2, MaximumConcurrentLoadsGlobal = 2 });
        cache.Connect(Profile);
        // A burst of six distinct keys of the same connection (six different collections' indexes) must queue past the cap
        // instead of opening one call per key.
        var tasks = Enumerable.Range(0, 6).Select(index => cache.RefreshAsync(new(Identity, MetadataScope.Indexes, "loja", $"c{index}"))).ToArray();
        await WaitUntilAsync(() => source.Calls == 2);
        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(2), "Only the configured cap reaches the source while the rest of the burst is still queued.");
            Assert.That(source.MaxConcurrent, Is.LessThanOrEqualTo(2));
        });
        source.Gate.SetResult();
        await Task.WhenAll(tasks);
        Assert.That(source.Calls, Is.EqualTo(6), "Every queued key is eventually loaded once slots free up.");
    }

    [Test]
    public async Task DisconnectDuringAQueuedBackgroundLoadNeverFaultsTheLoad()
    {
        var source = new FakeMetadataSource { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously), IgnoreCancellation = true };
        using var cache = new MetadataCache(source, options: new() { MaximumConcurrentLoadsPerConnection = 1, MaximumConcurrentLoadsGlobal = 1 });
        cache.Connect(Profile);
        cache.GetDatabases(Identity);
        var pending = cache.RefreshAsync(new(Identity, MetadataScope.Databases));
        await WaitUntilAsync(() => source.Calls == 1);
        // A second key of the same connection queues behind the per-connection gate instead of ever reaching the source.
        cache.GetCollections(Identity, "loja");
        var queued = cache.RefreshAsync(new(Identity, MetadataScope.Collections, "loja"));
        cache.Disconnect(Profile.Id);
        source.Gate.SetResult();
        await Task.WhenAll(pending, queued).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(source.Calls, Is.EqualTo(1), "A carga cancelada na fila nunca chega à origem.");
        Assert.That(cache.GetDatabases(Identity, MetadataAccess.Peek).Value, Is.Null, "Disconnection discards every result of the connection, queued or in flight.");
    }

    [Test]
    public async Task DisposingCacheDrainsLoadsWaitingOnTheGlobalGateWithoutPublishingLateResults()
    {
        var source = new FakeMetadataSource { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously), IgnoreCancellation = true };
        using var cache = new MetadataCache(source, options: new() { MaximumConcurrentLoadsGlobal = 1 });
        var other = ConnectionProfile.Create("other", "mongodb://other-host");
        cache.Connect(Profile);
        cache.Connect(other);
        var first = cache.RefreshAsync(new(Identity, MetadataScope.Databases));
        await WaitUntilAsync(() => source.Calls == 1);
        var queued = cache.RefreshAsync(new(ConnectionIdentity.From(other), MetadataScope.Databases));
        var changes = 0;
        cache.Changed += (_, _) => Interlocked.Increment(ref changes);

        cache.Dispose();
        source.Gate.SetResult();
        await Task.WhenAll(first, queued).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(source.Calls, Is.EqualTo(1), "A carga cancelada na fila global nunca chega à origem.");
            Assert.That(source.Completed, Is.EqualTo(1));
            Assert.That(changes, Is.Zero, "Após descarte, nenhum resultado é publicado.");
            Assert.That(cache.IsConnected(Identity), Is.False);
        });
    }

    [Test]
    public async Task DefinitionsLoadPerCollectionWithValidatorAndKind()
    {
        var source = new FakeMetadataSource
        {
            Collections = _ => [new("clientes", CollectionKind.Collection, """{"$jsonSchema":{"properties":{"Nome":{"bsonType":"string"}}}}"""), new("resumo", CollectionKind.View, null)]
        };
        using var cache = new MetadataCache(source);
        cache.PutCollections(Profile, "loja", ["clientes", "resumo"]);
        cache.Connect(Profile);
        await cache.RefreshAsync(new(Identity, MetadataScope.Definition, "loja", "clientes"));
        await cache.RefreshAsync(new(Identity, MetadataScope.Definition, "loja", "resumo"));
        Assert.That(cache.GetDefinition(Identity, "loja", "clientes", MetadataAccess.Peek).Value!.Validator!.Find("Nome")!.PrimaryType, Is.EqualTo("string"));
        Assert.That(cache.GetDefinition(Identity, "loja", "resumo", MetadataAccess.Peek).Value!.Validator, Is.Null);
        Assert.That(source.Calls, Is.EqualTo(2), "Only the requested collections are loaded, never every validator of the database.");
        Assert.That(cache.GetCollections(Identity, "loja", MetadataAccess.Peek).Value!.Single(entry => entry.Name == "resumo").Kind, Is.EqualTo(CollectionKind.View));
        cache.PutCollections(Profile, "loja", ["clientes", "resumo"]);
        Assert.That(cache.GetCollections(Identity, "loja", MetadataAccess.Peek).Value!.Single(entry => entry.Name == "resumo").Kind, Is.EqualTo(CollectionKind.View),
            "Explorer names keep kinds already known.");
    }

    [Test]
    public async Task SchemaSamplingRequiresOptInOrAnExplicitAction()
    {
        var source = new FakeMetadataSource { Sample = [new([new("Nome", "string", [], [])])] };
        var operations = new ApplicationOperationService();
        using var cache = new MetadataCache(source, operations);
        cache.Connect(Profile);
        Assert.That(cache.GetSampledSchema(Identity, "loja", "clientes").Freshness, Is.EqualTo(MetadataFreshness.Unknown));
        Assert.That(source.Calls, Is.Zero);

        var explicitSchema = await cache.SampleSchemaAsync(Profile, "loja", "pedidos");
        Assert.That(explicitSchema.Find("Nome"), Is.Not.Null);
        Assert.That(cache.GetSampledSchema(Identity, "loja", "pedidos", MetadataAccess.Peek).Value, Is.SameAs(explicitSchema));
        Assert.That(operations.LastCompleted!.Description, Does.Contain("somente nomes e tipos"));

        cache.SetSchemaSamplingAllowed(Profile.Id, true);
        cache.GetSampledSchema(Identity, "loja", "clientes");
        await WaitUntilAsync(() => cache.GetSampledSchema(Identity, "loja", "clientes", MetadataAccess.Peek).Value is not null);
        Assert.That(source.Calls, Is.EqualTo(2));
        Assert.That(cache.SchemaSamplingProfiles, Is.EqualTo(new[] { Profile.Id }));
    }

    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        while (!condition())
        {
            if (System.Diagnostics.Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(5)) Assert.Fail("Condição não atingida em 5 s.");
            await Task.Delay(10);
        }
    }
}
