using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class IndexVisibilityRequestTests
{
    [Test]
    public void ValidateWithConfirmedUserIndexReturnsSameRequest()
    {
        var request = new IndexVisibilityRequest("catalogo", "clientes", "email_1", "email_1", true);

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("_id_", "_id_")]
    [TestCase("email_1", "outro")]
    public void ValidateWithProtectedOrUnconfirmedIndexThrows(string name, string confirmation)
    {
        var request = new IndexVisibilityRequest("catalogo", "clientes", name, confirmation, true);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
