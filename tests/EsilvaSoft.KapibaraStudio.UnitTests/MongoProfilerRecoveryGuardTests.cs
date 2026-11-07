using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class MongoProfilerRecoveryGuardTests
{
    [Test]
    public void ChangedProcessIsRejectedImmediatelyBeforeMutation()
    {
        const string status = """{"was":1,"slowms":50,"sampleRate":0.5}""";
        const string expected = """{"topologyVersion":{"processId":{"$oid":"507f1f77bcf86cd799439011"}},"isWritablePrimary":true}""";
        const string changed = """{"topologyVersion":{"processId":{"$oid":"507f1f77bcf86cd799439012"}},"isWritablePrimary":true}""";
        Assert.That(() => MongoServerAdministrator.EnsureProfilerRecoveryStateMatches(status, changed, status, expected),
            Throws.TypeOf<InvalidOperationException>());
    }
}
