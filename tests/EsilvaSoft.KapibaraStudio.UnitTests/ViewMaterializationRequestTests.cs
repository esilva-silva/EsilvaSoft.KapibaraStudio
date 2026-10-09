using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ViewMaterializationRequestTests
{
    private const string ViewDefinition = "{\"name\":\"active\",\"type\":\"view\"}";

    [Test]
    public void ValidatesExplicitTargetAndPreview()
    {
        var request = new ViewMaterializationRequest(
            "catalogo", "active", "snapshot", "[]", "snapshot", ViewDefinition, "{}");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("active", "active", "[]", "active")]
    [TestCase("active", "system.profile", "[]", "system.profile")]
    [TestCase("active", "snapshot", "[]", "other")]
    [TestCase("active", "snapshot", "{}", "snapshot")]
    public void RejectsInvalidTargetConfirmationOrPipelineShape(
        string view, string destination, string pipeline, string confirmation)
    {
        var request = new ViewMaterializationRequest(
            "catalogo", view, destination, pipeline, confirmation, ViewDefinition, "{}");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void RejectsMissingPreview()
    {
        var request = new ViewMaterializationRequest(
            "catalogo", "active", "snapshot", "[]", "snapshot", "{}", "{}");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
