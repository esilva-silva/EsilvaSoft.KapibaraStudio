using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoAgentIndexSourceTests
{
    private static readonly string[] ExpectedKeyFields = ["active", "createdAt"];
    private static readonly string[] ExpectedKeyDirections = ["1", "-1"];
    private static readonly string[] ExpectedPartialFilterFields = ["tenantSecret", "status", "region"];

    [Test]
    public void ListOptionsUseSingleItemBatchAndBoundedTimeoutWhenSupported()
    {
        var maximum = TimeSpan.FromSeconds(7);
        var options = MongoAgentIndexSource.CreateListOptions(maximum);

        Assert.That(options.BatchSize, Is.EqualTo(1));
        var timeout = options.GetType().GetProperty("Timeout");
        if (timeout is not null)
            Assert.That(timeout.GetValue(options), Is.EqualTo(maximum));
        Assert.That(() => MongoAgentIndexSource.CreateListOptions(TimeSpan.FromSeconds(31)),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
    [Test]
    public void ProjectExcludesPartialFilterAndOtherBsonValues()
    {
        var raw = new BsonDocument
        {
            ["name"] = "active_1",
            ["key"] = new BsonDocument("active", 1).Add("createdAt", -1),
            ["unique"] = true,
            ["expireAfterSeconds"] = 3600,
            ["partialFilterExpression"] = new BsonDocument
            {
                ["tenantSecret"] = "private-canary",
                ["$and"] = new BsonArray
                {
                    new BsonDocument("status", "enabled-canary"),
                    new BsonDocument("region", new BsonDocument("$in", new BsonArray { "private-canary" }))
                }
            },
            ["wildcardProjection"] = new BsonDocument("privateField", "private-canary")
        };

        var projected = MongoAgentIndexSource.Project(raw);
        var json = System.Text.Json.JsonSerializer.Serialize(projected);

        Assert.Multiple(() =>
        {
            Assert.That(projected.Name, Is.EqualTo("active_1"));
            Assert.That(projected.KeyFields, Is.EqualTo(ExpectedKeyFields));
            Assert.That(projected.KeyDirections, Is.EqualTo(ExpectedKeyDirections));
            Assert.That(projected.Unique, Is.True);
            Assert.That(projected.TtlSeconds, Is.EqualTo(3600));
            Assert.That(projected.PartialFilterFields, Is.EqualTo(ExpectedPartialFilterFields));
            Assert.That(json, Does.Not.Contain("private-canary"));
            Assert.That(json, Does.Not.Contain("enabled-canary"));
            Assert.That(json, Does.Not.Contain("partialFilterExpression"));
            Assert.That(json, Does.Not.Contain("wildcardProjection"));
        });
    }

    [Test]
    public void ProjectBoundedRejectsOversizedRawDefinitionBeforeProjection()
    {
        var raw = new BsonDocument
        {
            ["name"] = "partial_1",
            ["key"] = new BsonDocument("a", 1),
            ["partialFilterExpression"] = new BsonDocument("private", new string('x', 65 * 1024))
        };

        Assert.That(() => MongoAgentIndexSource.ProjectBounded(raw, 256 * 1024, out _),
            Throws.TypeOf<FormatException>());
    }

    [TestCase(9_007_199_254_740_993L)]
    [TestCase(long.MaxValue)]
    public void ProjectPreservesExactInt64TtlWithoutFloatingPointConversion(long seconds)
    {
        var raw = new BsonDocument
        {
            ["name"] = "ttl_1", ["key"] = new BsonDocument("createdAt", 1),
            ["expireAfterSeconds"] = new BsonInt64(seconds)
        };

        var projected = MongoAgentIndexSource.ProjectBounded(raw, 256 * 1024, out _);

        Assert.That(projected.TtlSeconds, Is.EqualTo(seconds));
    }

    [TestCase(32, false)]
    [TestCase(33, true)]
    public void PartialFilterFieldLimitReportsTruncationWithoutValues(int count, bool expectedTruncated)
    {
        var filter = new BsonDocument();
        for (var number = 0; number < count; number++)
            filter.Add("field" + number, "private-filter-value-canary");
        var raw = new BsonDocument
        {
            ["name"] = "partial_1", ["key"] = new BsonDocument("a", 1),
            ["partialFilterExpression"] = filter
        };

        var projected = MongoAgentIndexSource.ProjectBounded(raw, 256 * 1024, out _, out var truncated);

        Assert.Multiple(() =>
        {
            Assert.That(projected.PartialFilterFields, Has.Count.EqualTo(Math.Min(count, 32)));
            Assert.That(truncated, Is.EqualTo(expectedTruncated));
            Assert.That(System.Text.Json.JsonSerializer.Serialize(projected), Does.Not.Contain("private-filter-value-canary"));
        });
    }

    [TestCase(4, false)]
    [TestCase(5, true)]
    public void PartialFilterDepthLimitReportsTruncation(int depth, bool expectedTruncated)
    {
        var filter = new BsonDocument("deepField", "private-filter-value-canary");
        for (var number = 0; number < depth; number++)
            filter = new BsonDocument("$and", new BsonArray { filter });
        var raw = new BsonDocument
        {
            ["name"] = "partial_1", ["key"] = new BsonDocument("a", 1),
            ["partialFilterExpression"] = filter
        };

        var projected = MongoAgentIndexSource.ProjectBounded(raw, 256 * 1024, out _, out var truncated);

        Assert.Multiple(() =>
        {
            Assert.That(projected.PartialFilterFields, Has.Count.EqualTo(expectedTruncated ? 0 : 1));
            Assert.That(truncated, Is.EqualTo(expectedTruncated));
        });
    }

    [Test]
    public void ProjectBoundedRejectsExcessiveFieldsAndTotalByteBudget()
    {
        var tooManyFields = new BsonDocument { ["name"] = "idx", ["key"] = new BsonDocument("a", 1) };
        for (var number = 0; number < 32; number++) tooManyFields["option" + number] = true;
        var ordinary = new BsonDocument { ["name"] = "idx", ["key"] = new BsonDocument("a", 1) };

        Assert.Multiple(() =>
        {
            Assert.That(() => MongoAgentIndexSource.ProjectBounded(tooManyFields, 256 * 1024, out _),
                Throws.TypeOf<FormatException>());
            Assert.That(() => MongoAgentIndexSource.ProjectBounded(ordinary, 1, out _),
                Throws.TypeOf<FormatException>());
        });
    }
}
