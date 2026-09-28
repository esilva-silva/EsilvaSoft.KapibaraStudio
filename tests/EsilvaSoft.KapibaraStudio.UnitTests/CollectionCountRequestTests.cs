using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CollectionCountRequestTests
{
    [Test]
    public void ExactCountAcceptsFilterAndTimeLimit()
    {
        var request = new CollectionCountRequest("catalogo", "clientes", "{ \"ativo\": true }", MaxTimeMs: 2500);

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void EstimatedCountRejectsNonEmptyFilter()
    {
        var request = new CollectionCountRequest("catalogo", "clientes", "{ \"ativo\": true }", UseEstimatedCount: true);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [TestCase("[]")]
    [TestCase("texto")]
    public void ValidateRejectsFilterThatIsNotJsonObject(string filter)
    {
        var request = new CollectionCountRequest("catalogo", "clientes", filter);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
