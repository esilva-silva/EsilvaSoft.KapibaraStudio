using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AuditJsonSerializerTests
{
    [Test]
    public void SerializeProducesIndentedSafeMetadataArray()
    {
        var entry = AuditEntry.Create("user.create", Guid.NewGuid(), "catalogo", null, "Usuário criado.");

        var json = AuditJsonSerializer.Serialize(new[] { entry });
        using var document = JsonDocument.Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(document.RootElement.GetArrayLength(), Is.EqualTo(1));
            Assert.That(json, Does.Contain(Environment.NewLine));
            Assert.That(json, Does.Not.Contain("mongodb://"));
        });
    }

    [Test]
    public void SerializeRejectsMoreThanFiveHundredEntries()
    {
        var entries = Enumerable.Range(0, 501)
            .Select(_ => AuditEntry.Create("diagnostic", null, null, null, "Consulta."))
            .ToArray();

        Assert.That(() => AuditJsonSerializer.Serialize(entries), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
