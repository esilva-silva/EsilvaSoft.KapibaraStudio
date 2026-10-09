using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class StandaloneImportRequestTests
{
    [Test]
    public void RejectsDottedOrProtectedCollectionAndWrongConfirmation()
    {
        var schema = new TransferImportSchema(TransferImportFormat.Ndjson);
        var request = new StandaloneImportRequest("items.ndjson", "target", "items", schema,
            DatabaseImportDuplicatePolicy.Reject, "target");
        Assert.Multiple(() =>
        {
            Assert.That(() => (request with { TargetCollection = "nested.items" }).Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(() => (request with { TargetCollection = "system.views" }).Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(() => (request with { ConfirmationDatabase = "other" }).Validate(), Throws.TypeOf<ArgumentException>());
            Assert.That(() => request.Validate(), Is.SameAs(request));
            Assert.That(() => (request with { RestartCheckpointId = Guid.Empty }).Validate(), Throws.TypeOf<ArgumentException>());
        });
    }
}
