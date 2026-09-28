using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class MongoAgentFindSourceTests
{
    [Test]
    public void LiteralParserPreservesCanonicalBsonAndEnvironmentText()
    {
        const string ejson = """
            {"_id":{"$oid":"507f1f77bcf86cd799439011"},"long":{"$numberLong":"9007199254740993"},"decimal":{"$numberDecimal":"123.450"},"date":{"$date":{"$numberLong":"1700000000000"}},"uuid":{"$binary":{"base64":"AAAAAAAAAAAAAAAAAAAAAA==","subType":"04"}},"legacy":{"$binary":{"base64":"AAAAAAAAAAAAAAAAAAAAAA==","subType":"03"}},"literal":"ENV.PRIVATE_CANARY"}
            """;

        var parsed = MongoAgentFindSource.ParseLiteral(ejson);

        Assert.Multiple(() =>
        {
            Assert.That(parsed["_id"].BsonType, Is.EqualTo(BsonType.ObjectId));
            Assert.That(parsed["long"].AsInt64, Is.EqualTo(9_007_199_254_740_993L));
            Assert.That(parsed["decimal"].BsonType, Is.EqualTo(BsonType.Decimal128));
            Assert.That(parsed["date"].BsonType, Is.EqualTo(BsonType.DateTime));
            Assert.That(parsed["uuid"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
            Assert.That(parsed["legacy"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidLegacy));
            Assert.That(parsed["literal"].AsString, Is.EqualTo("ENV.PRIVATE_CANARY"));
        });
    }

    [Test]
    public void LiteralParserRejectsConstructorsCodeAndDuplicateProperties()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => MongoAgentFindSource.ParseLiteral("ObjectId('507f1f77bcf86cd799439011')"),
                Throws.Exception);
            Assert.That(() => MongoAgentFindSource.ParseLiteral("{\"$code\":\"return true\"}"),
                Throws.Exception);
            Assert.That(() => MongoAgentFindSource.ParseLiteral("{\"a\":1,\"a\":2}"),
                Throws.Exception);
        });
    }

    [Test]
    public void CountSerializerUsesCanonicalInt64IncludingBeyondJavaScriptPrecision()
    {
        var json = MongoAgentFindSource.CanonicalCountEjson(9_007_199_254_740_993L);
        Assert.That(json, Is.EqualTo("{\"$numberLong\":\"9007199254740993\"}"));
        var parsed = MongoAgentFindSource.ParseLiteral("{\"count\":" + json + "}");
        Assert.That(parsed["count"].BsonType, Is.EqualTo(BsonType.Int64));
        Assert.That(parsed["count"].AsInt64, Is.EqualTo(9_007_199_254_740_993L));
    }

    [Test]
    public void IdParserPreservesBsonTypesAndBuildsLiteralEqualityFilter()
    {
        var cases = new (string Ejson, BsonType Type)[]
        {
            ("{\"$oid\":\"507f1f77bcf86cd799439011\"}", BsonType.ObjectId),
            ("\"ENV.PRIVATE_CANARY\"", BsonType.String),
            ("{\"$numberLong\":\"9007199254740993\"}", BsonType.Int64),
            ("{\"nested\":{\"$numberInt\":\"7\"}}", BsonType.Document),
            ("{\"$binary\":{\"base64\":\"AAAAAAAAAAAAAAAAAAAAAA==\",\"subType\":\"04\"}}", BsonType.Binary)
        };
        foreach (var (ejson, type) in cases)
        {
            var value = MongoAgentFindSource.ParseLiteralValue(ejson);
            var filter = MongoAgentFindSource.CreateIdFilter(value);
            Assert.Multiple(() =>
            {
                Assert.That(value.BsonType, Is.EqualTo(type), ejson);
                Assert.That(filter["_id"].AsBsonDocument["$eq"], Is.EqualTo(value), ejson);
            });
        }
        Assert.That(MongoAgentFindSource.ParseLiteralValue("\"ENV.PRIVATE_CANARY\"").AsString,
            Is.EqualTo("ENV.PRIVATE_CANARY"));
        Assert.That(MongoAgentFindSource.ParseLiteralValue("{\"$numberLong\":\"9007199254740993\"}").AsInt64,
            Is.EqualTo(9_007_199_254_740_993L));
        Assert.That(MongoAgentFindSource.ParseLiteralValue(
            "{\"$binary\":{\"base64\":\"AAAAAAAAAAAAAAAAAAAAAA==\",\"subType\":\"04\"}}")
            .AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
    }

    [Test]
    public void IdParserRejectsCodeConstructorsAndDuplicateProperties()
    {
        foreach (var ejson in new[]
                 { "ObjectId('507f1f77bcf86cd799439011')", "{\"$where\":\"true\"}",
                   "{\"$code\":\"return true\"}", "{\"a\":1,\"a\":2}" })
            Assert.That(() => MongoAgentFindSource.ParseLiteralValue(ejson), Throws.Exception, ejson);

        var operatorShapedId = MongoAgentFindSource.ParseLiteralValue("{\"$ne\":null}");
        var filter = MongoAgentFindSource.CreateIdFilter(operatorShapedId);
        Assert.That(filter["_id"].AsBsonDocument.ElementCount, Is.EqualTo(1));
        Assert.That(filter["_id"]["$eq"].AsBsonDocument.Contains("$ne"), Is.True);
    }

    [Test]
    public void DistinctPipelineKeepsLiteralFilterAndBoundsServerWork()
    {
        var filter = MongoAgentFindSource.ParseLiteral("{\"literal\":\"ENV.PRIVATE_CANARY\"}");
        var query = new EsilvaSoft.SlopStudio.Application.Agents.AgentMongoDistinctQuery(
            "allowed", "visible", "nested.value", "{\"literal\":\"ENV.PRIVATE_CANARY\"}", 20, 5_000);

        var stages = MongoAgentFindSource.BuildDistinctPipeline(query, filter);

        Assert.Multiple(() =>
        {
            Assert.That(stages, Has.Length.EqualTo(3));
            Assert.That(stages[0]["$match"]["literal"].AsString, Is.EqualTo("ENV.PRIVATE_CANARY"));
            Assert.That(stages[1]["$group"]["_id"].AsString, Is.EqualTo("$nested.value"));
            Assert.That(stages[2]["$limit"].AsInt32, Is.EqualTo(21));
        });
        foreach (var field in new[] { "", "$where", "a..b", ".a", "a.", "a\n", "a.$ne" })
            Assert.That(MongoAgentFindSource.IsSafeDistinctField(field), Is.False, field);
    }

    [Test]
    public void DistinctValuesKeepCanonicalBsonTypes()
    {
        var values = new BsonValue[]
        {
            new BsonInt64(9_007_199_254_740_993L),
            BsonDecimal128.Create("123.45"),
            new BsonDateTime(1_700_000_000_000L),
            new BsonObjectId(ObjectId.Parse("507f1f77bcf86cd799439011")),
            new BsonBinaryData(new byte[16], BsonBinarySubType.UuidStandard)
        };
        foreach (var value in values)
        {
            var ejson = value.ToJson(new MongoDB.Bson.IO.JsonWriterSettings
            {
                OutputMode = MongoDB.Bson.IO.JsonOutputMode.CanonicalExtendedJson
            });
            var parsed = MongoAgentFindSource.ParseLiteral("{\"value\":" + ejson + "}")["value"];
            Assert.That(parsed.BsonType, Is.EqualTo(value.BsonType), ejson);
            Assert.That(parsed, Is.EqualTo(value), ejson);
        }
    }
}
