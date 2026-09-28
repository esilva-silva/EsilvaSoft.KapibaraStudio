using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class OperationKillRequestTests
{
    [Test]
    public void GetOperationIdAcceptsConfirmedPositiveInteger()
    {
        var request = new OperationKillRequest("12345", "12345");

        Assert.That(request.GetOperationId(), Is.EqualTo(12345));
    }

    [TestCase("0", "0")]
    [TestCase("-1", "-1")]
    [TestCase("abc", "abc")]
    [TestCase("123", "124")]
    public void GetOperationIdRejectsInvalidOrUnconfirmedValue(string operationId, string confirmation)
    {
        var request = new OperationKillRequest(operationId, confirmation);

        Assert.That(() => request.GetOperationId(), Throws.TypeOf<ArgumentException>());
    }
}
