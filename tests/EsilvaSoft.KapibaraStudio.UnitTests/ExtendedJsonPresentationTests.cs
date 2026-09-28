using System.Text.Json;
using System.Text.RegularExpressions;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ExtendedJsonPresentationTests
{
    private const string Sample = "00112233-4455-6677-8899-aabbccddeeff";
    private static readonly int[] Positions = [0, 1, 2];
    private static string Binary(string hex, string subType) =>
        "{\"$binary\":{\"base64\":\"" + Convert.ToBase64String(Convert.FromHexString(hex)) + "\",\"subType\":\"" + subType + "\"}}";

    // Hand-written Canonical Extended JSON; the MongoDB driver parses it independently as the oracle.
    private static readonly string Typed = "{\"_id\":{\"$oid\":\"64b000000000000000000001\"},\"int32\":{\"$numberInt\":\"-7\"},\"int64\":{\"$numberLong\":\"9223372036854775807\"},"
        + "\"double\":{\"$numberDouble\":\"-0.0\"},\"decimal\":{\"$numberDecimal\":\"1234567890.123456789012345678901234\"},\"date\":{\"$date\":{\"$numberLong\":\"1700000000123\"}},"
        + "\"standard\":" + Binary("00112233445566778899aabbccddeeff", "04") + ",\"csharp\":" + Binary("33221100554477668899aabbccddeeff", "03")
        + ",\"java\":" + Binary("7766554433221100ffeeddccbbaa9988", "03") + ",\"python\":" + Binary("00112233445566778899aabbccddeeff", "03")
        + ",\"bytes\":" + Binary("0102", "00") + ",\"texto\":\"S\\u00e3o \\\"Paulo\\\"\",\"endereco.cidade\":\"Recife\",\"nulo\":null,\"vazio\":[],\"objetoVazio\":{},"
        + "\"itens\":[{\"sku\":\"a\"},{\"sku\":\"b\"}],\"regex\":{\"$regularExpression\":{\"pattern\":\"^a\",\"options\":\"i\"}},\"ts\":{\"$timestamp\":{\"t\":1700000000,\"i\":3}},\"min\":{\"$minKey\":1}}";

    [Test]
    public void FormattingChangesOnlyWhitespaceAndKeepsTypeWrappersOnOneLine()
    {
        var formatted = ExtendedJsonFormatter.Format(Typed);
        Assert.Multiple(() =>
        {
            Assert.That(BsonDocument.Parse(formatted), Is.EqualTo(BsonDocument.Parse(Typed)));
            Assert.That(Regex.Replace(formatted, "\\s", ""), Is.EqualTo(Regex.Replace(Typed, "\\s", "")));
            Assert.That(formatted, Does.StartWith("{\n  \"_id\": {\"$oid\":\"64b000000000000000000001\"},\n  \"int32\": {\"$numberInt\":\"-7\"},\n"));
            Assert.That(formatted, Does.Contain("\n  \"int64\": {\"$numberLong\":\"9223372036854775807\"},\n"));
            Assert.That(formatted, Does.Contain("\n  \"date\": {\"$date\":{\"$numberLong\":\"1700000000123\"}},\n"));
            Assert.That(formatted, Does.Contain("\n  \"python\": " + Binary("00112233445566778899aabbccddeeff", "03") + ",\n"));
            Assert.That(formatted, Does.Contain("\n  \"texto\": \"S\\u00e3o \\\"Paulo\\\"\",\n"), "Escapes are copied, not re-encoded.");
            Assert.That(formatted, Does.Contain("\n  \"endereco.cidade\": \"Recife\",\n  \"nulo\": null,\n  \"vazio\": [],\n  \"objetoVazio\": {},\n"));
            Assert.That(formatted, Does.Contain("\n  \"itens\": [\n    {\n      \"sku\": \"a\"\n    },\n"));
            Assert.That(formatted, Does.EndWith("\n  \"min\": {\"$minKey\":1}\n}"));
        });
    }

    [Test]
    public void ArraysDeepStructuresAndRelaxedNumbersAreIndentedWithoutConversion()
    {
        Assert.That(ExtendedJsonFormatter.Format("[1,[2,3],{\"k\":\"v\"}]"), Is.EqualTo("[\n  1,\n  [\n    2,\n    3\n  ],\n  {\n    \"k\": \"v\"\n  }\n]"));
        var deep = string.Concat(Enumerable.Repeat("{\"a\":", 100)) + "[2.50,1e400,-0]" + new string('}', 100);
        var formatted = ExtendedJsonFormatter.Format(deep);
        Assert.Multiple(() =>
        {
            Assert.That(Regex.Replace(formatted, "\\s", ""), Is.EqualTo(deep));
            Assert.That(formatted, Does.Contain("\n" + new string(' ', 202) + "2.50,\n" + new string(' ', 202) + "1e400,\n"));
            Assert.That(formatted.Split('\n'), Has.Length.EqualTo(205));
        });
    }

    [TestCase("{\"a\":")]
    [TestCase("{\"a\":1}{")]
    [TestCase("")]
    public void InvalidJsonIsReturnedUnchangedWithTheParserMessage(string json)
    {
        Assert.That(ExtendedJsonFormatter.TryFormat(json, out var formatted, out var error), Is.False);
        Assert.That(formatted, Is.EqualTo(json));
        Assert.That(error, Is.Not.Empty);
    }

    [TestCase(UuidRepresentation.Standard)]
    [TestCase(UuidRepresentation.CSharpLegacy)]
    [TestCase(UuidRepresentation.JavaLegacy)]
    [TestCase(UuidRepresentation.GoStandard)]
    public void FormattedDisplayRestoresIdenticalBytesForEveryRepresentation(UuidRepresentation representation)
    {
        var display = UuidCodec.FormatForDisplay(ExtendedJsonFormatter.Format(Typed), representation).Text;
        Assert.That(BsonDocument.Parse(UuidCodec.RewriteConstructors(display)), Is.EqualTo(BsonDocument.Parse(Typed)));
        var expected = representation switch
        {
            UuidRepresentation.CSharpLegacy => "\"csharp\": CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")",
            UuidRepresentation.JavaLegacy => "\"java\": JUUID(\"" + Sample + "\")",
            UuidRepresentation.GoStandard => "\"standard\": GUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")",
            _ => "\"python\": " + Binary("00112233445566778899aabbccddeeff", "03")
        };
        Assert.That(display, Does.Contain(expected));
    }

    private static IEnumerable<TestCaseData> Descriptions()
    {
        var python = Convert.ToBase64String(Convert.FromHexString("00112233445566778899aabbccddeeff"));
        yield return new TestCaseData("_id", UuidRepresentation.Standard, "ObjectId", "ObjectId(\"64b000000000000000000001\")");
        yield return new TestCaseData("int32", UuidRepresentation.Standard, "Int32", "-7");
        yield return new TestCaseData("int64", UuidRepresentation.Standard, "Int64", "9223372036854775807");
        yield return new TestCaseData("double", UuidRepresentation.Standard, "Double", "-0.0");
        yield return new TestCaseData("decimal", UuidRepresentation.Standard, "Decimal128", "1234567890.123456789012345678901234");
        yield return new TestCaseData("date", UuidRepresentation.Standard, "Date", "ISODate(\"2023-11-14T22:13:20.123Z\")");
        yield return new TestCaseData("standard", UuidRepresentation.Standard, "UUID", "UUID(\"" + Sample + "\")");
        yield return new TestCaseData("standard", UuidRepresentation.CSharpLegacy, "UUID", "UUID(\"" + Sample + "\")");
        yield return new TestCaseData("csharp", UuidRepresentation.CSharpLegacy, "UUID C# legacy", "CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")");
        yield return new TestCaseData("java", UuidRepresentation.JavaLegacy, "UUID Java legacy", "JUUID(\"" + Sample + "\")");
        yield return new TestCaseData("python", UuidRepresentation.Standard, "Binary 03 · UUID legado", "origem desconhecida · base64 \"" + python + "\"");
        yield return new TestCaseData("bytes", UuidRepresentation.Standard, "Binary 00", "2 bytes · base64 \"AQI=\"");
        yield return new TestCaseData("texto", UuidRepresentation.Standard, "String", "\"S\\u00e3o \\\"Paulo\\\"\"");
        yield return new TestCaseData("endereco.cidade", UuidRepresentation.Standard, "String", "\"Recife\"");
        yield return new TestCaseData("nulo", UuidRepresentation.Standard, "Null", "null");
        yield return new TestCaseData("vazio", UuidRepresentation.Standard, "Array", "[]");
        yield return new TestCaseData("objetoVazio", UuidRepresentation.Standard, "Objeto", "{}");
        yield return new TestCaseData("itens", UuidRepresentation.Standard, "Array", "2 itens");
        yield return new TestCaseData("regex", UuidRepresentation.Standard, "Regex", "/^a/i");
        yield return new TestCaseData("ts", UuidRepresentation.Standard, "Timestamp", "Timestamp(1700000000, 3)");
        yield return new TestCaseData("min", UuidRepresentation.Standard, "MinKey", "MinKey");
    }

    [TestCaseSource(nameof(Descriptions))]
    public void DescriptionsKeepExactTextForEveryBsonType(string field, UuidRepresentation representation, string type, string display)
    {
        using var document = JsonDocument.Parse(Typed);
        var description = ExtendedJsonValue.Describe(document.RootElement.GetProperty(field), representation);
        Assert.That((description.TypeName, description.Display), Is.EqualTo((type, display)));
    }

    [Test]
    public void ComparerTreatsFieldOrderAsSignificantUnlessRequested()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ExtendedJsonComparer.AreEquivalent("{\"a\":1,\"b\":\"\\u00e9\"}", "{ \"a\": 1,\n \"b\": \"é\" }"), Is.True);
            Assert.That(ExtendedJsonComparer.AreEquivalent("{\"b\":1,\"10\":2}", "{\"10\":2,\"b\":1}"), Is.False);
            Assert.That(ExtendedJsonComparer.AreEquivalent("{\"b\":1,\"10\":2}", "{\"10\":2,\"b\":1}", ignoreObjectOrder: true), Is.True);
            Assert.That(ExtendedJsonComparer.AreEquivalent("[1,2]", "[2,1]", ignoreObjectOrder: true), Is.False);
            Assert.That(ExtendedJsonComparer.AreEquivalent("{\"n\":{\"$numberLong\":\"1\"}}", "{\"n\":{\"$numberInt\":\"1\"}}"), Is.False);
            Assert.That(ExtendedJsonComparer.AreEquivalent("{\"a\":1}", "{\"a\":1.0}"), Is.False);
            Assert.That(ExtendedJsonComparer.AreEquivalent("{\"a\":null}", "{}"), Is.False);
            Assert.That(ExtendedJsonComparer.AreEquivalent("{", "{}"), Is.False);
        });
    }

    [Test]
    public void StructuredConsoleResultsCarryOriginPositionIdentityAndCompleteness()
    {
        var profile = ConnectionProfile.Create("Dev", "mongodb://host");
        var sets = new[]
        {
            new ConsoleResultSet(1, "[]", profile.Id, "loja", "clientes", ["{\"_id\":{\"$oid\":\"64b000000000000000000001\"},\"a\":1}", "{\"a\":2}", "{\"_id\":"], IsTruncated: true) { SourceProfile = profile, Method = "find" },
            new ConsoleResultSet(2, "[]", profile.Id, "loja", "clientes", ["{\"_id\":3}"]) { SourceProfile = profile, Method = "find", IsProjected = true },
            new ConsoleResultSet(3, "[]", profile.Id, "loja", "pedidos", ["{\"_id\":4}"]) { SourceProfile = profile, Method = "aggregate" },
            new ConsoleResultSet(4, "42")
        }.Select(StructuredResultSet.FromConsole).ToArray();
        var documents = sets[0].Documents!;
        Assert.Multiple(() =>
        {
            Assert.That(documents.Select(d => d.Position), Is.EqualTo(Positions));
            Assert.That(documents[0].IdJson, Is.EqualTo("{\"$oid\":\"64b000000000000000000001\"}"));
            Assert.That(documents[0].IdentityFilter, Is.EqualTo("{\"_id\":{\"$oid\":\"64b000000000000000000001\"}}"));
            Assert.That(documents[0].Json, Is.EqualTo("{\"_id\":{\"$oid\":\"64b000000000000000000001\"},\"a\":1}"));
            Assert.That(documents[1].IdJson, Is.Null);
            Assert.That(documents[2].IsValid, Is.False);
            Assert.That(documents[2].InvalidJsonMessage, Is.Not.Empty);
            Assert.That(documents[0].IsTruncated, Is.True);
            Assert.That(sets[0].Completeness, Is.EqualTo(ResultCompleteness.Complete));
            Assert.That(sets[0].Origin.Destination, Is.EqualTo("Dev › loja › clientes"));
            Assert.That(sets[0].Origin.Source, Does.Contain("[1]").And.Contain("find"));
            Assert.That(sets[0].Label, Is.EqualTo("[1] Dev › loja › clientes"));
            Assert.That(sets[1].Documents![0].IsPartialProjection, Is.True);
            Assert.That(sets[2].Completeness, Is.EqualTo(ResultCompleteness.Derived));
            Assert.That(sets[3].Documents, Is.Null);
            Assert.That(sets[3].Label, Is.EqualTo("[4] valor"));
            Assert.That(sets[3].Origin.HasCollection, Is.False);
        });
    }
}
