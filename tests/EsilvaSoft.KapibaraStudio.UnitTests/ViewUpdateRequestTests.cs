using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ViewUpdateRequestTests
{
    [Test]
    public void ValidateAcceptsExactConfirmationAndArrayPipeline()
    {
        var request = new ViewUpdateRequest("catalogo", "clientesAtivos", "[{\"$match\":{\"ativo\":true}}]", "clientesAtivos");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("", "clientesAtivos", "[]", "clientesAtivos")]
    [TestCase("catalogo", "clientesAtivos", "{}", "clientesAtivos")]
    [TestCase("catalogo", "clientesAtivos", "[1]", "clientesAtivos")]
    [TestCase("catalogo", "clientesAtivos", "[]", "outro")]
    public void ValidateRejectsInvalidViewUpdate(string database, string view, string pipeline, string confirmation)
    {
        var request = new ViewUpdateRequest(database, view, pipeline, confirmation);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
