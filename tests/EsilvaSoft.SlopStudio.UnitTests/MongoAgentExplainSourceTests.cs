using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class MongoAgentExplainSourceTests
{
    [Test]
    public void CommandUsesOnlyQueryPlannerWithBoundedFindOptions()
    {
        var query = new AgentMongoFindQuery("db", "items", "{}", null, null, 20, 3, 1200);
        var filter = new BsonDocument("tenant", "private-canary");
        var command = MongoAgentExplainSource.BuildExplainCommand(query, filter,
            new BsonDocument("a", 1), new BsonDocument("a", -1));
        var find = command["explain"].AsBsonDocument;

        Assert.Multiple(() =>
        {
            Assert.That(command["verbosity"].AsString, Is.EqualTo("queryPlanner"));
            Assert.That(command["maxTimeMS"].AsInt32, Is.EqualTo(1200));
            Assert.That(find["maxTimeMS"].AsInt32, Is.EqualTo(1200));
            Assert.That(find["limit"].AsInt32, Is.EqualTo(20));
            Assert.That(find["skip"].AsInt32, Is.EqualTo(3));
            Assert.That(find["filter"].AsBsonDocument["tenant"].AsString, Is.EqualTo("private-canary"));
            Assert.That(command.ToJson(), Does.Not.Contain("executionStats"));
        });
    }

    [Test]
    public void SanitizerDropsQueryValuesAndServerMetadata()
    {
        var raw = new BsonDocument
        {
            ["stage"] = "FETCH",
            ["filter"] = new BsonDocument("secret", "private-canary"),
            ["inputStage"] = new BsonDocument
            {
                ["stage"] = "IXSCAN",
                ["indexName"] = "a_1",
                ["indexBounds"] = new BsonDocument("a", "private-canary")
            },
            ["serverInfo"] = new BsonDocument("host", "private-host")
        };
        var nodes = 0;
        var sanitized = MongoAgentExplainSource.SanitizePlan(raw, ref nodes).ToJson();

        Assert.Multiple(() =>
        {
            Assert.That(sanitized, Does.Contain("IXSCAN"));
            Assert.That(sanitized, Does.Not.Contain("private-canary"));
            Assert.That(sanitized, Does.Not.Contain("private-host"));
            Assert.That(sanitized, Does.Not.Contain("indexBounds"));
        });
    }

    [Test]
    public void UnsupportedWinningPlanIsADataFailureWithFixedMessage()
    {
        var unsupported = new BsonDocument("private-canary", "secret");
        var exception = Assert.Throws<InvalidDataException>(() =>
            MongoAgentExplainSource.SanitizeServerPlan(unsupported));

        Assert.That(exception!.Message, Is.EqualTo("Unsupported query plan returned by the server."));
        Assert.That(exception.Message, Does.Not.Contain("private-canary"));
    }
}
