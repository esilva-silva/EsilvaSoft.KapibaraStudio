using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DatabaseUserRoleRequestTests
{
    [Test]
    public void ValidateAcceptsGrantAndRevokePayloads()
    {
        var grant = new DatabaseUserRoleRequest("catalogo", "relatorio", "[{\"role\":\"read\",\"db\":\"catalogo\"}]", "relatorio", false, "[]");
        var revoke = grant with { Revoke = true };

        Assert.Multiple(() =>
        {
            Assert.That(grant.Validate(), Is.SameAs(grant));
            Assert.That(revoke.Validate(), Is.SameAs(revoke));
        });
    }

    [TestCase("[]")]
    [TestCase("{}")]
    [TestCase("{")]
    public void ValidateRejectsInvalidRolePayload(string roles)
    {
        var request = new DatabaseUserRoleRequest("catalogo", "relatorio", roles, "relatorio", false, "[]");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [TestCase("[{\"role\":\"read\"}]")]
    [TestCase("[{\"db\":\"catalogo\"}]")]
    [TestCase("[{}]")]
    [TestCase("[{\"role\":\"read\",\"db\":\"catalogo\"},{\"role\":\"read\",\"db\":\"catalogo\"}]")]
    public void ValidateRejectsRoleDocumentsWithoutRoleAndDatabase(string roles)
    {
        var request = new DatabaseUserRoleRequest("catalogo", "relatorio", roles, "relatorio", false, "[]");
        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRequiresAWellFormedPreviewSnapshot()
    {
        var request = new DatabaseUserRoleRequest("catalogo", "relatorio", "[{\"role\":\"read\",\"db\":\"catalogo\"}]", "relatorio", false, "{}");
        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [TestCase(MongoUserAdministrationFailureKind.PermissionDenied, "errorUserPermission")]
    [TestCase(MongoUserAdministrationFailureKind.Unsupported, "errorUserUnsupported")]
    [TestCase(MongoUserAdministrationFailureKind.InvalidInput, "errorUserInput")]
    [TestCase(MongoUserAdministrationFailureKind.Conflict, "errorUserConflict")]
    [TestCase(MongoUserAdministrationFailureKind.CommandFailed, "errorUserCommand")]
    [TestCase(MongoUserAdministrationFailureKind.ReadbackFailed, "errorUserReadback")]
    public void OperationErrorDescriptionUsesSafeUserCategory(
        MongoUserAdministrationFailureKind kind, string expectedKey)
    {
        var result = EsilvaSoft.KapibaraStudio.Application.OperationErrorMessages.Describe(
            new MongoUserAdministrationException(kind), localize: key => "<" + key + ">");

        Assert.That(result, Is.EqualTo("<" + expectedKey + ">"));
    }

    [Test]
    public void MissingLocalizedUserMessageFallsBackToSafeText()
    {
        var result = EsilvaSoft.KapibaraStudio.Application.OperationErrorMessages.Describe(
            new MongoUserAdministrationException(MongoUserAdministrationFailureKind.CommandFailed),
            localize: key => key);

        Assert.That(result, Is.EqualTo("A operação de usuário não foi concluída. Releia o estado antes de tentar novamente."));
    }

    [Test]
    public void MissingCatalogEntryFallsBackToSafeText()
    {
        var result = EsilvaSoft.KapibaraStudio.Application.OperationErrorMessages.Describe(
            new MongoUserAdministrationException(MongoUserAdministrationFailureKind.CommandFailed),
            localize: key => "[[" + key + "]]");

        Assert.That(result, Is.EqualTo("A operação de usuário não foi concluída. Releia o estado antes de tentar novamente."));
    }
}
