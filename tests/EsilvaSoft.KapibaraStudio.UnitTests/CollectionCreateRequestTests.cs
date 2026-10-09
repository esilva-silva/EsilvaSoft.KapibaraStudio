using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class CollectionCreateRequestTests
{
    [TestCase("", "clientes")]
    [TestCase("catalogo", "")]
    [TestCase("catalogo", "system.views")]
    public void ValidateWithInvalidNamespaceThrows(string database, string collection)
    {
        var request = new CollectionCreateRequest(database, collection);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateWithUserCollectionReturnsSameRequest()
    {
        var request = new CollectionCreateRequest("catalogo", "clientes");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateAcceptsCappedCollectionWithSizeAndDocumentLimit()
    {
        var request = new CollectionCreateRequest("catalogo", "eventos", IsCapped: true, MaxSizeBytes: 1_048_576, MaxDocuments: 10_000);

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateAcceptsViewWithPipeline()
    {
        var request = new CollectionCreateRequest("catalogo", "clientesAtivos", ViewOn: "clientes", ViewPipelineJson: "[{\"$match\":{\"ativo\":true}}]");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateRejectsViewWithoutPipeline()
    {
        var request = new CollectionCreateRequest("catalogo", "clientesAtivos", ViewOn: "clientes");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsViewUsingItselfAsSource()
    {
        var request = new CollectionCreateRequest("catalogo", "clientesAtivos", ViewOn: "clientesAtivos", ViewPipelineJson: "[]");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [TestCase("system.clientes", "[]")]
    [TestCase("clientes", "{}")]
    [TestCase("clientes", "[1]")]
    public void ValidateRejectsInvalidViewSourceOrPipeline(string source, string pipeline)
    {
        var request = new CollectionCreateRequest("catalogo", "clientesAtivos", ViewOn: source, ViewPipelineJson: pipeline);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [TestCase(false, 1_048_576L, null)]
    [TestCase(true, null, null)]
    [TestCase(true, 0L, null)]
    public void ValidateRejectsInvalidCappedOptions(bool capped, long? size, long? documents)
    {
        var request = new CollectionCreateRequest("catalogo", "eventos", capped, size, documents);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateAcceptsOptionalCollationDocument()
    {
        var request = new CollectionCreateRequest("catalogo", "clientes", CollationJson: "{ \"locale\": \"pt\", \"strength\": 1 }");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateAcceptsClusteredCollectionWithSingleKey()
    {
        var request = new CollectionCreateRequest("catalogo", "eventos", IsClustered: true, ClusteredIndexKeyJson: "{ \"_id\": 1 }");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase(false, "{ \"_id\": 1 }")]
    [TestCase(true, "{}")]
    [TestCase(true, "{ \"id\": 1, \"ordem\": 1 }")]
    public void ValidateRejectsInvalidClusteredOptions(bool clustered, string key)
    {
        var request = new CollectionCreateRequest("catalogo", "eventos", IsClustered: clustered, ClusteredIndexKeyJson: key);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [TestCase("[]")]
    [TestCase("{")]
    public void ValidateRejectsInvalidCollation(string collation)
    {
        var request = new CollectionCreateRequest("catalogo", "clientes", CollationJson: collation);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
