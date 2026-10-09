using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Application;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DatabaseRoleMutationRequestTests
{
    private const string Privileges = "[{\"resource\":{\"db\":\"sample_mflix\",\"collection\":\"movies\"},\"actions\":[\"find\"]}]";
    private const string InheritedRoles = "[{\"role\":\"read\",\"db\":\"sample_mflix\"}]";

    [TestCase(DatabaseRoleMutationKind.Create, "null")]
    [TestCase(DatabaseRoleMutationKind.Update, "{\"role\":\"viewer\",\"db\":\"sample_mflix\"}")]
    public void ValidateAcceptsScopedCreateAndUpdate(DatabaseRoleMutationKind kind, string expected)
    {
        Assert.That(Create(kind, expected).Validate(), Is.Not.Null);
    }

    [Test]
    public void ValidateAcceptsDropWithoutPrivilegePayload()
    {
        var request = new DatabaseRoleMutationRequest(
            "sample_mflix", "viewer", DatabaseRoleMutationKind.Drop, "", "", "viewer",
            "{\"role\":\"viewer\",\"db\":\"sample_mflix\"}");

        Assert.That(request.Validate(), Is.Not.Null);
    }

    [Test]
    public void ValidateRejectsConfirmationMismatch()
    {
        var request = Create(DatabaseRoleMutationKind.Create, "null") with { ConfirmationRoleName = "other" };

        Assert.Throws<ArgumentException>(() => request.Validate());
    }

    [TestCase("[{\"resource\":{\"db\":\"other\",\"collection\":\"movies\"},\"actions\":[\"find\"]}]")]
    [TestCase("[{\"resource\":{\"db\":\"sample_mflix\",\"collection\":\"\",\"cluster\":true},\"actions\":[\"find\"]}]")]
    [TestCase("[{\"resource\":{\"db\":\"sample_mflix\",\"collection\":\"movies\"},\"actions\":[\"find\"],\"extra\":true}]")]
    public void ValidateRejectsPrivilegeOutsideSupportedShape(string privileges)
    {
        var request = Create(DatabaseRoleMutationKind.Create, "null") with { PrivilegesJson = privileges };

        Assert.Throws<ArgumentException>(() => request.Validate());
    }

    [Test]
    public void ValidateRejectsInheritedRoleFromAnotherDatabase()
    {
        var request = Create(DatabaseRoleMutationKind.Create, "null") with
        {
            InheritedRolesJson = "[{\"role\":\"read\",\"db\":\"admin\"}]"
        };

        Assert.Throws<ArgumentException>(() => request.Validate());
    }

    [Test]
    public void ValidateRejectsStalePreviewStateForOperationKind()
    {
        var request = Create(DatabaseRoleMutationKind.Create, "{\"role\":\"viewer\",\"db\":\"sample_mflix\"}");

        Assert.Throws<ArgumentException>(() => request.Validate());
    }

    [Test]
    public void ValidateRejectsInvalidJson()
    {
        var request = Create(DatabaseRoleMutationKind.Create, "null") with { PrivilegesJson = "{" };

        Assert.Throws<ArgumentException>(() => request.Validate());
    }

    [TestCase(MongoRoleAdministrationFailureKind.PermissionDenied, "errorRolePermission")]
    [TestCase(MongoRoleAdministrationFailureKind.Unsupported, "errorRoleUnsupported")]
    [TestCase(MongoRoleAdministrationFailureKind.ReadbackFailed, "errorRoleReadback")]
    [TestCase(MongoRoleAdministrationFailureKind.InvalidInput, "errorRoleInput")]
    [TestCase(MongoRoleAdministrationFailureKind.CommandFailed, "errorRoleCommand")]
    public void OperationErrorDescriptionUsesExplicitLocalizedRoleFailure(
        MongoRoleAdministrationFailureKind kind,
        string expectedKey)
    {
        var result = OperationErrorMessages.Describe(
            new MongoRoleAdministrationException(kind),
            localize: key => "<" + key + ">");

        Assert.That(result, Is.EqualTo("<" + expectedKey + ">"));
    }

    private static DatabaseRoleMutationRequest Create(DatabaseRoleMutationKind kind, string expected) =>
        new("sample_mflix", "viewer", kind, Privileges, InheritedRoles, "viewer", expected);
}
