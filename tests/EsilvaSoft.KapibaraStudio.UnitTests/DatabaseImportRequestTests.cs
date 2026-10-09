using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DatabaseImportRequestTests
{
    [TestCase("", "catalogo")]
    [TestCase("c:\\exportacao", "")]
    public void ValidateWithMissingRequiredPartThrows(string sourceDirectory, string targetDatabase)
    {
        var request = new DatabaseImportRequest(sourceDirectory, targetDatabase);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateWithSourceAndTargetReturnsSameRequest()
    {
        var request = new DatabaseImportRequest("c:\\exportacao", "catalogo");

        Assert.That(request.Validate(), Is.SameAs(request));
        Assert.That(request.DuplicatePolicy, Is.EqualTo(DatabaseImportDuplicatePolicy.Reject));
    }

    [Test]
    public void InvalidPolicyIsRefused()
    {
        var request = new DatabaseImportRequest("c:\\exportacao", "catalogo", (DatabaseImportDuplicatePolicy)99);
        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void UpsertNeedsExactTargetConfirmation()
    {
        var request = new DatabaseImportRequest("c:\\exportacao", "catalogo", DatabaseImportDuplicatePolicy.Upsert, "outro");
        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
        Assert.That((request with { ConfirmationDatabase = "catalogo" }).Validate(), Is.Not.Null);
    }

    [Test]
    public void RestoringDefinitionsNeedsExactTargetConfirmationAndIsOptIn()
    {
        var defaultRequest = new DatabaseImportRequest("c:\\exportacao", "catalogo");
        var restoreWithoutConfirmation = new DatabaseImportRequest(
            "c:\\exportacao", "catalogo", ConfirmationDatabase: "outro", RestoreDefinitions: true);
        var confirmedRestore = new DatabaseImportRequest(
            "c:\\exportacao", "catalogo", ConfirmationDatabase: "catalogo", RestoreDefinitions: true);

        Assert.That(defaultRequest.RestoreDefinitions, Is.False);
        Assert.That(() => restoreWithoutConfirmation.Validate(), Throws.TypeOf<ArgumentException>());
        Assert.That(confirmedRestore.Validate(), Is.SameAs(confirmedRestore));
    }

    [Test]
    public void RestartRequiresValidCheckpointAndExactTargetConfirmationEvenUnderRejectPolicy()
    {
        var request = new DatabaseImportRequest("c:\\exportacao", "catalogo")
            { RestartCheckpointId = Guid.NewGuid() };
        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
        Assert.That(() => (request with { ConfirmationDatabase = "catalogo", RestartCheckpointId = Guid.Empty }).Validate(),
            Throws.TypeOf<ArgumentException>());
        Assert.That((request with { ConfirmationDatabase = "catalogo" }).Validate(), Is.Not.Null);
    }
}
