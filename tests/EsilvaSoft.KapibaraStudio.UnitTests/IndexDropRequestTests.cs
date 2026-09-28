using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class IndexDropRequestTests
{
    [TestCase("", "clientes", "idx_email")]
    [TestCase("catalogo", "", "idx_email")]
    [TestCase("catalogo", "clientes", "")]
    public void ValidateWithMissingRequiredPartThrows(string database, string collection, string name)
    {
        var request = new IndexDropRequest(database, collection, name);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateProtectsIdIndex()
    {
        var request = new IndexDropRequest("catalogo", "clientes", "_id_");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateWithUserIndexReturnsSameRequest()
    {
        var request = new IndexDropRequest("catalogo", "clientes", "idx_email");

        Assert.That(request.Validate(), Is.SameAs(request));
    }
}
