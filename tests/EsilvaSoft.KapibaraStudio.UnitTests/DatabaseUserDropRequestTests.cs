using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DatabaseUserDropRequestTests
{
    [Test]
    public void ValidateAcceptsExactConfirmation()
    {
        var request = new DatabaseUserDropRequest("catalogo", "relatorio", "relatorio");

        Assert.That(request.Validate(), Is.SameAs(request));
    }

    [TestCase("", "relatorio", "relatorio")]
    [TestCase("catalogo", "", "")]
    [TestCase("catalogo", "relatorio", "outro")]
    public void ValidateRejectsMissingOrMismatchedValues(string database, string username, string confirmation)
    {
        var request = new DatabaseUserDropRequest(database, username, confirmation);

        Assert.That(() => request.Validate(), Throws.TypeOf<ArgumentException>());
    }
}
