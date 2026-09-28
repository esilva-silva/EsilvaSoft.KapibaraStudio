using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Context;

[TestFixture]
public sealed class NamespaceTargetResolverTests
{
    private static readonly NamespaceTarget Tab = new("Default", "app", null, NamespaceTargetConfidence.TabDefault);

    [TestCase("db.orders", "app", "orders")]
    [TestCase("db[\"orders\"]", "app", "orders")]
    [TestCase("db.getCollection(\"orders\").find({", "app", "orders")]
    public void ResolvesStaticDbCollectionChains(string source, string database, string collection)
    {
        var target = Resolve(source);
        Assert.That(target.Confidence, Is.EqualTo(NamespaceTargetConfidence.Explicit));
        Assert.That(target.Database, Is.EqualTo(database));
        Assert.That(target.Collection, Is.EqualTo(collection));
    }

    [Test]
    public void ResolvesNamedConnectionAndDatabaseChain()
    {
        var target = Resolve("getConnection(\"analytics\").getDatabase(\"audit\").getCollection(\"events\").find({");
        Assert.That(target, Is.EqualTo(new NamespaceTarget("analytics", "audit", "events", NamespaceTargetConfidence.Explicit)));
    }

    [Test]
    public void ResolvesConnectionPoolGetChain()
    {
        var target = Resolve("ConnectionPool.get(\"analytics\").getDatabase(\"audit\").getCollection(\"events\")");
        Assert.That(target, Is.EqualTo(new NamespaceTarget("analytics", "audit", "events", NamespaceTargetConfidence.Explicit)));
    }

    [Test]
    public void UsesUniqueEarlierConstAliasWithInferredConfidence()
    {
        var target = Resolve("const orders = db.orders;\norders.find({");
        Assert.That(target.Database, Is.EqualTo("app"));
        Assert.That(target.Collection, Is.EqualTo("orders"));
        Assert.That(target.Confidence, Is.EqualTo(NamespaceTargetConfidence.Inferred));
    }

    [Test]
    public void DynamicCollectionAndOpaqueInputDoNotInheritAnotherStatementTarget()
    {
        Assert.That(Resolve("db.getCollection(name).find({"), Is.EqualTo(NamespaceTarget.Unknown));
        Assert.That(Resolve("db.orders; db.getCollection(name).find({"), Is.EqualTo(NamespaceTarget.Unknown));
    }

    [Test]
    public void AggregationUsesTheCapturedTabTargetOnly()
    {
        var target = NamespaceTargetResolver.Resolve("{ $match: {} }", 14, Tab with { Collection = "orders" }, EditorDialects.AggregationJson);
        Assert.That(target.Database, Is.EqualTo("app"));
        Assert.That(target.Collection, Is.EqualTo("orders"));
        Assert.That(target.Confidence, Is.EqualTo(NamespaceTargetConfidence.TabDefault));
    }

    private static NamespaceTarget Resolve(string source) => NamespaceTargetResolver.Resolve(source, source.Length, Tab);
}
