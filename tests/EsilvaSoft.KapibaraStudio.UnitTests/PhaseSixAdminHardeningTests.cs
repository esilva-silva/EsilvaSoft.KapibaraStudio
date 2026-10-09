using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class PhaseSixAdminHardeningTests
{
    private const string CapturedOperation = """{"inprog":[{"opid":12345,"host":"mongo-a:27017","op":"query","ns":"sample_mflix.movies","client":"client:1","command":{"find":"movies","filter":{"year":2000}}}]}""";

    [Test]
    public async Task KillIsSentOnlyAfterMatchingOperationIsReRead()
    {
        var request = CreateRequest(CapturedOperation);
        long? killed = null;

        await OperationKillCoordinator.ExecuteAsync(
            request,
            _ => Task.FromResult(CapturedOperation),
            (id, _) => { killed = id; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.That(killed, Is.EqualTo(12345));
    }

    [TestCase("""{"inprog":[{"opid":12345,"host":"mongo-a:27017","op":"query","ns":"sample_mflix.movies","client":"client:1","command":{"find":"movies","filter":{"year":2001}}}]}""")]
    [TestCase("""{"inprog":[{"opid":12345,"host":"mongo-b:27017","op":"query","ns":"sample_mflix.movies","client":"client:1","command":{"find":"movies","filter":{"year":2000}}}]}""")]
    [TestCase("""{"inprog":[{"opid":12345,"host":"mongo-a:27017","op":"query","ns":"sample_mflix.movies","client":"client:1","command":{"find":"movies","filter":{"year":2000}}}],"truncated":true}""")]
    public async Task ChangedServerIdentityOperationOrTruncatedReadRefusesKill(string current)
    {
        var request = CreateRequest(CapturedOperation);
        var killed = false;

        Assert.ThrowsAsync<InvalidOperationException>(() => OperationKillCoordinator.ExecuteAsync(
            request,
            _ => Task.FromResult(current),
            (_, _) => { killed = true; return Task.CompletedTask; },
            CancellationToken.None));

        Assert.That(killed, Is.False);
    }

    [Test]
    public void RuntimeParameterRequiresAllowlistedBoundedConfirmedValues()
    {
        var valid = new RuntimeServerParameterRequest("logLevel", 0, 5, "logLevel");
        Assert.That(valid.Validate(), Is.SameAs(valid));
        var logSize = new RuntimeServerParameterRequest("maxLogSizeKB", 20, 10, "maxLogSizeKB");
        Assert.That(logSize.Validate(), Is.SameAs(logSize));

        Assert.Throws<ArgumentException>(() => (valid with { ParameterName = "startupParameter" }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (valid with { RequestedValue = 6 }).Validate());
        Assert.Throws<ArgumentException>(() => (valid with { ConfirmationParameterName = "5" }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (logSize with { RequestedValue = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (logSize with { RequestedValue = 11 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (logSize with { PreviousValue = -1 }).Validate());
        Assert.Throws<ArgumentException>(() => (logSize with { ConfirmationParameterName = "logLevel" }).Validate());
    }

    private static OperationKillRequest CreateRequest(string operations)
    {
        var fingerprint = OperationKillRequest.FindFingerprint(operations, 12345)!;
        return new OperationKillRequest("12345", "12345", fingerprint);
    }
}
