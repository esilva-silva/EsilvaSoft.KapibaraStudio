using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CollectionIntegrityCheckRequestTests
{
    [Test]
    public void ValidateWithConfirmedUserCollectionReturnsSameRequest()
    {
        var request = new CollectionIntegrityCheckRequest("catalogo", "clientes", "clientes", CollectionMaintenanceTestData.Preflight());

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("", "clientes", "clientes")]
    [TestCase("catalogo", "", "")]
    [TestCase("catalogo", "system.views", "system.views")]
    [TestCase("catalogo", "clientes", "outro")]
    public void ValidateWithInvalidOrUnconfirmedTargetThrows(string database, string collection, string confirmation)
    {
        var request = new CollectionIntegrityCheckRequest(database, collection, confirmation);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
