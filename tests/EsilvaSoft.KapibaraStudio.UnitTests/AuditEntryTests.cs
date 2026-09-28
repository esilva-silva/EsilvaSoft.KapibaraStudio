using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AuditEntryTests
{
    [Test]
    public void CreateKeepsMetadataWithoutConnectionString()
    {
        var entry = AuditEntry.Create("collection.drop", Guid.NewGuid(), "catalogo", "clientes", "Coleção removida.");

        Assert.Multiple(() =>
        {
            Assert.That(entry.Validate(), Is.SameAs(entry));
            Assert.That(entry.DisplayText, Does.Contain("collection.drop"));
            Assert.That(entry.Summary, Does.Not.Contain("mongodb://"));
        });
    }

    [Test]
    public void ValidateRejectsSummaryBeyondLimit()
    {
        var entry = new AuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "collection.drop", null, "catalogo", "clientes", new string('x', 501));

        Assert.That(() => entry.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsConnectionUriInSummary()
    {
        var entry = new AuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, "collection.drop", null, "catalogo", "clientes", "mongodb://usuario:segredo@servidor");

        Assert.That(() => entry.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
