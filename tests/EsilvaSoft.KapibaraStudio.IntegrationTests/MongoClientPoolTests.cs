using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Driver;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class MongoClientPoolTests
{
    [Test]
    public void MongoClientsAreReusedOnlyForEquivalentSettings()
    {
        using var pool = new MongoClientPool();
        var first = pool.Get(MongoClientSettings.FromConnectionString("mongodb://localhost:27017/?appName=mvp-test"));
        var again = pool.Get(MongoClientSettings.FromConnectionString("mongodb://localhost:27017/?appName=mvp-test"));
        var changed = pool.Get(MongoClientSettings.FromConnectionString("mongodb://localhost:27017/?appName=mvp-other"));
        Assert.That(again, Is.SameAs(first));
        Assert.That(changed, Is.Not.SameAs(first));
    }
}
