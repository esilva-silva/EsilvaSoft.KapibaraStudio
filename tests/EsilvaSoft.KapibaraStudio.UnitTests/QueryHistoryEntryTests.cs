using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class QueryHistoryEntryTests
{
    [Test]
    public void CreateAndToQueryPreserveAllOptions()
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
            "histórico",
            50,
            "{ \"locale\": \"pt\", \"strength\": 1 }");
        var entry = QueryHistoryEntry.Create(Guid.NewGuid(), query);

        Assert.That(entry.ToQuery(), Is.EqualTo(query));
    }

    [Test]
    public void ValidateRejectsEmptyIdentifier()
    {
        var entry = new QueryHistoryEntry(Guid.Empty, null, "catalogo", "clientes", "{}", null, null, null, 10, 0, null, DateTimeOffset.UtcNow);

        Assert.That(() => entry.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void DisplayTextContainsNamespace()
    {
        var entry = QueryHistoryEntry.Create(null, new MongoQuery("catalogo", "clientes"));

        Assert.That(entry.DisplayText, Does.Contain("catalogo.clientes"));
    }
}
