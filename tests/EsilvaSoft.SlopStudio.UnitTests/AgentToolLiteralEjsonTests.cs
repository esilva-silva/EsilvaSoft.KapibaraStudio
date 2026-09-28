using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class AgentToolLiteralEjsonTests
{
    private static readonly string[] NestedCollections = ["archivedOrders", "customers", "employees", "orders"];

    [TestCase("{}")]
    [TestCase("{\"name\":\"ENV.PRIVATE_CANARY\",\"template\":\"${ENV.MONGO_PASSWORD}\"}")]
    [TestCase("{\"big\":{\"$numberLong\":\"9007199254740993\"},\"dec\":{\"$numberDecimal\":\"1.10\"}}")]
    [TestCase("{\"_id\":{\"$uuid\":\"00112233-4455-6677-8899-aabbccddeeff\"}}")]
    [TestCase("{\"u\":{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\"}}}")]
    [TestCase("{\"d\":{\"$date\":{\"$numberLong\":\"1700000000000\"}},\"o\":{\"$oid\":\"507f1f77bcf86cd799439011\"}}")]
    [TestCase("{\"age\":{\"$gte\":18,\"$lt\":{\"$numberLong\":\"65\"}},\"tags\":{\"$in\":[\"a\",\"b\"]}}")]
    [TestCase("{\"$or\":[{\"a\":1},{\"b\":{\"$exists\":false}}],\"c\":{\"$not\":{\"$regex\":\"^x\",\"$options\":\"i\"}}}")]
    [TestCase("{\"items\":{\"$elemMatch\":{\"qty\":{\"$gt\":2},\"$or\":[{\"sku\":\"a\"},{\"sku\":\"b\"}]}}}")]
    [TestCase("{\"scores\":{\"$all\":[{\"$elemMatch\":{\"$gt\":5}}]},\"n\":{\"$type\":[\"int\",\"long\"]}}")]
    [TestCase("{\"$expr\":{\"$gt\":[\"$spent\",{\"$multiply\":[\"$budget\",2]}]}}")]
    [TestCase("{\"$expr\":{\"$anyElementTrue\":{\"$map\":{\"input\":\"$a\",\"as\":\"item\",\"in\":{\"$eq\":[\"$$item.x\",\"$$ROOT.y\"]}}}}}")]
    [TestCase("{\"loc\":{\"$geoWithin\":{\"$centerSphere\":[[0,0],0.1]}}}")]
    [TestCase("{\"flags\":{\"$bitsAllSet\":[1,5]},\"$comment\":\"agent\"}")]
    public void AcceptsClosedLiteralQueryFilters(string filter) =>
        Assert.That(AgentToolLiteralEjson.IsQueryFilter(filter), Is.True, filter);

    [TestCase("{\"$where\":\"this.a == 1\"}")]
    [TestCase("{\"\\u0024where\":\"true\"}")]
    [TestCase("{\"$and\":[{\"a\":1},{\"$where\":\"true\"}]}")]
    [TestCase("{\"items\":{\"$elemMatch\":{\"$where\":\"true\"}}}")]
    [TestCase("{\"a\":{\"$not\":{\"$where\":\"true\"}}}")]
    [TestCase("{\"$expr\":{\"$function\":{\"body\":\"return 1\",\"args\":[],\"lang\":\"js\"}}}")]
    [TestCase("{\"$expr\":{\"$eq\":[\"$$USER_ROLES\",[]]}}")]
    [TestCase("{\"$expr\":{\"$eq\":[\"$$CLUSTER_TIME\",1]}}")]
    [TestCase("{\"a\":{\"$code\":\"return 1\"}}")]
    [TestCase("{\"a\":{\"$eq\":{\"$code\":\"return 1\"}}}")]
    [TestCase("{\"a\":{\"$eq\":{\"nested\":{\"$where\":\"true\"}}}}")]
    [TestCase("{\"a\":{\"$dbPointer\":{\"$ref\":\"c\",\"$id\":{\"$oid\":\"507f1f77bcf86cd799439011\"}}}}")]
    [TestCase("{\"a\":{\"$symbol\":\"x\"}}")]
    [TestCase("{\"a\":{\"$gt\":1,\"b\":2}}")]
    [TestCase("{\"a\":{\"$unknownOperator\":1}}")]
    [TestCase("{\"$env\":\"MONGO_PASSWORD\"}")]
    [TestCase("{\"$jsonSchema\":{\"properties\":{\"a\":{\"$where\":\"true\"}}}}")]
    [TestCase("{\"o\":{\"$oid\":\"not-an-object-id\"}}")]
    [TestCase("{\"o\":{\"$oid\":\"507f1f77bcf86cd799439011\",\"extra\":1}}")]
    [TestCase("{\"a\":1,\"a\":2}")]
    [TestCase("{\"a\":{\"b\":1,\"b\":2}}")]
    [TestCase("{\"a\":\"\\ud800\"}")]
    [TestCase("{\"a\\u0000b\":1}")]
    [TestCase("{\"a\":ObjectId(\"507f1f77bcf86cd799439011\")}")]
    [TestCase("{\"a\":NumberLong(1)}")]
    [TestCase("{a:1}")]
    [TestCase("{\"a\":1,}")]
    [TestCase("{\"a\":1 /* comment */}")]
    [TestCase("[]")]
    [TestCase("\"text\"")]
    [TestCase("")]
    public void RejectsCodeEnvironmentConstructorsAndUnknownOperatorsAtAnyDepth(string filter)
    {
        Assert.That(AgentToolLiteralEjson.IsQueryFilter(filter), Is.False, filter);
        // The handler re-validates with the same codec before BSON parsing.
        Assert.That(() => MongoAgentFindSource.ParseLiteral(filter), Throws.Exception, filter);
    }

    [Test]
    public void RejectsOversizedAndOverlyDeepInput()
    {
        var oversized = "{\"a\":\"" + new string('x', AgentToolLiteralEjson.MaximumInputBytes) + "\"}";
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 70)) + "1" + new string('}', 70);
        Assert.Multiple(() =>
        {
            Assert.That(AgentToolLiteralEjson.IsQueryFilter(oversized), Is.False);
            Assert.That(AgentToolLiteralEjson.IsQueryFilter(deep), Is.False);
            Assert.That(AgentToolLiteralEjson.IsQueryFilter("{\"a\":\"\u00e9\"}", maximumBytes: 9), Is.False);
        });
    }

    [Test]
    public void AcceptedLiteralsParseWithBsonTypesPreserved()
    {
        const string filter = """
            {"big":{"$numberLong":"9007199254740993"},"uuid":{"$uuid":"00112233-4455-6677-8899-aabbccddeeff"},"legacy":{"$binary":{"base64":"AAAAAAAAAAAAAAAAAAAAAA==","subType":"03"}},"dec":{"$numberDecimal":"1.10"},"int":{"$numberInt":"7"},"env":"ENV.get('PRIVATE_CANARY')"}
            """;

        Assert.That(AgentToolLiteralEjson.IsQueryFilter(filter), Is.True);
        var parsed = MongoAgentFindSource.ParseLiteral(filter);

        Assert.Multiple(() =>
        {
            Assert.That(parsed["big"].BsonType, Is.EqualTo(BsonType.Int64));
            Assert.That(parsed["big"].AsInt64, Is.EqualTo(9_007_199_254_740_993L));
            Assert.That(parsed["uuid"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
            Assert.That(parsed["uuid"].AsBsonBinaryData.ToGuid(),
                Is.EqualTo(Guid.Parse("00112233-4455-6677-8899-aabbccddeeff")));
            Assert.That(parsed["legacy"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidLegacy));
            Assert.That(parsed["dec"].AsDecimal128.ToString(), Is.EqualTo("1.10"));
            Assert.That(parsed["int"].BsonType, Is.EqualTo(BsonType.Int32));
            Assert.That(parsed["env"].AsString, Is.EqualTo("ENV.get('PRIVATE_CANARY')"));
        });
    }

    [TestCase("{\"$oid\":\"507f1f77bcf86cd799439011\"}", true)]
    [TestCase("\"plain\"", true)]
    [TestCase("{\"nested\":{\"$numberLong\":\"1\"}}", true)]
    [TestCase("{\"$ne\":null}", true)]
    [TestCase("{\"$where\":\"true\"}", false)]
    [TestCase("{\"nested\":{\"$code\":\"x\"}}", false)]
    [TestCase("{\"$expr\":{\"$eq\":[1,1]}}", false)]
    public void LiteralValuesAcceptOnlyWrappersAndClosedOperators(string value, bool expected) =>
        Assert.That(AgentToolLiteralEjson.IsLiteralValue(value), Is.EqualTo(expected), value);

    [Test]
    public void PipelineCollectsEveryNestedNamespace()
    {
        const string pipeline = """
            [{"$match":{"status":"open"}},
             {"$lookup":{"from":"customers","localField":"customerId","foreignField":"_id","as":"customer"}},
             {"$lookup":{"from":"orders","let":{"id":"$_id"},"pipeline":[
                {"$match":{"$expr":{"$eq":["$owner","$$id"]}}},
                {"$unionWith":{"coll":"archivedOrders","pipeline":[{"$project":{"total":1}}]}}],"as":"orders"}},
             {"$facet":{"byTag":[{"$unwind":"$tags"},{"$sortByCount":"$tags"}],
                        "graph":[{"$graphLookup":{"from":"employees","startWith":"$managerId","connectFromField":"managerId","connectToField":"_id","as":"chain","maxDepth":3}}]}},
             {"$group":{"_id":"$region","total":{"$sum":"$amount"},"count":{"$count":{}}}},
             {"$sort":{"total":-1}},{"$limit":10}]
            """;

        Assert.That(AgentToolLiteralEjson.TryValidatePipeline(pipeline, out var collections), Is.True);
        Assert.That(collections, Is.EqualTo(NestedCollections));
    }

    [TestCase("[{\"$out\":\"copy\"}]")]
    [TestCase("[{\"$merge\":{\"into\":\"copy\"}}]")]
    [TestCase("[{\"$facet\":{\"x\":[{\"$merge\":{\"into\":\"copy\"}}]}}]")]
    [TestCase("[{\"$lookup\":{\"from\":\"a\",\"as\":\"x\",\"pipeline\":[{\"$out\":\"copy\"}]}}]")]
    [TestCase("[{\"$unionWith\":{\"coll\":\"a\",\"pipeline\":[{\"$facet\":{\"y\":[{\"$out\":\"copy\"}]}}]}}]")]
    [TestCase("[{\"$lookup\":{\"from\":{\"db\":\"other\",\"coll\":\"secrets\"},\"as\":\"x\",\"pipeline\":[]}}]")]
    [TestCase("[{\"$lookup\":{\"from\":\"system.profile\",\"as\":\"x\",\"pipeline\":[]}}]")]
    [TestCase("[{\"$unionWith\":\"bad$name\"}]")]
    [TestCase("[{\"$project\":{\"x\":{\"$function\":{\"body\":\"1\",\"args\":[],\"lang\":\"js\"}}}}]")]
    [TestCase("[{\"$group\":{\"_id\":null,\"x\":{\"$accumulator\":{\"init\":\"1\"}}}}]")]
    [TestCase("[{\"$match\":{\"$where\":\"true\"}}]")]
    [TestCase("[{\"$currentOp\":{}}]")]
    [TestCase("[{\"$documents\":[{\"a\":1}]}]")]
    [TestCase("[{\"$collStats\":{}}]")]
    [TestCase("[{\"$match\":{},\"$limit\":1}]")]
    [TestCase("[{\"$sample\":{\"size\":1000000}}]")]
    [TestCase("[{\"$lookup\":{\"from\":\"a\",\"localField\":\"x\",\"foreignField\":\"y\"}}]")]
    public void PipelineDeniesWritesJavaScriptAndUnprovableNamespacesAtAnyDepth(string pipeline)
    {
        Assert.That(AgentToolLiteralEjson.TryValidatePipeline(pipeline, out var collections), Is.False, pipeline);
        Assert.That(collections, Is.Empty);
    }
}
