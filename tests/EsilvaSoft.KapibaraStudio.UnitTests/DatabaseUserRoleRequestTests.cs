using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DatabaseUserRoleRequestTests
{
    [Test]
    public void ValidateAcceptsGrantAndRevokePayloads()
    {
        var grant = new DatabaseUserRoleRequest("catalogo", "relatorio", "[{\"role\":\"read\",\"db\":\"catalogo\"}]", "relatorio", false);
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
        var request = new DatabaseUserRoleRequest("catalogo", "relatorio", roles, "relatorio", false);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
