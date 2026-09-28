using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DistinctValuesRequestTests
{
    [Test]
    public void ValidateAcceptsNestedFieldAndFilter()
    {
        var request = new DistinctValuesRequest("catalogo", "clientes", "endereco.cidade", "{ \"ativo\": true }", 250, 2_000);

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("")]
    [TestCase("$where")]
    [TestCase("endereco..cidade")]
    public void ValidateRejectsInvalidFieldPath(string field)
    {
        var request = new DistinctValuesRequest("catalogo", "clientes", field);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsMaximumOutsideServerBoundedRange()
    {
        var request = new DistinctValuesRequest("catalogo", "clientes", "status", MaximumValues: 10_001);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
