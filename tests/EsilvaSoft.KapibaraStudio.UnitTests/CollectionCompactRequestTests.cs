using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CollectionCompactRequestTests
{
    [Test]
    public void ValidateWithConfirmedCollectionReturnsSameRequest()
    {
        var request = new CollectionCompactRequest("catalogo", "clientes", "clientes", Force: true, Preflight: CollectionMaintenanceTestData.Preflight());

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("", "clientes", "clientes")]
    [TestCase("catalogo", "", "")]
    [TestCase("catalogo", "system.views", "system.views")]
    [TestCase("catalogo", "clientes", "outro")]
    public void ValidateWithInvalidOrUnconfirmedTargetThrows(string database, string collection, string confirmation)
    {
        var request = new CollectionCompactRequest(database, collection, confirmation);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
