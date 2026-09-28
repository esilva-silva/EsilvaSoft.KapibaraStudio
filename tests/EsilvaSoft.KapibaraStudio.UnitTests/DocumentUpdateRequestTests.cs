using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DocumentUpdateRequestTests
{
    [TestCase("{}", "{ \"$set\": { \"nome\": \"novo\" } }")]
    [TestCase("{ \"_id\": 1 }", "")]
    public void ValidateWithMissingFilterOrUpdateThrows(string filter, string update)
    {
        var request = new DocumentUpdateRequest("catalogo", "clientes", filter, update);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateWithUpsertReturnsSameRequest()
    {
        var request = new DocumentUpdateRequest("catalogo", "clientes", "{ \"_id\": 1 }", "{ \"$set\": { \"nome\": \"novo\" } }", Upsert: true);

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateWithArrayFiltersReturnsSameRequest()
    {
        var request = new DocumentUpdateRequest(
            "catalogo",
            "clientes",
            "{ \"_id\": 1 }",
            "{ \"$set\": { \"itens.$[item].ativo\": true } }",
            ArrayFiltersJson: "[{ \"item.status\": \"pendente\" }]");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateWithUpdatePipelineReturnsSameRequest()
    {
        var request = new DocumentUpdateRequest(
            "catalogo",
            "clientes",
            "{ \"_id\": 1 }",
            "[{ \"$set\": { \"ativo\": true } }]");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("[1]")]
    [TestCase("\"texto\"")]
    [TestCase("{")]
    public void ValidateWithInvalidUpdateShapeThrows(string update)
    {
        var request = new DocumentUpdateRequest("catalogo", "clientes", "{ \"_id\": 1 }", update);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [TestCase("{}")]
    [TestCase("[1]")]
    [TestCase("[")]
    public void ValidateWithInvalidArrayFiltersThrows(string arrayFilters)
    {
        var request = new DocumentUpdateRequest(
            "catalogo",
            "clientes",
            "{ \"_id\": 1 }",
            "{ \"$set\": { \"ativo\": true } }",
            ArrayFiltersJson: arrayFilters);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
