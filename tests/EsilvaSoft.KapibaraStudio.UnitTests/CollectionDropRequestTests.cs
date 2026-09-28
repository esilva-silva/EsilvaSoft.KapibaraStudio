using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CollectionDropRequestTests
{
    [Test]
    public void ValidateRequiresExactTypedConfirmation()
    {
        var request = new CollectionDropRequest("catalogo", "clientes", "clientes");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("client")]
    [TestCase("Clientes")]
    [TestCase("")]
    public void ValidateRejectsDifferentConfirmation(string confirmation)
    {
        var request = new CollectionDropRequest("catalogo", "clientes", confirmation);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsSystemCollection()
    {
        var request = new CollectionDropRequest("catalogo", "system.profile", "system.profile");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
