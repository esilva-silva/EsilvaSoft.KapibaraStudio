using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class SavedQueryTests
{
    [Test]
    public void CreatePreservesQueryOptionsAndFavoriteState()
    {
        var query = new MongoQuery(
            "catalogo",
            "clientes",
            "{ \"ativo\": true }",
            "{ \"nome\": 1 }",
            "{ \"nome\": 1 }",
            25,
            5,
            "{ \"nome\": 1 }",
            3000,
            "favorita",
            50,
            "{ \"locale\": \"pt\", \"strength\": 1 }");
        var saved = SavedQuery.Create("Ativos", Guid.NewGuid(), query, isFavorite: true);

        Assert.Multiple(() =>
        {
            Assert.That(saved.ToQuery(), Is.EqualTo(query));
            Assert.That(saved.DisplayText, Does.StartWith("★ Ativos"));
            Assert.That(saved.Validate(), Is.EqualTo(saved));
        });
    }

    [Test]
    public void ValidateRejectsEmptyName()
    {
        var saved = new SavedQuery(Guid.NewGuid(), " ", null, "catalogo", "clientes", "{}", null, null, null, 100, 0, null, false, DateTimeOffset.UtcNow);

        Assert.That(() => saved.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsNameLongerThanLimit()
    {
        var saved = new SavedQuery(Guid.NewGuid(), new string('x', 121), null, "catalogo", "clientes", "{}", null, null, null, 100, 0, null, false, DateTimeOffset.UtcNow);

        Assert.That(() => saved.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
