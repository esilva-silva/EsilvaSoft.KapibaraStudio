using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class OperationKillCoordinatorTests
{
    private static readonly string[] ExpectedSteps = ["read", "kill"];
    private const string Captured = """{"inprog":[{"opid":45,"host":"server-a:27017","connectionId":5,"op":"query","ns":"db.items","command":{"find":"items"}}]}""";
    private const string Moved = """{"inprog":[{"opid":45,"host":"server-b:27017","connectionId":5,"op":"query","ns":"db.items","command":{"find":"items"}}]}""";

    [Test]
    public void ChangedServerRefusesKillBeforeDispatch()
    {
        var request = Request();
        var killCalls = 0;

        Assert.ThrowsAsync<InvalidOperationException>(async () => await OperationKillCoordinator.ExecuteAsync(request,
            _ => Task.FromResult(Moved),
            (_, _) => { killCalls++; return Task.CompletedTask; },
            CancellationToken.None));
        Assert.That(killCalls, Is.Zero);
    }

    [Test]
    public async Task MatchingRereadDispatchesOnlyCapturedNumericId()
    {
        var request = Request();
        long? dispatched = null;
        var steps = new List<string>();

        await OperationKillCoordinator.ExecuteAsync(request,
            _ => { steps.Add("read"); return Task.FromResult(Captured); },
            (id, _) => { steps.Add("kill"); dispatched = id; return Task.CompletedTask; },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(dispatched, Is.EqualTo(45));
            Assert.That(steps, Is.EqualTo(ExpectedSteps));
        });
    }

    [Test]
    public void CancellationAfterRereadPreventsDispatch()
    {
        var request = Request();
        using var cancellation = new CancellationTokenSource();
        var killCalls = 0;

        Assert.ThrowsAsync<OperationCanceledException>(async () => await OperationKillCoordinator.ExecuteAsync(request,
            _ => { cancellation.Cancel(); return Task.FromResult(Captured); },
            (_, _) => { killCalls++; return Task.CompletedTask; },
            cancellation.Token));
        Assert.That(killCalls, Is.Zero);
    }

    private static OperationKillRequest Request() => new("45", "45",
        OperationKillRequest.FindFingerprint(Captured, 45)!);
}
