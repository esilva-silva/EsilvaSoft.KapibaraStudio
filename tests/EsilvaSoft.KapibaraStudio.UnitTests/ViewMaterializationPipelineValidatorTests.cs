using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ViewMaterializationPipelineValidatorTests
{
    [Test]
    public void AllowsBoundedReadStages()
    {
        var stages = new BsonArray
        {
            new BsonDocument("$match", new BsonDocument("active", true)),
            new BsonDocument("$project", new BsonDocument("name", 1))
        };

        Assert.DoesNotThrow(() => ViewMaterializationPipelineValidator.Validate(stages));
    }

    [TestCase("$out")]
    [TestCase("$merge")]
    [TestCase("$lookup")]
    [TestCase("$unionWith")]
    [TestCase("$currentOp")]
    public void RejectsWriteAndCrossNamespaceStages(string stage)
    {
        var stages = new BsonArray { new BsonDocument(stage, "other") };

        Assert.That(() => ViewMaterializationPipelineValidator.Validate(stages), Throws.Exception);
    }

    [Test]
    public void DoesNotMistakeLiteralFieldNamedOutForWriteStage()
    {
        var stages = new BsonArray
        {
            new BsonDocument("$project", new BsonDocument("$out", new BsonDocument("$literal", "safe")))
        };

        Assert.DoesNotThrow(() => ViewMaterializationPipelineValidator.Validate(stages));
    }

    [Test]
    public void RejectsPipelineBeyondBudget()
    {
        var stages = new BsonArray(Enumerable.Range(0, 21)
            .Select(_ => new BsonDocument("$match", new BsonDocument())));

        Assert.That(() => ViewMaterializationPipelineValidator.Validate(stages), Throws.TypeOf<ArgumentException>());
    }
}
