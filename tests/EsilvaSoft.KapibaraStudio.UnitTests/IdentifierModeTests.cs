using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;
using static EsilvaSoft.KapibaraStudio.UnitTests.IdentifierModeTestFixture;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class IdentifierModeTests
{
    [Test]
    public void ModesAreThreeNamedValuesAndStandardMeansObjectIdPlusUuid()
    {
        Assert.Multiple(() =>
        {
            Assert.That(IdentifierRepresentationService.All, Is.EqualTo(ModeNames));
            Assert.That(Enum.GetNames<IdentifierRepresentationMode>(), Has.None.Contains("StandardUuid"));
            Assert.That(JsonSerializer.Serialize(IdentifierRepresentationMode.UuidV4), Is.EqualTo("\"UuidV4\""));
            Assert.That(IdentifierRepresentationService.ChoiceLabel(IdentifierRepresentationMode.Standard), Is.EqualTo("Standard · ObjectId + UUID v4"));
            Assert.That(IdentifierRepresentationService.ChoiceLabel(IdentifierRepresentationMode.ObjectId), Is.EqualTo("ObjectId · MongoDB ObjectId"));
            Assert.That(IdentifierRepresentationService.ChoiceLabel(IdentifierRepresentationMode.UuidV4), Is.EqualTo("UUID v4 · BSON subtype 4"));
            Assert.That(IdentifierRepresentationService.Description(IdentifierRepresentationMode.Standard), Does.StartWith("Aceita ObjectId e UUID v4.").And.Contain("preserva o tipo BSON original"));
            Assert.That(IdentifierRepresentationService.Description(IdentifierRepresentationMode.ObjectId), Does.StartWith("Prioriza MongoDB ObjectId"));
            Assert.That(IdentifierRepresentationService.Description(IdentifierRepresentationMode.UuidV4), Does.StartWith("Prioriza UUID padrão BSON subtype 4.").And.Contain("ObjectIds podem ser representados como UUID"));
            Assert.Throws<ArgumentOutOfRangeException>(() => IdentifierRepresentationService.DisplayName((IdentifierRepresentationMode)9));
        });
    }

    [Test]
    public void ObjectIdToUuidIsDeterministicReversibleAndMatchesIndependentBytes()
    {
        var uuid = IdentifierRepresentationService.ObjectIdToUuid(Hex);
        Assert.Multiple(() =>
        {
            Assert.That(uuid.ToString("D"), Is.EqualTo(Equivalent));
            Assert.That(Convert.ToHexStringLower(uuid.ToByteArray(bigEndian: true)), Is.EqualTo(Hex + "00000000"));
            Assert.That(IdentifierRepresentationService.ObjectIdToUuid(Hex.ToUpperInvariant()), Is.EqualTo(uuid), "Maiúsculas não mudam os bytes.");
            Assert.That(IdentifierRepresentationService.ObjectIdToUuid(Hex), Is.EqualTo(uuid), "Mesmo ObjectId, mesmo UUID.");
            Assert.That(IdentifierRepresentationService.TryUuidToObjectId(uuid, out var back), Is.True);
            Assert.That(back, Is.EqualTo(Hex));
            Assert.That(IdentifierRepresentationService.TryUuidToObjectId(Guid.Parse(Sample), out _), Is.False);
            Assert.That(IdentifierRepresentationService.ObjectIdToUuid("000000000000000000000000"), Is.EqualTo(Guid.Empty));
            Assert.Throws<FormatException>(() => IdentifierRepresentationService.ObjectIdToUuid("66e3bd5b3bc3f54c840d73a"));
            Assert.Throws<FormatException>(() => IdentifierRepresentationService.ObjectIdToUuid("zze3bd5b3bc3f54c840d73ac"));
        });
    }

    [TestCase("_id", UuidRepresentation.Standard, IdentifierKind.ObjectId)]
    [TestCase("uuid", UuidRepresentation.Standard, IdentifierKind.Uuid)]
    [TestCase("legacy", UuidRepresentation.Standard, IdentifierKind.UnknownLegacyUuid)]
    [TestCase("legacy", UuidRepresentation.CSharpLegacy, IdentifierKind.Uuid)]
    [TestCase("texto", UuidRepresentation.Standard, IdentifierKind.None)]
    [TestCase("textoUuid", UuidRepresentation.Standard, IdentifierKind.None)]
    public void BsonTypeHasPriorityAndLookalikeStringsStayStrings(string field, UuidRepresentation uuid, IdentifierKind expected)
    {
        using var document = JsonDocument.Parse(Mixed);
        Assert.That(IdentifierRepresentationService.DetectType(document.RootElement.GetProperty(field), uuid), Is.EqualTo(expected));
    }

    [TestCase(Hex, IdentifierKind.ObjectId)]
    [TestCase(Sample, IdentifierKind.Uuid)]
    [TestCase("00112233445566778899aabbccddeeff", IdentifierKind.Uuid)]
    [TestCase("ObjectId(\"" + Hex + "\")", IdentifierKind.ObjectId)]
    [TestCase("GUUID(\"" + Sample + "\")", IdentifierKind.Uuid)]
    [TestCase("66e3bd5b3bc3f54c840d73a", IdentifierKind.None)]
    [TestCase("texto", IdentifierKind.None)]
    public void TextualDetectionIsUsedOnlyWithoutBsonValues(string text, IdentifierKind expected)
    {
        foreach (var mode in ModeNames)
            Assert.That(IdentifierRepresentationService.DetectType(text, Options(mode)), Is.EqualTo(expected), mode.ToString());
    }

    private static IEnumerable<TestCaseData> ParsedForms() =>
        ParsedFormCases().Select(data => data.SetArgDisplayNames((string)data.Arguments[0]!, data.Arguments[1]!.ToString()!));

    private static IEnumerable<TestCaseData> ParsedFormCases()
    {
        var objectId = new BsonObjectId(ObjectId.Parse(Hex));
        var standard = new BsonBinaryData(Guid.Parse(Sample), GuidRepresentation.Standard);
        var csharp = new BsonBinaryData(Guid.Parse(Sample), GuidRepresentation.CSharpLegacy);
        yield return new TestCaseData("ObjectId(\"" + Hex.ToUpperInvariant() + "\")", UuidRepresentation.Standard, IdentifierKind.ObjectId, true, "ObjectId(\"" + Hex + "\")", objectId);
        yield return new TestCaseData(" " + Hex + " ", UuidRepresentation.Standard, IdentifierKind.ObjectId, false, "ObjectId(\"" + Hex + "\")", objectId);
        yield return new TestCaseData("new ObjectId('" + Hex + "')", UuidRepresentation.Standard, IdentifierKind.ObjectId, true, "ObjectId(\"" + Hex + "\")", objectId);
        yield return new TestCaseData("{\"$oid\":\"" + Hex + "\"}", UuidRepresentation.JavaLegacy, IdentifierKind.ObjectId, true, "ObjectId(\"" + Hex + "\")", objectId);
        yield return new TestCaseData("UUID(\"" + Sample + "\")", UuidRepresentation.CSharpLegacy, IdentifierKind.Uuid, true, "UUID(\"" + Sample + "\")", standard);
        yield return new TestCaseData(Sample, UuidRepresentation.Standard, IdentifierKind.Uuid, false, "UUID(\"" + Sample + "\")", standard);
        yield return new TestCaseData(Sample.ToUpperInvariant().Replace("-", "", StringComparison.Ordinal), UuidRepresentation.CSharpLegacy, IdentifierKind.Uuid, false, "CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")", csharp);
        yield return new TestCaseData(Binary("33221100554477668899aabbccddeeff", "03"), UuidRepresentation.CSharpLegacy, IdentifierKind.Uuid, true, "CGUUID(\"00112233-4455-6677-8899-AABBCCDDEEFF\")", csharp);
    }

    [TestCaseSource(nameof(ParsedForms))]
    public void ParseIdentifierAcceptsRequiredFormsInEveryModeWithDriverIdenticalBson(string text, UuidRepresentation uuid, IdentifierKind kind, bool isExplicit, string display, BsonValue oracle)
    {
        foreach (var mode in ModeNames)
        {
            var value = IdentifierRepresentationService.ParseIdentifier(text, Options(mode, uuid));
            Assert.Multiple(() =>
            {
                Assert.That((value.Kind, value.IsExplicitType, value.Text), Is.EqualTo((kind, isExplicit, display)), mode.ToString());
                Assert.That(BsonDocument.Parse("{\"v\":" + value.ExtendedJson + "}")["v"], Is.EqualTo(oracle));
                Assert.That(BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors("{\"v\":" + value.Text + "}"))["v"], Is.EqualTo(oracle));
                Assert.That(value.UuidEquivalent, kind == IdentifierKind.ObjectId ? Is.EqualTo(Equivalent) : Is.Null);
            });
        }
    }

    [TestCase("", "não é um identificador reconhecido")]
    [TestCase("66e3bd5b3bc3f54c840d73a", "não é um identificador reconhecido")]
    [TestCase("66e3bd5b3bc3f54c840d73acd", "não é um identificador reconhecido")]
    [TestCase("zze3bd5b3bc3f54c840d73ac", "não é um identificador reconhecido")]
    [TestCase("\"66e3bd5b3bc3f54c840d73ac\"", "não é um identificador reconhecido")]
    [TestCase("{\"a\":1}", "não é um identificador reconhecido")]
    [TestCase("{\"$oid\":\"123\"}", "não é um identificador reconhecido")]
    [TestCase("ObjectId(\"123\")", "ObjectId(\"123\") não contém um ObjectId válido. Use 24 dígitos hexadecimais. (linha 1, coluna 1)")]
    [TestCase("ObjectId(123)", "ObjectId(...) exige um único texto entre aspas")]
    [TestCase("UUID(\"123\")", "UUID(\"123\") não contém um UUID válido")]
    [TestCase("ObjectId(\"" + Hex + "\") extra", "não é um identificador reconhecido")]
    public void ParseIdentifierRejectsInvalidValuesWithVisibleReason(string text, string message)
    {
        var exception = Assert.Throws<FormatException>(() => IdentifierRepresentationService.ParseIdentifier(text, IdentifierDisplayOptions.Default));
        Assert.That(exception!.Message, Does.Contain(message));
        Assert.That(IdentifierRepresentationService.TryParseIdentifier(text, IdentifierDisplayOptions.Default, out _), Is.False);
    }

    [TestCase(IdentifierRepresentationMode.Standard, UuidRepresentation.Standard, 1)]
    [TestCase(IdentifierRepresentationMode.ObjectId, UuidRepresentation.CSharpLegacy, 0)]
    [TestCase(IdentifierRepresentationMode.UuidV4, UuidRepresentation.JavaLegacy, 0)]
    public void HumanJsonUsesConstructorsForBothTypesAndRestoresIdenticalBson(IdentifierRepresentationMode mode, UuidRepresentation uuid, int unknown)
    {
        var display = IdentifierRepresentationService.FormatForDisplay(ExtendedJsonFormatter.Format(Mixed), Options(mode, uuid));
        Assert.Multiple(() =>
        {
            Assert.That(display.Text, Does.Contain("\"_id\": ObjectId(\"" + Hex + "\"),"));
            Assert.That(display.Text, Does.Contain("\"texto\": \"" + Hex + "\"").And.Contain("\"textoUuid\": \"" + Sample + "\""), "Strings parecidas continuam strings.");
            Assert.That(display.Text, Does.Not.Contain(Equivalent), "O JSON exibido continua reinterpretável; a alternativa só aparece na árvore.");
            Assert.That(display.UnknownLegacyCount, Is.EqualTo(unknown));
            Assert.That(BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors(display.Text)), Is.EqualTo(BsonDocument.Parse(Mixed)));
            Assert.That(UuidCodec.FormatForDisplay(Mixed, uuid).Text, Does.Contain("{\"$oid\":\"" + Hex + "\"}"), "O codec UUID continua limitado a UUIDs.");
        });
    }

    [Test]
    public void RewriteLeavesStringsCommentsAndMemberAccessUntouched()
    {
        const string text = "{\"a\":\"ObjectId(\\\"bad\\\")\", /* ObjectId(\"bad\") */ \"b\": x.ObjectId(\"bad\"), // ObjectId(\"bad\")\n \"c\": 'ObjectId(\"bad\")'}";
        Assert.That(IdentifierRepresentationService.RewriteConstructors(text), Is.EqualTo(text));
        Assert.That(IdentifierRepresentationService.RewriteConstructors("[anew ObjectId(\"" + Hex + "\")]"), Is.EqualTo("[anew {\"$oid\":\"" + Hex + "\"}]"));
        Assert.That(UuidCodec.RewriteConstructors("{\"_id\":ObjectId(\"bad\")}"), Is.EqualTo("{\"_id\":ObjectId(\"bad\")}"), "O codec UUID não interpreta ObjectId.");
    }

    [Test]
    public void TreeDescriptionsKeepTheStoredTypeAndOnlyUuidV4AppendsTheAlternative()
    {
        using var document = JsonDocument.Parse(Mixed);
        var id = document.RootElement.GetProperty("_id");
        var uuid = document.RootElement.GetProperty("uuid");
        foreach (var mode in ModeNames)
        {
            var description = ExtendedJsonValue.Describe(id, Options(mode));
            var expected = "ObjectId(\"" + Hex + "\")" + (mode == IdentifierRepresentationMode.UuidV4 ? " · UUID " + Equivalent : "");
            Assert.Multiple(() =>
            {
                Assert.That((description.TypeName, description.Display), Is.EqualTo(("ObjectId", expected)), mode.ToString());
                Assert.That(ExtendedJsonValue.Describe(uuid, Options(mode)).Display, Is.EqualTo("UUID(\"" + Sample + "\")"));
            });
        }
    }

    [TestCase(IdentifierRepresentationMode.Standard, new[] { IdentifierKind.ObjectId, IdentifierKind.Uuid })]
    [TestCase(IdentifierRepresentationMode.ObjectId, new[] { IdentifierKind.ObjectId })]
    [TestCase(IdentifierRepresentationMode.UuidV4, new[] { IdentifierKind.Uuid })]
    public void GenerationFollowsTheModeWithValidObjectIdsAndUuidV4(IdentifierRepresentationMode mode, IdentifierKind[] kinds)
    {
        var generated = IdentifierRepresentationService.Generate(Options(mode, UuidRepresentation.GoStandard));
        Assert.That(generated.Select(value => value.Kind), Is.EqualTo(kinds));
        foreach (var value in generated)
        {
            Assert.That(IdentifierRepresentationService.RewriteConstructors(value.Script), Is.EqualTo(value.ExtendedJson));
            var parsed = IdentifierRepresentationService.ParseIdentifier(value.Script, Options(mode, UuidRepresentation.GoStandard));
            if (value.Kind == IdentifierKind.ObjectId)
            {
                var objectId = BsonDocument.Parse("{\"v\":" + value.ExtendedJson + "}")["v"].AsObjectId;
                Assert.That(objectId.CreationTime, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(2)));
            }
            else
            {
                var guid = Guid.Parse(value.Script["GUUID(\"".Length..^2]);
                Assert.That((guid.Version, guid.Variant >> 2), Is.EqualTo((4, 2)), "UUID v4 com variante RFC 4122.");
                Assert.That(value.ExtendedJson, Does.Contain("\"subType\":\"04\""));
            }
            Assert.That(parsed.ExtendedJson, Is.EqualTo(value.ExtendedJson));
        }
        Assert.That(IdentifierRepresentationService.NewObjectId(), Is.Not.EqualTo(IdentifierRepresentationService.NewObjectId()));
        Assert.That(IdentifierRepresentationService.NewObjectId(DateTimeOffset.FromUnixTimeSeconds(0x66e3bd5b))[..8], Is.EqualTo("66e3bd5b"));
    }

    [Test]
    public void CrudScriptPlaceholdersFollowModeAndUuidRepresentation()
    {
        var objectId = ExplorerScripts.Create(ExplorerScriptOperation.Delete, "db", "col", identifiers: Options(IdentifierRepresentationMode.ObjectId, UuidRepresentation.CSharpLegacy));
        var uuid = ExplorerScripts.Create(ExplorerScriptOperation.Update, "db", "col", identifiers: Options(IdentifierRepresentationMode.UuidV4, UuidRepresentation.CSharpLegacy));
        var standard = ExplorerScripts.Create(ExplorerScriptOperation.Delete, "db", "col", identifiers: Options(IdentifierRepresentationMode.Standard, UuidRepresentation.JavaLegacy));
        Assert.Multiple(() =>
        {
            Assert.That(objectId, Does.Contain(".deleteMany({ _id: ObjectId(\"000000000000000000000000\") });").And.Not.Contain("UUID"));
            Assert.That(uuid, Does.Contain(".updateMany({ _id: CGUUID(\"00000000-0000-4000-8000-000000000000\") }, ").And.Not.Contain("ObjectId"));
            Assert.That(standard, Does.Contain("_id: ObjectId(\"000000000000000000000000\") /* ou JUUID(\"00000000-0000-4000-8000-000000000000\") */ }"));
            Assert.That(ExplorerScripts.Create(ExplorerScriptOperation.Delete, "db", "col"), Does.Contain("_id: ObjectId(\"000000000000000000000000\") /* ou UUID("));
            Assert.That(BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors("{\"_id\":" + IdentifierRepresentationService.ScriptIdentifierPlaceholder(Options(IdentifierRepresentationMode.UuidV4, UuidRepresentation.CSharpLegacy)) + "}"))["_id"].AsBsonBinaryData.SubType,
                Is.EqualTo(BsonBinarySubType.UuidLegacy));
            Assert.That(IdentifierRepresentationService.PlaceholderUuid.Version, Is.EqualTo(4));
        });
    }

    [Test]
    public void PolicySnapshotCarriesTheGlobalModeSeparatelyFromUuidOverrides()
    {
        var profile = Guid.NewGuid();
        var policy = new UuidDisplayPolicy(UuidRepresentation.JavaLegacy, new Dictionary<Guid, UuidRepresentation> { [profile] = UuidRepresentation.CSharpLegacy }, IdentifierRepresentationMode.UuidV4);
        Assert.Multiple(() =>
        {
            Assert.That(policy.ResolveOptions(profile), Is.EqualTo(Options(IdentifierRepresentationMode.UuidV4, UuidRepresentation.CSharpLegacy)));
            Assert.That(policy.ResolveOptions(null), Is.EqualTo(Options(IdentifierRepresentationMode.UuidV4, UuidRepresentation.JavaLegacy)));
            Assert.That(UuidDisplayPolicy.Default.Mode, Is.EqualTo(IdentifierRepresentationMode.Standard));
            Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UuidDisplayPolicy(UuidRepresentation.Standard, null, (IdentifierRepresentationMode)7));
        });
    }
}
