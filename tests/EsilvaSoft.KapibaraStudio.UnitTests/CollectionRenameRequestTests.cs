using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CollectionRenameRequestTests
{
    [Test]
    public void ValidateAcceptsDistinctUserCollections()
    {
        var request = new CollectionRenameRequest("catalogo", "clientes", "clientes_arquivados");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("clientes", "clientes")]
    [TestCase("system.views", "clientes")]
    [TestCase("clientes", "system.profile")]
    public void ValidateRejectsUnsafeNamespaces(string source, string target)
    {
        var request = new CollectionRenameRequest("catalogo", source, target);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
