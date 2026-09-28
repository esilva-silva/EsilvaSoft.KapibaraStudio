using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class BulkInsertRequestTests
{
    [TestCase(0)]
    [TestCase(10_001)]
    public void ValidateWithInvalidMaximumThrows(int maximum)
    {
        var request = new BulkInsertRequest("catalogo", "clientes", "[]", maximum);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ValidateWithArrayReturnsSameRequest()
    {
        var request = new BulkInsertRequest("catalogo", "clientes", "[]", Ordered: false);

        Assert.That(request.Validate(), Is.SameAs(request));
    }
}
