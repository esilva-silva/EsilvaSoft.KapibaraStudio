using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DocumentDuplicateDraftTests
{
    [Test]
    public void CreateWithoutIdPreservesExtendedJsonValuesAndRemovesOnlyTopLevelIdentifier()
    {
        var draft = DocumentDuplicateDraft.CreateWithoutId("""
            {
              "_id": { "$oid": "507f1f77bcf86cd799439011" },
              "name": "Ana",
              "nested": { "_id": "preservado" },
              "uuid": { "$binary": { "base64": "ABEiM0RVZneImaq7zN3u/w==", "subType": "04" } }
            }
            """);

        using var document = JsonDocument.Parse(draft);
        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.TryGetProperty("_id", out _), Is.False);
            Assert.That(document.RootElement.GetProperty("nested").GetProperty("_id").GetString(), Is.EqualTo("preservado"));
            Assert.That(document.RootElement.GetProperty("uuid").GetProperty("$binary").GetProperty("subType").GetString(), Is.EqualTo("04"));
        });
    }

    [TestCase("[]")]
    [TestCase("texto")]
    [TestCase("")]
    public void CreateWithoutIdRejectsInvalidDocumentPreview(string documentJson)
    {
        Assert.That(() => DocumentDuplicateDraft.CreateWithoutId(documentJson), Throws.TypeOf<ArgumentException>());
    }
}
