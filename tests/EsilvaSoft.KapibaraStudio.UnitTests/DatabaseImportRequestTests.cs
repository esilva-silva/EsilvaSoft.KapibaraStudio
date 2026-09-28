using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DatabaseImportRequestTests
{
    [TestCase("", "catalogo")]
    [TestCase("c:\\exportacao", "")]
    public void ValidateWithMissingRequiredPartThrows(string sourceDirectory, string targetDatabase)
    {
        var request = new DatabaseImportRequest(sourceDirectory, targetDatabase);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateWithSourceAndTargetReturnsSameRequest()
    {
        var request = new DatabaseImportRequest("c:\\exportacao", "catalogo");

        Assert.That(request.Validate(), Is.SameAs(request));
    }
}
