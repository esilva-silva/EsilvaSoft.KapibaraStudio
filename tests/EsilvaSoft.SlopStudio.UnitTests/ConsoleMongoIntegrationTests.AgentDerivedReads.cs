using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Infrastructure;
using MongoDB.Bson;
using MongoDB.Driver;

namespace EsilvaSoft.SlopStudio.UnitTests;

// Lote 2 (DerivedReads) contra MongoDB real: cada tool passa pelo registry de produção, com política, principal e
// ledger LiteDB reais, e pelas fontes Mongo dedicadas do agente. Os documentos têm tipos BSON que se perdem com
// facilidade (Int64 além de 2^53, Decimal128, UUID subtipo 3 e 4, ObjectId, Date e subdocumentos).
public sealed partial class ConsoleMongoIntegrationTests
{
    private const string DerivedDatabase = "agentdb";
    private const string DerivedItems = "items";
    private const string DerivedLarge = "large";
    private const long BeyondDoublePrecision = 9_007_199_254_740_993L;
    private const string LiteralEnvTemplate = "${ENV.get('SLOP_AGENT_CANARY')}";
    private const string ResolvedEnvCanary = "resolved-canary-3D71";
    private const string SecretCanary = "canary-secret-C0FFEE";
    private const string ExplainCanary = "explain-canary-51A9";
    private static readonly Guid DerivedFirst = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private static readonly Guid DerivedSecond = Guid.Parse("8f5c3d2a-1b4e-4c6d-9a7b-0e1f2a3b4c5d");
    private static readonly ObjectId DerivedObjectId = ObjectId.Parse("64b7f0c2a1b2c3d4e5f60718");
    private static readonly DateTime DerivedWhen = new(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
    private static readonly string[] GrantedDerivedCollections = [DerivedItems, DerivedLarge];
    private static readonly string[] ExpectedIndexNames = ["_id_", "level_desc", "tag_1", "when_hidden"];
    private static readonly string[] LevelKey = ["nested.inner.level"];
    private static readonly string[] ProjectedNames = ["_id", "tag", "uuid4"];

    // "dotted.name" is one field with a literal dot; the schema path escapes it instead of nesting it.
    private static readonly (string Path, string[] Types)[] ExpectedSchemaTypes =
    [
        ("_id", ["binData", "long", "objectId", "string"]),
        ("big", ["long"]),
        ("dec", ["decimal"]),
        ("when", ["date"]),
        ("uuid4", ["binData"]),
        ("uuid3", ["binData"]),
        ("nested", ["object"]),
        ("nested.inner.level", ["long"]),
        ("nested.inner.tags", ["array"]),
        ("kind", ["binData", "decimal", "long", "objectId"]),
        ("dotted\\.name", ["int"])
    ];

    // Filters that would run JavaScript, write, reach another namespace, resolve ENV or use shell constructors.
    // Nested positions are included: the codec must reject them before BSON parsing or any driver call.
    private static readonly string[] ForbiddenFilters =
    [
        "{\"$where\":\"sleep(100) || true\"}",
        "{\"$where\":{\"$code\":\"true\"}}",
        "{\"$expr\":{\"$function\":{\"body\":\"function(){return true}\",\"args\":[],\"lang\":\"js\"}}}",
        "{\"$expr\":{\"$let\":{\"vars\":{\"x\":1},\"in\":{\"$function\":{\"body\":\"function(){return true}\",\"args\":[],\"lang\":\"js\"}}}}}",
        "{\"$expr\":{\"$accumulator\":{\"init\":\"function(){}\",\"accumulate\":\"function(){}\",\"accumulateArgs\":[],\"merge\":\"function(){}\",\"lang\":\"js\"}}}",
        "{\"$and\":[{\"$or\":[{\"tag\":\"a\"},{\"$where\":\"true\"}]}]}",
        "{\"$nor\":[{\"$and\":[{\"$expr\":{\"$function\":{\"body\":\"f\",\"args\":[],\"lang\":\"js\"}}}]}]}",
        "{\"nested\":{\"$elemMatch\":{\"$where\":\"true\"}}}",
        "{\"tag\":{\"$not\":{\"$where\":\"true\"}}}",
        "{\"tag\":{\"$regex\":\"a\",\"$where\":\"true\"}}",
        "{\"$out\":\"stolen\"}",
        "{\"$merge\":{\"into\":\"stolen\"}}",
        "{\"tag\":{\"$lookup\":{\"from\":\"secrets\",\"as\":\"x\"}}}",
        "{\"$expr\":{\"$eq\":[\"$$USER_ROLES\",1]}}",
        "{\"$comment\":\"ok\",\"$eval\":\"db.dropDatabase()\"}",
        "{\"tag\":{\"$code\":\"function(){return 1}\"}}",
        "{\"tag\":{\"$eq\":{\"$code\":\"x\"}}}",
        "{\"marker\":ENV.get(\"SLOP_AGENT_CANARY\")}",
        "{\"big\":NumberLong(1)}",
        "{\"_id\":UUID(\"00112233-4455-6677-8899-aabbccddeeff\")}",
        "{\"tag\":\"a\",}",
        "{\"tag\":\"a\",\"tag\":\"b\"}"
    ];

    private static readonly string[] ForbiddenIds =
    [
        "{\"$where\":\"true\"}",
        "{\"$code\":\"function(){return 1}\"}",
        "{\"$expr\":{\"$eq\":[1,1]}}",
        "ENV.get(\"SLOP_AGENT_CANARY\")",
        "ObjectId(\"64b7f0c2a1b2c3d4e5f60718\")",
        "{\"$binary\":\"AAECAwQFBgcICQoLDA0ODw==\",\"$type\":\"04\"}"
    ];

    private static readonly string[] ForbiddenFieldSpecs =
    [
        "{\"tag\":{\"$function\":{\"body\":\"f\",\"args\":[],\"lang\":\"js\"}}}",
        "{\"$where\":1}",
        "{\"$natural\":1}",
        "{\"tag\":{\"$meta\":\"textScore\"}}",
        "{\"tag\":\"$secret\"}"
    ];

    private static readonly string[] ForbiddenDistinctFields = ["$tag", "tag.$where", "a..b", ""];

    [Test, Category("MongoReal")]
    public async Task AgentDerivedDocumentReadsPreserveBsonTypesAgainstRealServer()
    {
        await RunDerivedReadsAsync("agent-derived-docs", async harness =>
        {
            var originals = await harness.ReadAllAsync(DerivedItems);
            Assert.That(originals, Has.Count.EqualTo(5));

            // get_document: every identifier shape goes through the literal codec and a $eq filter.
            var legacySecond = new BsonBinaryData(DerivedSecond, GuidRepresentation.CSharpLegacy);
            var byObjectId = await harness.InvokeAsync(AgentToolRegistry.GetDocumentToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, idEjson: $"{{\"$oid\":\"{DerivedObjectId}\"}}"));
            var byUuid = await harness.InvokeAsync(AgentToolRegistry.GetDocumentToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, idEjson: $"{{\"$uuid\":\"{DerivedSecond:D}\"}}"));
            var byLegacyUuid = await harness.InvokeAsync(AgentToolRegistry.GetDocumentToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, idEjson:
                    $"{{\"$binary\":{{\"base64\":\"{Convert.ToBase64String(legacySecond.Bytes)}\",\"subType\":\"03\"}}}}"));
            var byInt64 = await harness.InvokeAsync(AgentToolRegistry.GetDocumentToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, idEjson: "{\"$numberLong\":\"42\"}"));
            var byString = await harness.InvokeAsync(AgentToolRegistry.GetDocumentToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, idEjson: "\"text-id\""));
            var missing = await harness.InvokeAsync(AgentToolRegistry.GetDocumentToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, idEjson: $"{{\"$uuid\":\"{Guid.Empty:D}\"}}"));
            // Int32 7 and Int64 7 are equal for MongoDB; the literal $numberLong must not become a double lookup.
            var byInt64AsDouble = await harness.InvokeAsync(AgentToolRegistry.GetDocumentToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, idEjson: "{\"$numberDouble\":\"42.5\"}"));

            // mongo_find_one with nested Int64, relaxed date and Decimal128 literals.
            var byNested = await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems,
                    filterEjson: "{\"nested.inner.level\":{\"$numberLong\":\"5\"}}"));
            var byDate = await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems,
                    filterEjson: "{\"when\":{\"$date\":\"2026-01-02T03:04:05.678Z\"},\"tag\":\"b\"}"));
            var byDecimal = await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems,
                    filterEjson: "{\"dec\":{\"$numberDecimal\":\"1234567890.123456789012345678901234\"}}"));
            var byBigInt = await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems,
                    filterEjson: $"{{\"big\":{{\"$numberLong\":\"{BeyondDoublePrecision}\"}}}}"));
            // 2^53 + 1 rounds to 2^53 as a double; a lossy codec would match nothing or the wrong document.
            var byRoundedBigInt = await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems,
                    filterEjson: "{\"big\":{\"$numberLong\":\"9007199254740992\"}}"));
            var sorted = await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems,
                    filterEjson: "{\"tag\":{\"$in\":[\"a\",\"b\",\"c\"]}}", sortEjson: "{\"tag\":-1}",
                    projectionEjson: "{\"tag\":1,\"uuid4\":1}"));

            // sample_documents: bounded, no filter, every returned document is complete canonical EJSON.
            var sample = await harness.InvokeAsync(AgentToolRegistry.SampleDocumentsToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, limit: 20));
            var sampleLimited = await harness.InvokeAsync(AgentToolRegistry.SampleDocumentsToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, limit: 2));
            // Exactly as many documents as the limit: the adapter reads limit + 1, so it knows nothing is left.
            var sampleExact = await harness.InvokeAsync(AgentToolRegistry.SampleDocumentsToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, limit: 5));

            AssertSucceeded(byObjectId, byUuid, byLegacyUuid, byInt64, byString, missing, byInt64AsDouble, byNested,
                byDate, byDecimal, byBigInt, byRoundedBigInt, sorted, sample, sampleLimited, sampleExact);
            var firstDocument = SingleDocument(byObjectId);
            var sampled = Documents(sample, out var sampleTruncated, out var sampleHasMore);
            var limited = Documents(sampleLimited, out var limitedTruncated, out var limitedHasMore);
            var exact = Documents(sampleExact, out var exactTruncated, out var exactHasMore);
            var sortedDocument = SingleDocument(sorted);
            Assert.Multiple(() =>
            {
                // Byte-level BSON round trip: field order, numeric widths and binary subtypes all preserved.
                AssertSameBson(firstDocument, originals, "_id ObjectId");
                AssertSameBson(SingleDocument(byUuid), originals, "_id UUID subtipo 4");
                AssertSameBson(SingleDocument(byLegacyUuid), originals, "_id UUID subtipo 3");
                AssertSameBson(SingleDocument(byInt64), originals, "_id Int64");
                AssertSameBson(SingleDocument(byString), originals, "_id string");
                Assert.That(SingleDocument(byUuid)["tag"].AsString, Is.EqualTo("b"), "$uuid não pode casar subtipo 3.");
                Assert.That(SingleDocument(byLegacyUuid)["tag"].AsString, Is.EqualTo("d"));
                Assert.That(DocumentJson(missing), Is.Null);
                Assert.That(DocumentJson(byInt64AsDouble), Is.Null);

                var canonical = DocumentJson(byObjectId)!;
                Assert.That(canonical, Does.Contain($"\"_id\" : {{ \"$oid\" : \"{DerivedObjectId}\" }}"));
                Assert.That(canonical, Does.Contain($"{{ \"$numberLong\" : \"{BeyondDoublePrecision}\" }}"));
                Assert.That(canonical, Does.Contain("{ \"$numberDecimal\" : \"1234567890.123456789012345678901234\" }"));
                Assert.That(canonical, Does.Contain("\"subType\" : \"04\""));
                Assert.That(canonical, Does.Contain("\"subType\" : \"03\""));
                Assert.That(canonical, Does.Contain("{ \"$date\" : { \"$numberLong\" : \"1767323045678\" } }"));
                Assert.That(canonical, Does.Contain(LiteralEnvTemplate), "Valor literal devolvido como dado.");
                Assert.That(firstDocument["uuid4"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
                Assert.That(firstDocument["uuid4"].AsBsonBinaryData.ToGuid(), Is.EqualTo(DerivedFirst));
                Assert.That(firstDocument["uuid3"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidLegacy));
                Assert.That(firstDocument["uuid3"].AsBsonBinaryData.ToGuid(GuidRepresentation.CSharpLegacy),
                    Is.EqualTo(DerivedFirst));
                Assert.That(firstDocument["nested"]["inner"]["level"].BsonType, Is.EqualTo(BsonType.Int64));
                Assert.That(firstDocument["when"].ToUniversalTime(), Is.EqualTo(DerivedWhen));

                AssertSameBson(SingleDocument(byNested), originals, "filtro Int64 aninhado");
                Assert.That(SingleDocument(byNested)["_id"], Is.EqualTo((BsonValue)DerivedObjectId));
                Assert.That(SingleDocument(byDate)["tag"].AsString, Is.EqualTo("b"));
                Assert.That(SingleDocument(byDecimal)["_id"], Is.EqualTo((BsonValue)DerivedObjectId));
                Assert.That(SingleDocument(byBigInt)["_id"], Is.EqualTo((BsonValue)DerivedObjectId));
                Assert.That(DocumentJson(byRoundedBigInt), Is.Null, "Int64 não pode ser arredondado para double.");
                Assert.That(sortedDocument["tag"].AsString, Is.EqualTo("c"));
                Assert.That(sortedDocument.Names, Is.EquivalentTo(ProjectedNames),
                    "Projeção literal aplicada pelo servidor.");
                Assert.That(sortedDocument["uuid4"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
                Assert.That(sortedDocument["uuid4"].AsBsonBinaryData.ToGuid(), Is.EqualTo(DerivedSecond));

                Assert.That(sampled, Has.Count.EqualTo(5));
                foreach (var document in sampled) AssertSameBson(document, originals, "sample_documents");
                Assert.That(sampleTruncated, Is.False);
                Assert.That(sampleHasMore, Is.False);
                Assert.That(limited, Has.Count.EqualTo(2));
                Assert.That(limitedTruncated, Is.False, "Limite de documentos não é truncamento de saída.");
                Assert.That(limitedHasMore, Is.True);
                Assert.That(exact, Has.Count.EqualTo(5));
                Assert.That(exactTruncated, Is.False);
                Assert.That(exactHasMore, Is.False, "hasMore só quando o servidor entregou limit + 1.");
            });
            await harness.AssertLedgerCleanAsync();
        });
    }

    [Test, Category("MongoReal")]
    public async Task AgentDerivedSchemaDistinctIndexesAndExplainUseRealServer()
    {
        await RunDerivedReadsAsync("agent-derived-meta", async harness =>
        {
            var consentCallsBefore = harness.Consent.Calls;
            var schema = await harness.InvokeAsync(AgentToolRegistry.GetCollectionSchemaToolName,
                AgentOutputDataScope.Schema, harness.Args(DerivedItems, sampleSize: 20));
            var schemaWithoutConsent = await harness.InvokeAsync(AgentToolRegistry.GetCollectionSchemaToolName,
                AgentOutputDataScope.Schema, harness.Args(DerivedLarge, sampleSize: 20));
            var distinct = await harness.InvokeAsync(AgentToolRegistry.MongoDistinctToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, field: "kind"));
            var distinctLimited = await harness.InvokeAsync(AgentToolRegistry.MongoDistinctToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, field: "kind", maximumValues: 2));
            var distinctFiltered = await harness.InvokeAsync(AgentToolRegistry.MongoDistinctToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, field: "nested.inner.level",
                    filterEjson: $"{{\"marker\":\"{LiteralEnvTemplate}\"}}"));
            var indexes = await harness.InvokeAsync(AgentToolRegistry.GetIndexesToolName,
                AgentOutputDataScope.Metadata, harness.Args(DerivedItems));
            var explain = await harness.InvokeAsync(AgentToolRegistry.MongoExplainToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems,
                    filterEjson: $"{{\"tag\":\"{ExplainCanary}\"}}", sortEjson: "{\"tag\":1}"));
            var explainScan = await harness.InvokeAsync(AgentToolRegistry.MongoExplainToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems,
                    filterEjson: $"{{\"big\":{{\"$numberLong\":\"{BeyondDoublePrecision}\"}}}}"));

            AssertSucceeded(schema, distinct, distinctLimited, distinctFiltered, indexes, explain, explainScan);
            using var schemaJson = JsonDocument.Parse(schema.StructuredContentJson!);
            var fields = schemaJson.RootElement.GetProperty("fields").EnumerateArray().ToDictionary(
                item => item.GetProperty("path").GetString()!,
                item => (Types: item.GetProperty("bsonTypes").EnumerateArray().Select(type => type.GetString()!).ToArray(),
                    Count: item.GetProperty("observedCount").GetInt32()), StringComparer.Ordinal);
            var values = DistinctValues(distinct, out var distinctTruncated, out var distinctReason);
            var limitedValues = DistinctValues(distinctLimited, out var limitedTruncated, out var limitedReason);
            var filteredValues = DistinctValues(distinctFiltered, out _, out _);
            using var indexJson = JsonDocument.Parse(indexes.StructuredContentJson!);
            var indexList = indexJson.RootElement.GetProperty("indexes").EnumerateArray()
                .ToDictionary(item => item.GetProperty("name").GetString()!, item => item, StringComparer.Ordinal);
            var plan = PlanJson(explain);
            var scanPlan = PlanJson(explainScan);
            var plannedIndexes = CollectPlanValues(plan, "indexName");
            var scanStages = CollectPlanValues(scanPlan, "stage");

            Assert.Multiple(() =>
            {
                Assert.That(schemaJson.RootElement.GetProperty("sampleSize").GetInt32(), Is.EqualTo(5));
                Assert.That(schemaJson.RootElement.GetProperty("source").GetString(), Is.EqualTo("sample"));
                foreach (var (path, types) in ExpectedSchemaTypes)
                    Assert.That(fields.TryGetValue(path, out var observed) ? observed.Types : null,
                        Is.EquivalentTo(types), path);
                Assert.That(fields["_id"].Count, Is.EqualTo(5));
                Assert.That(fields["nested.inner.level"].Count, Is.EqualTo(1));
                Assert.That(schema.StructuredContentJson, Does.Not.Contain(SecretCanary).And.Not.Contain("text-id")
                    .And.Not.Contain(BeyondDoublePrecision.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    "Schema não carrega valores.");
                Assert.That(schemaWithoutConsent.ErrorCode, Is.EqualTo("PermissionDenied"));
                Assert.That(harness.Consent.Calls - consentCallsBefore, Is.EqualTo(2));

                // Five BSON-distinct kinds, including UUID subtype 3 and 4 with different bytes.
                Assert.That(values, Has.Count.EqualTo(5));
                Assert.That(values, Is.EquivalentTo(new BsonValue[]
                {
                    new BsonInt64(BeyondDoublePrecision), BsonDecimal128.Create("0.10"),
                    new BsonBinaryData(DerivedFirst, GuidRepresentation.Standard),
                    new BsonBinaryData(DerivedFirst, GuidRepresentation.CSharpLegacy), DerivedObjectId
                }));
                Assert.That(values.Single(value => value.IsDecimal128).AsDecimal128.ToString(), Is.EqualTo("0.10"),
                    "Decimal128 preserva escala.");
                Assert.That(distinctTruncated, Is.False);
                Assert.That(distinctReason, Is.Null);
                Assert.That(limitedValues, Has.Count.EqualTo(2));
                Assert.That(limitedTruncated, Is.True);
                Assert.That(limitedReason, Is.EqualTo("ValueLimit"));
                Assert.That(filteredValues, Is.EqualTo(new BsonValue[] { new BsonInt64(5) }),
                    "Filtro literal com texto ENV casa apenas o texto armazenado.");

                Assert.That(indexList.Keys, Is.EquivalentTo(ExpectedIndexNames));
                Assert.That(indexList["tag_1"].GetProperty("unique").GetBoolean(), Is.True);
                Assert.That(indexList["level_desc"].GetProperty("sparse").GetBoolean(), Is.True);
                Assert.That(indexList["level_desc"].GetProperty("keyFields").EnumerateArray()
                    .Select(item => item.GetString()), Is.EqualTo(LevelKey));
                Assert.That(indexList["when_hidden"].GetProperty("hidden").GetBoolean(), Is.True);
                Assert.That(indexJson.RootElement.GetProperty("truncated").GetBoolean(), Is.False);
                Assert.That(indexes.StructuredContentJson, Does.Not.Contain("partialFilterExpression")
                    .And.Not.Contain("expireAfterSeconds").And.Not.Contain("\"v\""), "Somente a allowlist de índices.");

                Assert.That(plannedIndexes, Does.Contain("tag_1"));
                Assert.That(plan.GetRawText(), Does.Not.Contain(ExplainCanary), "Plano não ecoa limites do filtro.");
                Assert.That(scanStages, Does.Contain("COLLSCAN"));
                Assert.That(scanPlan.GetRawText(), Does.Not.Contain("9007199254740993"));
            });
            await harness.AssertLedgerCleanAsync();
        });
    }

    [Test, Category("MongoReal")]
    public async Task AgentDerivedReadsRejectCodeEnvAndWriteOperatorsBeforeRealDriver()
    {
        await RunDerivedReadsAsync("agent-derived-deny", async harness =>
        {
            // Positive control first: the literal ENV-like text is data and matches only the stored literal.
            var literal = await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems,
                    filterEjson: $"{{\"marker\":\"{LiteralEnvTemplate}\"}}"));
            var resolved = await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems,
                    filterEjson: $"{{\"marker\":\"{ResolvedEnvCanary}\"}}"));
            AssertSucceeded(literal, resolved);
            Assert.That(SingleDocument(literal)["_id"], Is.EqualTo((BsonValue)DerivedObjectId));
            Assert.That(SingleDocument(resolved)["tag"].AsString, Is.EqualTo("b"));

            var dispatchedBefore = harness.Sources.Calls;
            var consentBefore = harness.Consent.Calls;
            var rejected = new List<(string Case, AgentToolInvocationResult Result)>();
            foreach (var filter in ForbiddenFilters)
            {
                rejected.Add(("find_one " + filter, await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, filterEjson: filter))));
                rejected.Add(("find " + filter, await harness.InvokeAsync(AgentToolRegistry.MongoFindToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, filterEjson: filter))));
                rejected.Add(("explain " + filter, await harness.InvokeAsync(AgentToolRegistry.MongoExplainToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, filterEjson: filter))));
                rejected.Add(("distinct " + filter, await harness.InvokeAsync(AgentToolRegistry.MongoDistinctToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, field: "tag", filterEjson: filter))));
            }
            foreach (var id in ForbiddenIds)
                rejected.Add(("get_document " + id, await harness.InvokeAsync(AgentToolRegistry.GetDocumentToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, idEjson: id))));
            foreach (var spec in ForbiddenFieldSpecs)
            {
                rejected.Add(("sample projection " + spec, await harness.InvokeAsync(
                    AgentToolRegistry.SampleDocumentsToolName, AgentOutputDataScope.DocumentValues,
                    harness.Args(DerivedItems, projectionEjson: spec))));
                rejected.Add(("find_one sort " + spec, await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, sortEjson: spec))));
                rejected.Add(("explain sort " + spec, await harness.InvokeAsync(AgentToolRegistry.MongoExplainToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, sortEjson: spec))));
            }
            foreach (var field in ForbiddenDistinctFields)
                rejected.Add(("distinct field " + field, await harness.InvokeAsync(AgentToolRegistry.MongoDistinctToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, field: field))));
            // Out-of-range bounds are invalid arguments, never silently clamped into a larger read.
            rejected.Add(("sample limit", await harness.InvokeAsync(AgentToolRegistry.SampleDocumentsToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, limit: 21))));
            rejected.Add(("distinct maximum", await harness.InvokeAsync(AgentToolRegistry.MongoDistinctToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, field: "tag", maximumValues: 101))));
            rejected.Add(("schema size", await harness.InvokeAsync(AgentToolRegistry.GetCollectionSchemaToolName,
                AgentOutputDataScope.Schema, harness.Args(DerivedItems, sampleSize: 101))));
            rejected.Add(("explain maxTime", await harness.InvokeAsync(AgentToolRegistry.MongoExplainToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, maxTimeMs: 30_001))));
            rejected.Add(("get_document extra", await harness.InvokeAsync(AgentToolRegistry.GetDocumentToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems, idEjson: "1", filterEjson: "{}"))));

            // Namespaces outside the grant are denied by policy, also before any source call.
            var denied = new List<(string Case, AgentToolInvocationResult Result)>
            {
                ("find_one secrets", await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args("secrets"))),
                ("get_document secrets", await harness.InvokeAsync(AgentToolRegistry.GetDocumentToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args("secrets", idEjson: "1"))),
                ("sample secrets", await harness.InvokeAsync(AgentToolRegistry.SampleDocumentsToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args("secrets"))),
                ("distinct secrets", await harness.InvokeAsync(AgentToolRegistry.MongoDistinctToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args("secrets", field: "token"))),
                ("indexes secrets", await harness.InvokeAsync(AgentToolRegistry.GetIndexesToolName,
                    AgentOutputDataScope.Metadata, harness.Args("secrets"))),
                ("explain secrets", await harness.InvokeAsync(AgentToolRegistry.MongoExplainToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args("secrets"))),
                ("schema secrets", await harness.InvokeAsync(AgentToolRegistry.GetCollectionSchemaToolName,
                    AgentOutputDataScope.Schema, harness.Args("secrets"))),
                // A granted read never widens the output scope: document values under a metadata grant are denied.
                ("find_one metadata scope", await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                    AgentOutputDataScope.Metadata, harness.Args(DerivedItems))),
                ("indexes documents scope", await harness.InvokeAsync(AgentToolRegistry.GetIndexesToolName,
                    AgentOutputDataScope.DocumentValues, harness.Args(DerivedItems)))
            };
            var systemNamespace = await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                AgentOutputDataScope.DocumentValues, harness.Args("system.users", database: "admin"));

            Assert.Multiple(() =>
            {
                foreach (var (name, result) in rejected)
                    Assert.That(result.ErrorCode, Is.EqualTo("InvalidArguments"), name);
                foreach (var (name, result) in denied)
                    Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"), name);
                Assert.That(systemNamespace.Succeeded, Is.False);
                Assert.That(harness.Sources.Calls, Is.EqualTo(dispatchedBefore), "Nada rejeitado chega ao driver.");
                Assert.That(harness.Consent.Calls, Is.EqualTo(consentBefore), "Nem ao consentimento de amostragem.");
            });
            using (var client = new MongoClient(harness.ServerUri))
            {
                var names = await (await client.GetDatabase(DerivedDatabase).ListCollectionNamesAsync()).ToListAsync();
                Assert.That(names, Does.Not.Contain("stolen"), "Nenhum $out/$merge executado.");
            }
            await harness.AssertLedgerCleanAsync();
        });
    }

    [Test, Category("MongoReal")]
    public async Task AgentDerivedReadsTruncateOnlyAtWholeDocumentsAgainstRealServer()
    {
        await RunDerivedReadsAsync("agent-derived-trunc", async harness =>
        {
            var originals = await harness.ReadAllAsync(DerivedLarge);
            // Three documents of ~40 KB each fit the source byte budget, but '<' becomes < in the JSON
            // envelope (6x), so only one document fits the 256 KiB tool output: truncation must be by document.
            var sample = await harness.InvokeAsync(AgentToolRegistry.SampleDocumentsToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedLarge, limit: 3));
            var find = await harness.InvokeAsync(AgentToolRegistry.MongoFindToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedLarge, limit: 3,
                    filterEjson: "{\"kind\":\"escaped\"}", sortEjson: "{\"_id\":1}"));
            // Raw budget: 7 documents of ~40 KB exceed the adapter's 256 KiB cursor budget; whole documents only.
            var raw = await harness.InvokeAsync(AgentToolRegistry.MongoFindToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedLarge, limit: 20,
                    filterEjson: "{\"kind\":\"plain\"}", sortEjson: "{\"_id\":1}"));
            // One document whose escaped envelope alone exceeds the output limit is refused, never cut.
            var oversized = await harness.InvokeAsync(AgentToolRegistry.GetDocumentToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedLarge, idEjson: "{\"$numberLong\":\"100\"}"));
            var oversizedOne = await harness.InvokeAsync(AgentToolRegistry.MongoFindOneToolName,
                AgentOutputDataScope.DocumentValues, harness.Args(DerivedLarge, filterEjson: "{\"kind\":\"oversized\"}"));

            AssertSucceeded(sample, find, raw);
            var sampled = Documents(sample, out var sampleTruncated, out var sampleHasMore);
            var found = Documents(find, out var findTruncated, out var findHasMore);
            var rawDocuments = Documents(raw, out var rawTruncated, out var rawHasMore);
            Assert.Multiple(() =>
            {
                foreach (var result in new[] { sample, find, raw })
                {
                    Assert.That(Encoding.UTF8.GetByteCount(result.StructuredContentJson!), Is.LessThanOrEqualTo(256 * 1024));
                    using var envelope = JsonDocument.Parse(result.StructuredContentJson!);
                    Assert.That(envelope.RootElement.GetProperty("truncationReason").GetString(), Is.EqualTo("OutputLimit"));
                }
                Assert.That(sampled, Has.Count.EqualTo(1));
                Assert.That(sampleTruncated && sampleHasMore, Is.True);
                Assert.That(found, Has.Count.EqualTo(1));
                Assert.That(findTruncated && findHasMore, Is.True);
                Assert.That(found[0]["_id"].AsInt64, Is.EqualTo(1));
                Assert.That(rawDocuments, Has.Count.InRange(1, 6));
                Assert.That(rawTruncated && rawHasMore, Is.True);
                Assert.That(rawDocuments.Select(document => document["_id"].AsInt64),
                    Is.EqualTo(Enumerable.Range(10, rawDocuments.Count).Select(value => (long)value)), "Ordem estável.");
                foreach (var document in sampled.Concat(found).Concat(rawDocuments))
                    AssertSameBson(document, originals, "documento inteiro após truncamento");
                Assert.That(oversized.ErrorCode, Is.EqualTo("ResultTooLarge"));
                Assert.That(oversized.StructuredContentJson, Is.Null);
                Assert.That(oversizedOne.ErrorCode, Is.EqualTo("ResultTooLarge"));
                Assert.That(oversizedOne.StructuredContentJson, Is.Null);
            });
            await harness.AssertLedgerCleanAsync();
        });
    }

    private static async Task RunDerivedReadsAsync(string name, Func<DerivedReadsHarness, Task> body)
    {
        var executable = ResolveMongodExecutable();
        if (executable is null) Assert.Ignore("Fixture MongoDB portátil ausente.");
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, name + "-" + Guid.NewGuid().ToString("N"));
        var logPath = Path.Combine(TestContext.CurrentContext.WorkDirectory,
            $"mongod-{Path.GetFileName(Path.GetDirectoryName(directory))}-{Path.GetFileName(directory)}.log");
        var completed = false;
        try
        {
            using var server = await StartServer(executable!, directory);
            using var repository = new LiteDbConnectionProfileRepository(Path.Combine(directory, "workspace.db"));
            using var pool = new MongoClientPool();
            await SeedDerivedReadsAsync(server.Uri);

#pragma warning disable CA1859 // The facet is exercised through its interface, exactly as DI composes it.
            IAgentPrincipalAuthority authority = repository;
#pragma warning restore CA1859
            IAgentAuthorizationPolicyRepository policies = repository;
            IAgentAuditRepository ledger = repository;
            var principalId = await authority.GetInternalPrincipalIdAsync();
            var profile = ConnectionProfile.Create("Agente derivado", server.Uri) with { SourceGenerationId = Guid.NewGuid() };
            var sessionId = Guid.NewGuid();
            var grants = new List<AgentPermissionGrant>();
            foreach (var collection in GrantedDerivedCollections)
            {
                var scope = AgentNamespaceScope.ForCollection(profile.Id, DerivedDatabase, collection);
                AgentPermissionGrant Grant(AgentPermission permission, AgentOutputDataScope output) =>
                    new(principalId, AgentInvocationScope.ForSession(sessionId), profile.SourceGenerationId!.Value,
                        permission, scope, AgentOutputDestination.Local(), output);
                grants.Add(Grant(AgentPermission.ReadMetadata, AgentOutputDataScope.Metadata));
                grants.Add(Grant(AgentPermission.ReadSchema, AgentOutputDataScope.Schema));
                grants.Add(Grant(AgentPermission.ExecuteReadQueries, AgentOutputDataScope.Schema));
                grants.Add(Grant(AgentPermission.ExecuteReadQueries, AgentOutputDataScope.DocumentValues));
                grants.Add(Grant(AgentPermission.ReadDocuments, AgentOutputDataScope.DocumentValues));
                grants.Add(Grant(AgentPermission.ReadDiagnostics, AgentOutputDataScope.DocumentValues));
            }
            await policies.SaveAsync(principalId, grants, 0);
            var issued = await authority.IssueInternalAsync();
            Assert.That(issued.IsIssued, Is.True, issued.Status.ToString());

            var secrets = new SessionConnectionSecretStore();
            var sources = new CountingDerivedSources(new MongoAgentFindSource(secrets, repository, pool),
                new MongoAgentIndexSource(secrets, repository, pool), new MongoAgentExplainSource(secrets, repository, pool));
            var consent = new ExactSchemaConsent(profile.Id, DerivedDatabase, DerivedItems);
            var registry = new AgentToolRegistry(new FixedProfiles(profile), policies,
                new AgentPermissionEvaluator(policies), ledger, TimeSpan.FromSeconds(20),
                new MongoMetadataSource(secrets, repository, pool), consent, find: sources, count: sources,
                distinct: sources, indexes: sources, explain: sources,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
                principalAuthority: authority);
            await body(new DerivedReadsHarness(registry, issued.Principal!, profile, sessionId, sources, consent,
                ledger, server.Uri));
            TestContext.Out.WriteLine($"MongoDB {server.Version}: {name} via registry DerivedReads com fontes reais.");
            completed = true;
        }
        finally
        {
            CleanupDatabaseDirectory(directory, completed);
            if (completed && File.Exists(logPath)) File.Delete(logPath);
        }
    }

    private static string? ResolveMongodExecutable()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "EsilvaSoft.SlopStudio.slnx"))) root = root.Parent;
        var binaries = Path.Combine(root!.FullName, ".cache", "console-mongo", "server");
        return Environment.GetEnvironmentVariable("SLOP_CONSOLE_MONGOD") ??
            (Directory.Exists(binaries)
                ? Directory.EnumerateFiles(binaries, OperatingSystem.IsWindows() ? "mongod.exe" : "mongod",
                    SearchOption.AllDirectories).FirstOrDefault()
                : null);
    }

    private static async Task SeedDerivedReadsAsync(string uri)
    {
        using var client = new MongoClient(uri);
        var database = client.GetDatabase(DerivedDatabase);
        var items = database.GetCollection<BsonDocument>(DerivedItems);
        await items.InsertManyAsync(
        [
            new BsonDocument
            {
                ["_id"] = DerivedObjectId,
                ["uuid4"] = new BsonBinaryData(DerivedFirst, GuidRepresentation.Standard),
                ["uuid3"] = new BsonBinaryData(DerivedFirst, GuidRepresentation.CSharpLegacy),
                ["big"] = new BsonInt64(BeyondDoublePrecision),
                ["dec"] = BsonDecimal128.Create("1234567890.123456789012345678901234"),
                ["when"] = new BsonDateTime(DerivedWhen),
                ["nested"] = new BsonDocument
                {
                    ["inner"] = new BsonDocument { ["level"] = new BsonInt64(5), ["tags"] = new BsonArray { "x", "y" } },
                    ["label"] = "n1"
                },
                ["tag"] = "a",
                ["marker"] = LiteralEnvTemplate,
                ["kind"] = new BsonInt64(BeyondDoublePrecision),
                ["dotted.name"] = 1
            },
            new BsonDocument
            {
                ["_id"] = new BsonBinaryData(DerivedSecond, GuidRepresentation.Standard), ["tag"] = "b",
                ["big"] = new BsonInt64(1), ["when"] = new BsonDateTime(DerivedWhen), ["marker"] = ResolvedEnvCanary,
                ["kind"] = BsonDecimal128.Create("0.10")
            },
            new BsonDocument
            {
                ["_id"] = new BsonInt64(42), ["tag"] = "c",
                ["uuid4"] = new BsonBinaryData(DerivedSecond, GuidRepresentation.Standard),
                ["kind"] = new BsonBinaryData(DerivedFirst, GuidRepresentation.Standard)
            },
            new BsonDocument
            {
                ["_id"] = new BsonBinaryData(DerivedSecond, GuidRepresentation.CSharpLegacy), ["tag"] = "d",
                ["kind"] = new BsonBinaryData(DerivedFirst, GuidRepresentation.CSharpLegacy)
            },
            new BsonDocument { ["_id"] = "text-id", ["tag"] = "e", ["kind"] = DerivedObjectId }
        ]);
        await items.Indexes.CreateManyAsync(
        [
            new CreateIndexModel<BsonDocument>(new BsonDocument("tag", 1),
                new CreateIndexOptions { Name = "tag_1", Unique = true }),
            new CreateIndexModel<BsonDocument>(new BsonDocument("nested.inner.level", -1),
                new CreateIndexOptions { Name = "level_desc", Sparse = true }),
            new CreateIndexModel<BsonDocument>(new BsonDocument("when", 1),
                new CreateIndexOptions { Name = "when_hidden", Hidden = true })
        ]);

        var large = database.GetCollection<BsonDocument>(DerivedLarge);
        var documents = new List<BsonDocument>();
        for (var i = 1; i <= 3; i++)
            documents.Add(new BsonDocument
            {
                ["_id"] = new BsonInt64(i), ["kind"] = "escaped", ["payload"] = new string('<', 40_000),
                ["uuid"] = new BsonBinaryData(Guid.NewGuid(), GuidRepresentation.Standard)
            });
        for (var i = 10; i < 17; i++)
            documents.Add(new BsonDocument
            {
                ["_id"] = new BsonInt64(i), ["kind"] = "plain", ["payload"] = new string('p', 40_000),
                ["big"] = new BsonInt64(long.MaxValue - i)
            });
        documents.Add(new BsonDocument
        {
            ["_id"] = new BsonInt64(100), ["kind"] = "oversized", ["payload"] = new string('<', 50_000)
        });
        await large.InsertManyAsync(documents);
        await database.GetCollection<BsonDocument>("secrets").InsertOneAsync(new BsonDocument("token", SecretCanary));
    }

    private static void AssertSucceeded(params AgentToolInvocationResult[] results)
    {
        for (var i = 0; i < results.Length; i++)
            Assert.That(results[i].Succeeded, Is.True, $"Chamada {i}: {results[i].ErrorCode}");
    }

    private static void AssertSameBson(BsonDocument actual, IReadOnlyDictionary<BsonValue, BsonDocument> originals,
        string because)
    {
        Assert.That(originals.TryGetValue(actual["_id"], out var original), Is.True, because);
        Assert.That(actual.ToBson(), Is.EqualTo(original!.ToBson()), because);
    }

    private static string? DocumentJson(AgentToolInvocationResult result)
    {
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        var value = output.RootElement.GetProperty("documentEjson");
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static BsonDocument SingleDocument(AgentToolInvocationResult result) =>
        BsonDocument.Parse(DocumentJson(result) ?? throw new AssertionException("Documento ausente."));

    private static List<BsonDocument> Documents(AgentToolInvocationResult result, out bool truncated, out bool hasMore)
    {
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        truncated = output.RootElement.GetProperty("truncated").GetBoolean();
        hasMore = output.RootElement.GetProperty("hasMore").GetBoolean();
        Assert.That(output.RootElement.GetProperty("returnedCount").GetInt32(),
            Is.EqualTo(output.RootElement.GetProperty("documentsEjson").GetArrayLength()));
        return output.RootElement.GetProperty("documentsEjson").EnumerateArray()
            .Select(item => BsonDocument.Parse(item.GetString()!)).ToList();
    }

    private static List<BsonValue> DistinctValues(AgentToolInvocationResult result, out bool truncated,
        out string? reason)
    {
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        truncated = output.RootElement.GetProperty("truncated").GetBoolean();
        reason = output.RootElement.TryGetProperty("truncationReason", out var value) ? value.GetString() : null;
        return output.RootElement.GetProperty("valuesEjson").EnumerateArray()
            .Select(item => BsonDocument.Parse("{\"v\":" + item.GetString() + "}")["v"]).ToList();
    }

    private static JsonElement PlanJson(AgentToolInvocationResult result)
    {
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.That(output.RootElement.GetProperty("verbosity").GetString(), Is.EqualTo("queryPlanner"));
        using var plan = JsonDocument.Parse(output.RootElement.GetProperty("planEjson").GetString()!);
        return plan.RootElement.Clone();
    }

    private static List<string> CollectPlanValues(JsonElement element, string name)
    {
        var values = new List<string>();
        void Visit(JsonElement node)
        {
            if (node.ValueKind == JsonValueKind.Array)
                foreach (var item in node.EnumerateArray()) Visit(item);
            if (node.ValueKind != JsonValueKind.Object) return;
            foreach (var property in node.EnumerateObject())
            {
                if (property.NameEquals(name) && property.Value.ValueKind == JsonValueKind.String)
                    values.Add(property.Value.GetString()!);
                Visit(property.Value);
            }
        }
        Visit(element);
        return values;
    }

    private sealed class DerivedReadsHarness(
        AgentToolRegistry registry,
        AgentPrincipal principal,
        ConnectionProfile profile,
        Guid sessionId,
        CountingDerivedSources sources,
        ExactSchemaConsent consent,
        IAgentAuditRepository ledger,
        string serverUri)
    {
        public CountingDerivedSources Sources { get; } = sources;
        public ExactSchemaConsent Consent { get; } = consent;
        public string ServerUri { get; } = serverUri;

        // A fresh turn per call keeps the per-turn quota out of the way; the session grant still applies.
        public Task<AgentToolInvocationResult> InvokeAsync(string tool, AgentOutputDataScope scope, string arguments) =>
            registry.InvokeAsync(principal, new AgentInvocationContext(null, null, sessionId, Guid.NewGuid()),
                AgentOutputDestination.Local(), scope, tool, arguments);

        public string Args(string collection, string database = DerivedDatabase, string? filterEjson = null,
            string? projectionEjson = null, string? sortEjson = null, string? idEjson = null, string? field = null,
            int? limit = null, int? sampleSize = null, int? maximumValues = null, int? maxTimeMs = null)
        {
            var arguments = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["connectionId"] = profile.Id,
                ["database"] = database,
                ["collection"] = collection
            };
            if (filterEjson is not null) arguments["filterEjson"] = filterEjson;
            if (projectionEjson is not null) arguments["projectionEjson"] = projectionEjson;
            if (sortEjson is not null) arguments["sortEjson"] = sortEjson;
            if (idEjson is not null) arguments["idEjson"] = idEjson;
            if (field is not null) arguments["field"] = field;
            if (limit is not null) arguments["limit"] = limit;
            if (sampleSize is not null) arguments["sampleSize"] = sampleSize;
            if (maximumValues is not null) arguments["maximumValues"] = maximumValues;
            if (maxTimeMs is not null) arguments["maxTimeMs"] = maxTimeMs;
            return JsonSerializer.Serialize(arguments);
        }

        public async Task<IReadOnlyDictionary<BsonValue, BsonDocument>> ReadAllAsync(string collection)
        {
            using var client = new MongoClient(ServerUri);
            var documents = await client.GetDatabase(DerivedDatabase).GetCollection<BsonDocument>(collection)
                .Find(new BsonDocument()).ToListAsync();
            return documents.ToDictionary(document => document["_id"]);
        }

        // Every intent has a terminal outcome and nothing from the documents or connection reached the ledger.
        public async Task AssertLedgerCleanAsync()
        {
            Assert.That(await ledger.GetPendingAsync(), Is.Empty);
            var persisted = JsonSerializer.Serialize(await ledger.GetRecentAsync(500));
            Assert.That(persisted, Does.Not.Contain(SecretCanary).And.Not.Contain(ServerUri)
                .And.Not.Contain("9007199254740993").And.Not.Contain(ExplainCanary).And.Not.Contain("text-id")
                .And.Not.Contain("sleep(").And.Not.Contain("dropDatabase"));
        }
    }

    // Local consent is trusted host state; this stub grants exactly one namespace and records every request.
    private sealed class ExactSchemaConsent(Guid connectionId, string database, string collection)
        : IAgentSchemaSamplingConsentProvider
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        public Task<bool> HasLocalConsentAsync(AgentSchemaSamplingRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(request.ConnectionId == connectionId &&
                string.Equals(request.Database, database, StringComparison.Ordinal) &&
                string.Equals(request.Collection, collection, StringComparison.Ordinal));
        }
    }

    // Counts real dispatches to MongoDB; the inner handlers are the production agent sources.
    private sealed class CountingDerivedSources(
        MongoAgentFindSource find,
        MongoAgentIndexSource indexes,
        MongoAgentExplainSource explain)
        : IAgentMongoFindSource, IAgentMongoCountSource, IAgentMongoDistinctSource, IAgentMongoIndexSource,
            IAgentMongoExplainSource
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        private T Count<T>(T operation)
        {
            Interlocked.Increment(ref _calls);
            return operation;
        }

        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken) => Count(find.FindAsync(profile, query, cancellationToken));

        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken) => Count(find.FindByIdAsync(profile, query, cancellationToken));

        public Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query,
            CancellationToken cancellationToken) => Count(find.CountAsync(profile, query, cancellationToken));

        public Task<AgentMongoDistinctPage> DistinctAsync(ConnectionProfile profile, AgentMongoDistinctQuery query,
            CancellationToken cancellationToken) => Count(find.DistinctAsync(profile, query, cancellationToken));

        public Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database,
            string collection, TimeSpan maximumExecutionTime, CancellationToken cancellationToken) =>
            Count(indexes.GetIndexesAsync(profile, database, collection, maximumExecutionTime, cancellationToken));

        public Task<AgentMongoExplainResult> ExplainAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken) => Count(explain.ExplainAsync(profile, query, cancellationToken));
    }
}
