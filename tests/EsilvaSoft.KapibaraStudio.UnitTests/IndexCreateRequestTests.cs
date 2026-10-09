using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class IndexCreateRequestTests
{
    [Test]
    public void ValidateWithNegativeTtlThrows()
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"expiraEm\": 1 }", ExpireAfterSeconds: -1);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ValidateWithUniqueSparseAndTtlReturnsSameRequest()
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"expiraEm\": 1 }", IsUnique: true, IsSparse: true, ExpireAfterSeconds: 60);

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateWithHiddenIndexReturnsSameRequest()
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"email\": 1 }", IsHidden: true);

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateWithPartialFilterReturnsSameRequest()
    {
        var request = new IndexCreateRequest(
            "catalogo",
            "clientes",
            "{ \"email\": 1 }",
            IsUnique: true,
            PartialFilterJson: "{ \"ativo\": true }");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateAcceptsOrderedCompoundSpecialIndexWithNumericFields()
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"prefixo\": 1, \"localizacao\": \"2dsphere\", \"sufixo\": -1 }");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateAcceptsMultipleAdjacentTextFieldsInOneTextIndex()
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"tenant\": 1, \"titulo\": \"text\", \"descricao\": \"text\", \"criadoEm\": -1 }");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateRejectsNonAdjacentTextFieldsInCompoundIndex()
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"titulo\": \"text\", \"tenant\": 1, \"descricao\": \"text\" }");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateAcceptsMultipleGeoJsonFieldsInCompoundTwoDSphereIndex()
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"origem\": \"2dsphere\", \"destino\": \"2dsphere\", \"categoria\": 1 }");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [Test]
    public void ValidateAcceptsTwoDIndexAsFirstOfAtMostTwoKeys()
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"localizacao\": \"2d\", \"categoria\": 1 }");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("{ \"categoria\": 1, \"localizacao\": \"2d\" }")]
    [TestCase("{ \"localizacao\": \"2d\", \"categoria\": 1, \"tenant\": 1 }")]
    public void ValidateRejectsTwoDCompoundIndexWithInvalidFieldOrderOrCount(string keys)
    {
        var request = new IndexCreateRequest("catalogo", "clientes", keys);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [TestCase("{ \"expiraEm\": 1, \"tenant\": 1 }", 60)]
    [TestCase("{ \"digest\": \"hashed\" }", null, true)]
    [TestCase("{ \"a\": \"2d\", \"b\": \"2dsphere\" }", null)]
    public void ValidateRejectsKnownIncompatibleIndexOptions(string keys, int? ttl, bool unique = false)
    {
        var request = new IndexCreateRequest("catalogo", "clientes", keys, IsUnique: unique, ExpireAfterSeconds: ttl);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsSparseTogetherWithPartialFilter()
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"campo\": 1 }", IsSparse: true, PartialFilterJson: "{ \"ativo\": true }");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsKeyDirectionOutsideMongoIndexTypes()
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"campo\": 0 }");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsBlankPartialFilter()
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"email\": 1 }", PartialFilterJson: " ");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateAcceptsCollationDocument()
    {
        var request = new IndexCreateRequest(
            "catalogo",
            "clientes",
            "{ \"nome\": 1 }",
            CollationJson: "{ \"locale\": \"pt\", \"strength\": 1 }");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase(" ")]
    [TestCase("[]")]
    [TestCase("{")]
    public void ValidateRejectsInvalidCollation(string collation)
    {
        var request = new IndexCreateRequest("catalogo", "clientes", "{ \"nome\": 1 }", CollationJson: collation);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateAcceptsInclusionWildcardProjectionForWildcardKey()
    {
        var request = new IndexCreateRequest(
            "catalogo",
            "clientes",
            "{ \"$**\": 1 }",
            WildcardProjectionJson: "{ \"atributos.cor\": 1, \"_id\": 0 }");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("{ \"campo\": 1 }", "{ \"atributos.cor\": 1 }")]
    [TestCase("{ \"$**\": 1 }", "{ \"atributos.cor\": 2 }")]
    [TestCase("{ \"$**\": 1 }", "{ \"atributos.cor\": 1, \"atributos.tamanho\": 0 }")]
    public void ValidateRejectsInvalidWildcardProjection(string keys, string projection)
    {
        var request = new IndexCreateRequest("catalogo", "clientes", keys, WildcardProjectionJson: projection);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
