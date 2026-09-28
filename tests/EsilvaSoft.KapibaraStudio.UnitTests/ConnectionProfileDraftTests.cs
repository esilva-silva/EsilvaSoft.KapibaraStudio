using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ConnectionProfileDraftTests
{
    [TestCase(" mongodb://localhost:27017/catalogo?retryWrites=true ", "catalogo", "catalogo")]
    [TestCase("mongodb+srv://cluster0.example.mongodb.net/?retryWrites=true", "cluster0.example.mongodb.net", null)]
    [TestCase("mongodb://usuario@db-a.example:27017,db-b.example:27017/pedidos", "pedidos", "pedidos")]
    public void FromConnectionStringExtractsDraftWithoutConnecting(string connectionString, string expectedName, string? expectedDatabase)
    {
        var draft = ConnectionProfileDraft.FromConnectionString(connectionString);

        Assert.Multiple(() =>
        {
            Assert.That(draft.ConnectionString, Is.EqualTo(connectionString.Trim()));
            Assert.That(draft.SuggestedName, Is.EqualTo(expectedName));
            Assert.That(draft.DefaultDatabase, Is.EqualTo(expectedDatabase));
        });
    }

    [TestCase("")]
    [TestCase("postgres://localhost/catalogo")]
    [TestCase("mongodb://")]
    public void FromConnectionStringRejectsMissingOrUnsupportedMongoUri(string connectionString)
    {
        Assert.That(() => ConnectionProfileDraft.FromConnectionString(connectionString), Throws.TypeOf<ArgumentException>());
    }
}
