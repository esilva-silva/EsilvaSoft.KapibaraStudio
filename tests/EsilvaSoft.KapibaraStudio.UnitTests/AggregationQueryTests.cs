using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AggregationQueryTests
{
    [TestCase(0)]
    [TestCase(10001)]
    public void ValidateWithLimitOutsideSupportedRangeThrows(int limit)
    {
        var query = new AggregationQuery("catalogo", "clientes", "[]", limit);

        Assert.That(() => query.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ValidateWithRequiredPartsReturnsSameQuery()
    {
        var query = new AggregationQuery("catalogo", "clientes", "[]");

        Assert.That(query.Validate(), Is.SameAs(query));
    }
}
