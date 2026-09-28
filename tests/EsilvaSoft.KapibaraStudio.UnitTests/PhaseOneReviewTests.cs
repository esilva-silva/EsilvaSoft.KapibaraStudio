using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class PhaseOneReviewTests
{
    private static readonly ConnectionProfile Profile = ConnectionProfile.Create("review", "mongodb://host");

    [Test]
    public async Task WriteThroughSurvivesAnOlderLoad()
    {
        var source = new FakeMetadataSource { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        var identity = ConnectionIdentity.From(Profile);
        var pending = cache.RefreshAsync(new(identity, MetadataScope.Collections, "loja"));
        await MetadataCacheTests.WaitUntilAsync(() => source.Calls == 1);
        cache.PutCollections(Profile, "loja", ["nova"]);
        source.Gate.SetResult();
        await pending;
        Assert.That(cache.GetCollections(identity, "loja", MetadataAccess.Peek).Value!.Select(item => item.Name), Is.EqualTo(Expect.Words("nova")));
    }

    [Test]
    public async Task SoftInvalidationSurvivesAnOlderLoad()
    {
        var source = new FakeMetadataSource { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        var identity = ConnectionIdentity.From(Profile);
        cache.PutDatabases(Profile, ["loja"]);
        var pending = cache.RefreshAsync(new(identity, MetadataScope.Databases));
        await MetadataCacheTests.WaitUntilAsync(() => source.Calls == 1);
        cache.Invalidate(new(Profile.Id, MetadataChange.Databases, Strength: InvalidationStrength.Soft));
        source.Gate.SetResult();
        await pending;
        Assert.That(cache.GetDatabases(identity, MetadataAccess.Peek).Freshness, Is.EqualTo(MetadataFreshness.Stale));
    }

    [Test]
    public async Task ExplicitSampleCannotRepopulateADisconnectedProfile()
    {
        var source = new FakeMetadataSource { Gate = new(TaskCreationOptions.RunContinuationsAsynchronously), Sample = [new([new("Nome", "string", [], [])])] };
        using var cache = new MetadataCache(source);
        cache.Connect(Profile);
        var pending = cache.SampleSchemaAsync(Profile, "loja", "clientes");
        await MetadataCacheTests.WaitUntilAsync(() => source.Calls == 1);
        cache.Disconnect(Profile.Id);
        source.Gate.SetResult();
        try { await pending; } catch (OperationCanceledException) { }
        Assert.That(cache.GetSampledSchema(ConnectionIdentity.From(Profile), "loja", "clientes", MetadataAccess.Peek).Value, Is.Null);
    }
}
