using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CollectionRenameTargetPreviewTests
{
    [Test]
    public void PreviewIsBoundToProfileInstanceNamespaceAndExactDefinition()
    {
        var profile = new ConnectionProfile(Guid.NewGuid(), "local", "mongodb://localhost:27017");
        var anotherGeneration = profile with { Name = "local again" };
        var preview = new CollectionRenameTargetPreview(
            profile, "catalogo", "origem", "destino", "{\"name\":\"destino\",\"type\":\"collection\"}");

        Assert.Multiple(() =>
        {
            Assert.That(preview.MatchesContext(profile, "catalogo", "origem", "destino"), Is.True);
            Assert.That(preview.MatchesContext(anotherGeneration, "catalogo", "origem", "destino"), Is.False);
            Assert.That(preview.MatchesContext(profile, "catalogo", "outra", "destino"), Is.False);
            Assert.That(preview.MatchesContext(profile, "catalogo", "origem", "outro"), Is.False);
            Assert.That(preview.MatchesObservedDefinition(preview.DefinitionJson), Is.True);
            Assert.That(preview.MatchesObservedDefinition("{}"), Is.False);
            Assert.That(preview.MatchesObservedDefinition("{\"name\":\"destino\",\"type\":\"view\"}"), Is.False);
        });
    }
}
