using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class QueryResultExportSerializerTests
{
    [Test]
    public void SerializeCreatesJsonArrayFromExtendedJsonDocuments()
    {
        var result = QueryResultExportSerializer.Serialize(["{\"name\":\"Ana\"}", "{\"id\":{\"$oid\":\"507f1f77bcf86cd799439011\"}}"]);
        using var json = JsonDocument.Parse(result);
        Assert.That(json.RootElement.GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public void SerializeRejectsInvalidDocument()
    {
        Assert.That(() => QueryResultExportSerializer.Serialize(["{"]), Throws.InstanceOf<JsonException>());
    }

    [Test]
    public void SerializeEmptyPageCreatesEmptyJsonArray()
    {
        Assert.That(QueryResultExportSerializer.Serialize([]), Is.EqualTo("[]"));
    }
}
