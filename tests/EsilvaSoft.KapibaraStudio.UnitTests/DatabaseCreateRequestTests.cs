using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DatabaseCreateRequestTests
{
    [Test]
    public void ValidateWithConfirmedUserDatabaseReturnsSameRequest()
    {
        var request = new DatabaseCreateRequest("catalogo", "clientes", "catalogo");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("", "clientes", "")]
    [TestCase("admin", "clientes", "admin")]
    [TestCase("catalogo", "system.views", "catalogo")]
    [TestCase("catalogo", "clientes", "outro")]
    public void ValidateWithInvalidOrUnconfirmedValuesThrows(string database, string collection, string confirmation)
    {
        var request = new DatabaseCreateRequest(database, collection, confirmation);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
