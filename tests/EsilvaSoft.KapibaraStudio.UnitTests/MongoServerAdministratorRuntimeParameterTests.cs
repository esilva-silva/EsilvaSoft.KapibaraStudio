using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoServerAdministratorRuntimeParameterTests
{
    [TestCase("logLevel", 1)]
    [TestCase("maxLogSizeKB", 1)]
    public void GetParameterRequestIsExplicitAndIncludesRuntimeDetails(string name, int marker)
    {
        var command = MongoServerAdministrator.BuildGetRuntimeParameterCommand(name);

        Assert.Multiple(() =>
        {
            Assert.That(command[name].AsInt32, Is.EqualTo(marker));
            Assert.That(command["getParameter"].AsBsonDocument["showDetails"].AsBoolean, Is.True);
            Assert.That(command.ElementCount, Is.EqualTo(2));
        });
    }

    [TestCase("logLevel", 3)]
    [TestCase("maxLogSizeKB", 8)]
    public void SetParameterRequestContainsOnlyAllowlistedValue(string name, int value)
    {
        var command = MongoServerAdministrator.BuildSetRuntimeParameterCommand(
            new RuntimeServerParameterRequest(name, value, value, name));

        Assert.Multiple(() =>
        {
            Assert.That(command["setParameter"].AsInt32, Is.EqualTo(1));
            Assert.That(command[name].AsInt32, Is.EqualTo(value));
            Assert.That(command.ElementCount, Is.EqualTo(2));
        });
    }

    [Test]
    public void GetParameterRejectsUnknownNameBeforeCommandConstruction()
    {
        Assert.That(() => MongoServerAdministrator.BuildGetRuntimeParameterCommand("startupParameter"),
            Throws.TypeOf<ArgumentException>());
    }

    [TestCase("logLevel", 5)]
    [TestCase("maxLogSizeKB", 10)]
    public void ReadbackRequiresRuntimeSupportAndReturnsTheTypedValue(string name, int value)
    {
        var response = new BsonDocument(name, new BsonDocument
        {
            ["settableAtRuntime"] = true,
            ["value"] = value
        });

        Assert.That(MongoServerAdministrator.ParseRuntimeParameterValue(name, response), Is.EqualTo(value));
    }

    [Test]
    public void ReadbackRejectsParameterThatIsNotRuntimeSettable()
    {
        var response = new BsonDocument("maxLogSizeKB", new BsonDocument
        {
            ["settableAtRuntime"] = false,
            ["value"] = 10
        });

        Assert.That(() => MongoServerAdministrator.ParseRuntimeParameterValue("maxLogSizeKB", response),
            Throws.TypeOf<RuntimeServerParameterException>()
                .With.Property(nameof(RuntimeServerParameterException.Kind)).EqualTo(RuntimeServerParameterFailureKind.Unsupported));
    }

    [Test]
    public void SetParameterRejectsValueOutsideConservativeAllowlistRange()
    {
        Assert.That(() => MongoServerAdministrator.BuildSetRuntimeParameterCommand(
                new RuntimeServerParameterRequest("maxLogSizeKB", 10, 128, "maxLogSizeKB")),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
