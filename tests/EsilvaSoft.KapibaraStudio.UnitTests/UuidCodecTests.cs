using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class UuidCodecTests
{
    private const string Sample = "00112233-4455-6677-8899-aabbccddeeff";
    private static readonly JsonWriterSettings Canonical = new() { OutputMode = JsonOutputMode.CanonicalExtendedJson };

    // Independent byte fixtures from the MongoDB GUID serialization examples; none of them is produced by UuidCodec.
    private static readonly object[] Fixtures =
    [
        new object[] { UuidRepresentation.Standard, "UUID", "00112233445566778899aabbccddeeff", (byte)4, "00112233-4455-6677-8899-aabbccddeeff" },
        new object[] { UuidRepresentation.CSharpLegacy, "CGUUID", "33221100554477668899aabbccddeeff", (byte)3, "00112233-4455-6677-8899-AABBCCDDEEFF" },
        new object[] { UuidRepresentation.JavaLegacy, "JUUID", "7766554433221100ffeeddccbbaa9988", (byte)3, "00112233-4455-6677-8899-aabbccddeeff" },
        new object[] { UuidRepresentation.GoStandard, "GUUID", "00112233445566778899aabbccddeeff", (byte)4, "00112233-4455-6677-8899-AABBCCDDEEFF" }
    ];

    private static string Binary(string hex, string subType) =>
        "{\"$binary\":{\"base64\":\"" + Convert.ToBase64String(Convert.FromHexString(hex)) + "\",\"subType\":\"" + subType + "\"}}";

    [TestCaseSource(nameof(Fixtures))]
    public void ConstructorProducesFixtureBytesAndDisplaysWithItsCapitalization(UuidRepresentation representation, string constructor, string hex, byte subType, string text)
    {
        var filter = BsonDocument.Parse(UuidCodec.RewriteConstructors($"{{ \"customerId\": {constructor}(\"{Sample.ToUpperInvariant()}\") }}"));
        var binary = filter["customerId"].AsBsonBinaryData;
        var display = UuidCodec.FormatForDisplay(filter.ToJson(Canonical), representation);
        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexStringLower(binary.Bytes), Is.EqualTo(hex));
            Assert.That((byte)binary.SubType, Is.EqualTo(subType));
            Assert.That(display.Text, Does.Contain($"{constructor}(\"{text}\")").And.Not.Contain("$binary"));
            Assert.That(display.UnknownLegacyCount, Is.Zero);
        });
    }

    [TestCase(UuidRepresentation.Standard, GuidRepresentation.Standard)]
    [TestCase(UuidRepresentation.CSharpLegacy, GuidRepresentation.CSharpLegacy)]
    [TestCase(UuidRepresentation.JavaLegacy, GuidRepresentation.JavaLegacy)]
    [TestCase(UuidRepresentation.GoStandard, GuidRepresentation.Standard)]
    public void CodecAgreesWithOfficialDriverRepresentation(UuidRepresentation representation, GuidRepresentation driver)
    {
        var value = Guid.Parse(Sample);
        var expected = new BsonBinaryData(value, driver);
        Assert.That(UuidCodec.ToBytes(value, representation), Is.EqualTo(expected.Bytes));
        Assert.That(UuidCodec.SubType(representation), Is.EqualTo((byte)expected.SubType));
    }

    [TestCase("00000000-0000-0000-0000-000000000000")]
    [TestCase("00000000000000000000000000000000")]
    [TestCase("AbCdEf01-2345-6789-aBcD-ef0123456789")]
    [TestCase("abcdef0123456789ABCDEF0123456789")]
    public void AcceptedTextFormsRoundTripThroughBsonForEveryRepresentation(string text)
    {
        var expected = Guid.Parse(text);
        foreach (var representation in UuidCodec.All)
        {
            var json = UuidCodec.RewriteConstructors($"[{UuidCodec.Constructor(representation)}('{text}')]");
            var binary = BsonSerializer.Deserialize<BsonArray>(json)[0].AsBsonBinaryData;
            var display = UuidCodec.FormatForDisplay(json, representation).Text;
            Assert.That(UuidCodec.FromBytes(binary.Bytes, representation), Is.EqualTo(expected), representation.ToString());
            Assert.That(display, Is.EqualTo("[" + UuidCodec.FormatConstructor(expected, representation) + "]"));
            Assert.That(UuidCodec.RewriteConstructors(display), Is.EqualTo(json), "A saída humana volta aos mesmos bytes.");
        }
    }

    [TestCase("{ \"a\": CGUUID(\"xyz\") }", "CGUUID(\"xyz\") não contém um UUID válido")]
    [TestCase("{ \"a\": UUID(\"0011\") }", "UUID(\"0011\") não contém um UUID válido")]
    [TestCase("{ \"a\": GUUID(\"{00112233-4455-6677-8899-aabbccddeeff}\") }", "não contém um UUID válido")]
    [TestCase("{ \"a\": JUUID(123) }", "JUUID(...) exige um único texto")]
    [TestCase("{ \"a\": UUID() }", "UUID(...) exige um único texto")]
    [TestCase("{ \"a\": JUUID(\"00112233-4455-6677-8899-aabbccddeeff\" }", "JUUID(...) exige um único texto")]
    [TestCase("{\n  \"a\": UUID(\"00112233-4455-6677-8899-aabbccddeefg\") }", "(linha 2, coluna 8)")]
    public void InvalidConstructorIsRejectedWithItsLocation(string text, string message) =>
        Assert.That(Assert.Throws<FormatException>(() => UuidCodec.RewriteConstructors(text))!.Message, Does.Contain(message));

    [Test]
    public void UuidLookingStringsRegexCommentsAndMemberAccessRemainUnchanged()
    {
        const string json = """{ "a": "UUID(\"00112233-4455-6677-8899-aabbccddeeff\")", "b": 'CGUUID("00112233445566778899aabbccddeeff")', "c": "00112233-4455-6677-8899-aabbccddeeff", "r": /JUUID\("x"\)/ }""";
        Assert.That(UuidCodec.RewriteConstructors(json), Is.SameAs(json));
        Assert.That(UuidCodec.RewriteConstructors("// GUUID(\"bad\")\n/* CGUUID('bad') */ x.UUID(\"bad\")"), Is.EqualTo("// GUUID(\"bad\")\n/* CGUUID('bad') */ x.UUID(\"bad\")"));
        var document = BsonDocument.Parse(json);
        Assert.Multiple(() =>
        {
            Assert.That(document["a"].BsonType, Is.EqualTo(BsonType.String));
            Assert.That(document["b"].BsonType, Is.EqualTo(BsonType.String));
            Assert.That(document["c"].AsString, Is.EqualTo(Sample));
            Assert.That(document["r"].BsonType, Is.EqualTo(BsonType.RegularExpression));
        });
        const string quoted = """{"text":"{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\"}}"}""";
        Assert.That(UuidCodec.FormatForDisplay(quoted, UuidRepresentation.GoStandard), Is.EqualTo(new UuidDisplayText(quoted, 0)));
    }

    [Test]
    public void SubtypeThreeAndFourAreNeverConfused()
    {
        var standard = Binary("00112233445566778899aabbccddeeff", "04");
        var legacyWithStandardOrder = Binary("00112233445566778899aabbccddeeff", "03");
        Assert.Multiple(() =>
        {
            Assert.That(UuidCodec.FormatForDisplay(standard, UuidRepresentation.CSharpLegacy).Text, Is.EqualTo($"UUID(\"{Sample}\")"));
            Assert.That(UuidCodec.FormatForDisplay(standard, UuidRepresentation.JavaLegacy).Text, Is.EqualTo($"UUID(\"{Sample}\")"));
            Assert.That(UuidCodec.FormatForDisplay(standard, UuidRepresentation.GoStandard).Text, Is.EqualTo($"GUUID(\"{Sample.ToUpperInvariant()}\")"));
            Assert.That(UuidCodec.FormatForDisplay(legacyWithStandardOrder, UuidRepresentation.Standard), Is.EqualTo(new UuidDisplayText(legacyWithStandardOrder, 1)));
            Assert.That(UuidCodec.FormatForDisplay(legacyWithStandardOrder, UuidRepresentation.GoStandard), Is.EqualTo(new UuidDisplayText(legacyWithStandardOrder, 1)));
            Assert.That(UuidCodec.FormatForDisplay(legacyWithStandardOrder, UuidRepresentation.CSharpLegacy).Text, Is.EqualTo("CGUUID(\"33221100-5544-7766-8899-AABBCCDDEEFF\")"));
            Assert.That(BsonDocument.Parse("{\"v\":" + UuidCodec.RewriteConstructors($"UUID(\"{Sample}\")") + "}")["v"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidStandard));
            Assert.That(BsonDocument.Parse("{\"v\":" + UuidCodec.RewriteConstructors($"JUUID(\"{Sample}\")") + "}")["v"].AsBsonBinaryData.SubType, Is.EqualTo(BsonBinarySubType.UuidLegacy));
        });
    }

    [TestCase("{\"$binary\":{\"base64\":\"AQID\",\"subType\":\"04\"}}")]
    [TestCase("{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"00\"}}")]
    [TestCase("{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\"},\"extra\":1}")]
    [TestCase("{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\",\"extra\":1}}")]
    [TestCase("not json {\"$binary\"")]
    public void NonUuidOrInvalidJsonStaysUntouched(string json) =>
        Assert.That(UuidCodec.FormatForDisplay(json, UuidRepresentation.JavaLegacy), Is.EqualTo(new UuidDisplayText(json, 0)));

    [Test]
    public void MixedCollectionShowsEachStoredByteOrderAndRequiresExplicitProfileForLegacy()
    {
        var json = "{ \"_id\" : { \"$oid\" : \"64b000000000000000000001\" }, \"s\" : " + Binary("00112233445566778899aabbccddeeff", "04")
            + ", \"items\" : [" + Binary("33221100554477668899aabbccddeeff", "03") + ", " + Binary("7766554433221100ffeeddccbbaa9988", "03")
            + ", { \"n\" : { \"$numberLong\" : \"9223372036854775807\" }, \"texto\" : \"ação UUID(\\\"x\\\")\" }] }";
        var csharp = UuidCodec.FormatForDisplay(json, UuidRepresentation.CSharpLegacy);
        var java = UuidCodec.FormatForDisplay(json, UuidRepresentation.JavaLegacy);
        Assert.Multiple(() =>
        {
            Assert.That(csharp.Text, Is.EqualTo("{ \"_id\" : { \"$oid\" : \"64b000000000000000000001\" }, \"s\" : UUID(\"00112233-4455-6677-8899-aabbccddeeff\"), \"items\" : [CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\"), CGUUID(\"44556677-2233-0011-FFEE-DDCCBBAA9988\"), { \"n\" : { \"$numberLong\" : \"9223372036854775807\" }, \"texto\" : \"ação UUID(\\\"x\\\")\" }] }"));
            // C# legacy bytes 33 22 11 00 55 44 77 66 | 88..ff read as Java reverse each half: 66 77 44 55 00 11 22 33 | ff..88.
            Assert.That(java.Text, Does.Contain("JUUID(\"66774455-0011-2233-ffee-ddccbbaa9988\"), JUUID(\"00112233-4455-6677-8899-aabbccddeeff\")"));
            Assert.That(UuidCodec.FormatForDisplay(json, UuidRepresentation.Standard).UnknownLegacyCount, Is.EqualTo(2));
            Assert.That(BsonDocument.Parse(UuidCodec.RewriteConstructors(csharp.Text)), Is.EqualTo(BsonDocument.Parse(json)));
            Assert.That(BsonDocument.Parse(UuidCodec.RewriteConstructors(java.Text)), Is.EqualTo(BsonDocument.Parse(json)));
        });
    }

    [Test]
    public void PolicyResolvesOverridesFromAnImmutableSnapshot()
    {
        var profile = Guid.NewGuid();
        var overrides = new Dictionary<Guid, UuidRepresentation> { [profile] = UuidRepresentation.JavaLegacy };
        var policy = new UuidDisplayPolicy(UuidRepresentation.GoStandard, overrides);
        overrides[profile] = UuidRepresentation.CSharpLegacy;
        Assert.Multiple(() =>
        {
            Assert.That(policy.Resolve(profile), Is.EqualTo(UuidRepresentation.JavaLegacy));
            Assert.That(policy.Resolve(Guid.NewGuid()), Is.EqualTo(UuidRepresentation.GoStandard));
            Assert.That(policy.Resolve(null), Is.EqualTo(UuidRepresentation.GoStandard));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UuidDisplayPolicy((UuidRepresentation)9, null));
        });
    }

    [Test]
    public void StandardExtendedJsonKeepsPreviousCanonicalFixture()
    {
        using var document = JsonDocument.Parse(UuidCodec.ToExtendedJson(Guid.Parse(Sample), UuidRepresentation.Standard));
        Assert.That(document.RootElement.GetProperty("$binary").GetProperty("subType").GetString(), Is.EqualTo("04"));
        Assert.That(document.RootElement.GetProperty("$binary").GetProperty("base64").GetString(), Is.EqualTo("ABEiM0RVZneImaq7zN3u/w=="));
    }
}
