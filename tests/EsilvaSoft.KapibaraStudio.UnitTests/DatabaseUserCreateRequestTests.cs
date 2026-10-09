using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DatabaseUserCreateRequestTests
{
    [Test]
    public void ValidateAcceptsConfirmedUserWithRoleDocuments()
    {
        var request = new DatabaseUserCreateRequest("catalogo", "relatorio", "segredo", "[{\"role\":\"read\",\"db\":\"catalogo\"}]", "relatorio");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("[]")]
    [TestCase("{}")]
    [TestCase("[\"read\"]")]
    [TestCase("[{\"role\":\"read\"}]")]
    [TestCase("[{\"role\":\"read\",\"db\":\"catalogo\"},{\"role\":\"read\",\"db\":\"catalogo\"}]")]
    [TestCase("{")]
    public void ValidateRejectsInvalidRoles(string roles)
    {
        var request = new DatabaseUserCreateRequest("catalogo", "relatorio", "segredo", roles, "relatorio");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsMismatchedConfirmation()
    {
        var request = new DatabaseUserCreateRequest("catalogo", "relatorio", "segredo", "[{\"role\":\"read\",\"db\":\"catalogo\"}]", "outro");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsPasswordAboveLimit()
    {
        var request = new DatabaseUserCreateRequest(
            "catalogo",
            "relatorio",
            new string('x', 1025),
            "[{\"role\":\"read\",\"db\":\"catalogo\"}]",
            "relatorio");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ValidateRejectsRolesPayloadAboveLimit()
    {
        var request = new DatabaseUserCreateRequest(
            "catalogo",
            "relatorio",
            "segredo",
            new string('x', 32 * 1024 + 1),
            "relatorio");

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
