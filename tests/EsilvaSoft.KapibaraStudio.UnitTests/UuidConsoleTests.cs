using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Jint;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class UuidConsoleTests
{
    private const string Sample = "00112233-4455-6677-8899-aabbccddeeff";

    [Test]
    public async Task ConsoleConstructorsSendExplicitRepresentationsAndStringsStayStrings()
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        await context.Repository.SaveAsync(profile);
        string? filter = null;
        context.Mongo.Handler = (_, args) => { filter = ((MongoQuery)args[1]!).FilterJson; return Task.FromResult(new QueryPage([], TimeSpan.Zero, false)); };
        var result = await CreateRuntime(context).ExecuteAsync(new(profile, "db", $$"""
            db.Customers.find({ s: UUID("{{Sample}}"), c: CGUUID("{{Sample}}"), j: JUUID("{{Sample}}"), g: GUUID("{{Sample.ToUpperInvariant()}}"), text: 'CGUUID("{{Sample}}")' });
            typeof __hostUuid + "|" + typeof __hostUuidJson;
            EJSON.parse('{"v": JUUID("{{Sample}}"), "t": "UUID(\\"x\\")"}');
            """, SaveHistory: false), (_, _) => Task.FromResult(false));
        Assert.That(result.Error, Is.Null, result.Error);
        var sent = BsonDocument.Parse(filter!);
        var parsed = BsonDocument.Parse(result.Results[2].Json);
        Assert.Multiple(() =>
        {
            AssertBinary(sent["s"], "00112233445566778899aabbccddeeff", BsonBinarySubType.UuidStandard);
            AssertBinary(sent["c"], "33221100554477668899aabbccddeeff", BsonBinarySubType.UuidLegacy);
            AssertBinary(sent["j"], "7766554433221100ffeeddccbbaa9988", BsonBinarySubType.UuidLegacy);
            AssertBinary(sent["g"], "00112233445566778899aabbccddeeff", BsonBinarySubType.UuidStandard);
            Assert.That(sent["text"].AsString, Is.EqualTo($"CGUUID(\"{Sample}\")"));
            Assert.That(result.Results[1].Json, Is.EqualTo("\"undefined|undefined\""));
            AssertBinary(parsed["v"], "7766554433221100ffeeddccbbaa9988", BsonBinarySubType.UuidLegacy);
            Assert.That(parsed["t"].AsString, Is.EqualTo("UUID(\"x\")"));
        });
    }

    [TestCase("db.Customers.find({ c: UUID() })", "UUID(...) exige um texto")]
    [TestCase("db.Customers.find({ c: CGUUID(42) })", "CGUUID(...) exige um texto")]
    [TestCase("db.Customers.find({ c: JUUID('xyz') })", "JUUID(\"xyz\") não contém um UUID válido")]
    [TestCase("EJSON.parse('{\"v\": GUUID(\"bad\")}')", "GUUID(\"bad\") não contém um UUID válido")]
    public async Task InvalidConsoleUuidNeverReachesTheDatabase(string script, string message)
    {
        using var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        await context.Repository.SaveAsync(profile);
        var calls = 0;
        context.Mongo.Handler = (_, _) => { calls++; return Task.FromResult(new QueryPage([], TimeSpan.Zero, false)); };
        var result = await CreateRuntime(context).ExecuteAsync(new(profile, "db", script, SaveHistory: false), (_, _) => Task.FromResult(false));
        Assert.That(result.Error, Does.Contain(message));
        Assert.That(calls, Is.Zero);
    }

    private static ConsoleRuntime CreateRuntime(WorkspaceTestContext context) =>
        new(context.Repository, context.Repository, new SessionConnectionSecretStore(), new WorkspaceConsoleSession((IMongoWorkspaceService)context.Mongo), context.Repository, context.Repository);

    private static void AssertBinary(BsonValue value, string hex, BsonBinarySubType subType)
    {
        Assert.That(Convert.ToHexStringLower(value.AsBsonBinaryData.Bytes), Is.EqualTo(hex));
        Assert.That(value.AsBsonBinaryData.SubType, Is.EqualTo(subType));
    }

}
