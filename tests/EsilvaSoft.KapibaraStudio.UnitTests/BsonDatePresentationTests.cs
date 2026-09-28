using System.Text.Json;
using Jint;
using EsilvaSoft.KapibaraStudio.Core;
using MongoDB.Bson;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class BsonDatePresentationTests
{
    [Test]
    public void MongoshHelperCreatesNativeUtcDateAndKeepsStaticMethods()
    {
        using var engine = new Jint.Engine();
        engine.Execute(EsilvaSoft.KapibaraStudio.Infrastructure.MongoshScriptTemplate.DateHelpers);
        Assert.That(engine.Evaluate("Date('2024-12-30 20:56:44.999').toISOString()").AsString(), Is.EqualTo("2024-12-30T20:56:44.999Z"));
        Assert.That(engine.Evaluate("new Date('2024-12-30 20:56:44.999') instanceof globalThis.Date").AsBoolean(), Is.True);
        Assert.That(engine.Evaluate("Date.UTC(1970, 0, 1)").AsNumber(), Is.Zero);
        Assert.Throws<Jint.Runtime.JavaScriptException>(() => engine.Evaluate("Date('2024-02-30 20:56:44.999')"));
    }

    [TestCase("2024-12-30 20:56:44.999")]
    [TestCase("2024-12-30T20:56:44.999Z")]
    [TestCase("2024-12-30T17:56:44.999-03:00")]
    public void DateInputsAndNestedDisplayPreserveBsonMilliseconds(string input)
    {
        var canonical = IdentifierRepresentationService.RewriteConstructors("{\"items\":[{\"at\":Date(\"" + input + "\")}]}");
        var expected = new BsonDocument("items", new BsonArray { new BsonDocument("at", new BsonDateTime(new DateTime(2024, 12, 30, 20, 56, 44, 999, DateTimeKind.Utc))) });
        Assert.That(BsonDocument.Parse(canonical).ToBson(), Is.EqualTo(expected.ToBson()));
        var display = IdentifierRepresentationService.FormatForDisplay(canonical, new(IdentifierRepresentationMode.Standard, UuidRepresentation.Standard)).Text;
        Assert.That(display, Does.Contain("ISODate(\"2024-12-30T20:56:44.999Z\")"));
        Assert.That(BsonDocument.Parse(IdentifierRepresentationService.RewriteConstructors(display)).ToBson(), Is.EqualTo(expected.ToBson()));
        Assert.That(BsonDocument.Parse(display).ToBson(), Is.EqualTo(expected.ToBson()), "A saída deve ser legível diretamente pelo driver, sem helper da IDE.");
    }

    [TestCase("2024-12-30T17:56:44.999-03:00")]
    [TestCase("2024-12-31T02:26:44.999+05:30")]
    [TestCase("2024-12-30T20:56:44.999Z")]
    public void RelaxedDateOffsetsShowTheEquivalentInstantWithExplicitUtc(string input)
    {
        var json = "{\"$date\":\"" + input + "\"}";
        using var document = JsonDocument.Parse(json);
        const string expected = "ISODate(\"2024-12-30T20:56:44.999Z\")";
        Assert.That(ExtendedJsonValue.Describe(document.RootElement, UuidRepresentation.Standard).Display, Is.EqualTo(expected));
        Assert.That(IdentifierRepresentationService.FormatForDisplay(json, new(IdentifierRepresentationMode.Standard, UuidRepresentation.Standard)).Text, Is.EqualTo(expected));
    }

    [TestCase("-1", "ISODate(\"1969-12-31T23:59:59.999Z\")")]
    [TestCase("0", "ISODate(\"1970-01-01T00:00:00.000Z\")")]
    public void TreeAndTextUseTheSameUtcLiteral(string milliseconds, string expected)
    {
        var json = "{\"$date\":{\"$numberLong\":\"" + milliseconds + "\"}}";
        using var document = JsonDocument.Parse(json);
        Assert.That(ExtendedJsonValue.Describe(document.RootElement, UuidRepresentation.Standard).Display, Is.EqualTo(expected));
        Assert.That(IdentifierRepresentationService.FormatForDisplay(json, new(IdentifierRepresentationMode.Standard, UuidRepresentation.Standard)).Text, Is.EqualTo(expected));
    }

    [Test]
    public void OutsideDotNetRangeAndLookalikeStringsStayUnchanged()
    {
        const string json = """{"at":{"$date":{"$numberLong":"9223372036854775807"}},"text":"Date(\"2024-12-30 20:56:44.999\")"}""";
        Assert.That(IdentifierRepresentationService.FormatForDisplay(json, new(IdentifierRepresentationMode.Standard, UuidRepresentation.Standard)).Text, Is.EqualTo(json));
        Assert.That(IdentifierRepresentationService.RewriteConstructors(json), Is.EqualTo(json));
    }

    [TestCase("2024-02-30 20:56:44.999")]
    [TestCase("not a date")]
    public void InvalidDatesAreRejected(string input) =>
        Assert.Throws<FormatException>(() => IdentifierRepresentationService.RewriteConstructors("Date(\"" + input + "\")"));
}
