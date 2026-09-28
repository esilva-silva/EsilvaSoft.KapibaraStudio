using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoQueryTests
{
    [TestCase(0)]
    [TestCase(1001)]
    public void ValidateWithLimitOutsideSupportedRangeThrows(int limit)
    {
        var query = new MongoQuery("catalogo", "clientes", Limit: limit);

        Assert.That(() => query.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ValidateWithRequiredPartsReturnsSameQuery()
    {
        var query = new MongoQuery("catalogo", "clientes");

        Assert.That(query.Validate(), Is.SameAs(query));
    }

    [Test]
    public void QueryKeepsOptionalProjectionAndSort()
    {
        var query = new MongoQuery(
            "catalogo",
            "clientes",
            "{ \"ativo\": true }",
            "{ \"nome\": 1 }",
            "{ \"criadoEm\": -1 }");

        Assert.Multiple(() =>
        {
            Assert.That(query.ProjectionJson, Is.EqualTo("{ \"nome\": 1 }"));
            Assert.That(query.SortJson, Is.EqualTo("{ \"criadoEm\": -1 }"));
        });
    }

    [Test]
    public void QueryKeepsSkipAndHint()
    {
        var query = new MongoQuery(
            "catalogo",
            "clientes",
            Skip: 10,
            HintJson: "{ \"criadoEm\": -1 }",
            MaxTimeMs: 5000);

        Assert.Multiple(() =>
        {
            Assert.That(query.Skip, Is.EqualTo(10));
            Assert.That(query.HintJson, Is.EqualTo("{ \"criadoEm\": -1 }"));
            Assert.That(query.MaxTimeMs, Is.EqualTo(5000));
        });
    }

    [TestCase(-1)]
    [TestCase(1_000_001)]
    public void ValidateWithSkipOutsideSupportedRangeThrows(int skip)
    {
        var query = new MongoQuery("catalogo", "clientes", Skip: skip);

        Assert.That(() => query.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [TestCase(0)]
    [TestCase(600_001)]
    public void ValidateWithMaxTimeOutsideSupportedRangeThrows(int maxTimeMs)
    {
        var query = new MongoQuery("catalogo", "clientes", MaxTimeMs: maxTimeMs);

        Assert.That(() => query.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void QueryKeepsOperationComment()
    {
        var query = new MongoQuery("catalogo", "clientes", Comment: "investigação de duplicidade");

        Assert.That(query.Validate().Comment, Is.EqualTo("investigação de duplicidade"));
    }

    [Test]
    public void ValidateRejectsCommentLongerThan512Characters()
    {
        var query = new MongoQuery("catalogo", "clientes", Comment: new string('x', 513));

        Assert.That(() => query.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void QueryKeepsBatchSize()
    {
        var query = new MongoQuery("catalogo", "clientes", BatchSize: 250);

        Assert.That(query.Validate().BatchSize, Is.EqualTo(250));
    }

    [TestCase(0)]
    [TestCase(10_001)]
    public void ValidateRejectsBatchSizeOutsideSupportedRange(int batchSize)
    {
        var query = new MongoQuery("catalogo", "clientes", BatchSize: batchSize);

        Assert.That(() => query.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void QueryAcceptsCollationDocument()
    {
        var query = new MongoQuery("catalogo", "clientes", CollationJson: "{ \"locale\": \"pt\", \"strength\": 1 }");

        Assert.That(query.Validate(), Is.SameAs(query));
    }

    [TestCase("[]")]
    [TestCase("{")]
    public void QueryRejectsInvalidCollation(string collation)
    {
        var query = new MongoQuery("catalogo", "clientes", CollationJson: collation);

        Assert.That(() => query.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
