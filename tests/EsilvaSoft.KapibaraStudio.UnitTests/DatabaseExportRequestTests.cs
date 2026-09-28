using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DatabaseExportRequestTests
{
    [TestCase(0)]
    [TestCase(1_000_001)]
    public void ValidateWithInvalidPerCollectionLimitThrows(int limit)
    {
        var request = new DatabaseExportRequest("catalogo", limit);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ValidateWithDatabaseAndLimitReturnsSameRequest()
    {
        var request = new DatabaseExportRequest("catalogo", 500);

        Assert.That(request.Validate(), Is.SameAs(request));
    }
}
