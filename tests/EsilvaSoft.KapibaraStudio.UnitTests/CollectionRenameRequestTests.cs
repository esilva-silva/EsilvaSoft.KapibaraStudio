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

    [Test]
    public void ValidateRequiresSeparateDestinationConfirmationWhenReplacing()
    {
        var withoutConfirmation = new CollectionRenameRequest("catalogo", "clientes", "arquivo", DropTarget: true);
        var wrongConfirmation = withoutConfirmation with { DropTargetConfirmation = "clientes" };
        var confirmed = withoutConfirmation with { DropTargetConfirmation = "arquivo" };

        Assert.Multiple(() =>
        {
            Assert.That(() => withoutConfirmation.Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(() => wrongConfirmation.Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(confirmed.Validate(), Is.SameAs(confirmed));
        });
    }
}
