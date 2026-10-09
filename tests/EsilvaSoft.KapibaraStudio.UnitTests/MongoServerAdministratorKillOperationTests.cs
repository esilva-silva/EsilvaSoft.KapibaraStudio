using EsilvaSoft.KapibaraStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoServerAdministratorKillOperationTests
{
    [Test]
    public void KillOperationCommandUsesProtocolEnvelopeAndPositiveOperationId()
    {
        var command = MongoServerAdministrator.BuildKillOperationCommand(42);

        Assert.Multiple(() =>
        {
            Assert.That(command, Is.EqualTo(new BsonDocument { ["killOp"] = 1, ["op"] = 42 }));
            Assert.That(command["killOp"].AsInt32, Is.EqualTo(1));
            Assert.That(command["op"].AsInt64, Is.EqualTo(42));
            Assert.That(command.ElementCount, Is.EqualTo(2));
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void KillOperationCommandRejectsNonPositiveOperationId(long operationId)
    {
        Assert.That(() => MongoServerAdministrator.BuildKillOperationCommand(operationId),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
