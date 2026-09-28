using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class MongoAgentIndexSourceTests
{
    private static readonly string[] ActiveKeyFields = ["active"];
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
            ["key"] = new BsonDocument("active", 1),
            ["unique"] = true,
            ["partialFilterExpression"] = new BsonDocument("tenantSecret", "private-canary"),
            ["wildcardProjection"] = new BsonDocument("privateField", "private-canary")
        };

        var projected = MongoAgentIndexSource.Project(raw);
        var json = System.Text.Json.JsonSerializer.Serialize(projected);

        Assert.Multiple(() =>
        {
            Assert.That(projected.Name, Is.EqualTo("active_1"));
            Assert.That(projected.KeyFields, Is.EqualTo(ActiveKeyFields));
            Assert.That(projected.Unique, Is.True);
            Assert.That(json, Does.Not.Contain("private-canary"));
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
