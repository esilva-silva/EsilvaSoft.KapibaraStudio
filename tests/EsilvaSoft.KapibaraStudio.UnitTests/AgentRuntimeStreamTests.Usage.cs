using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

public sealed partial class AgentRuntimeStreamTests
{
    [Test]
    public async Task UsageIsValidatedDeduplicatedAndBoundToEachOriginatingSessionAndTurn()
    {
        var provider = new ScriptedProvider((_, _, _) => Script());
        await using var runtime = new AgentRuntime([provider]);
        var sessionA = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var sessionB = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turnA = AgentTurnId.New();
        var turnB = AgentTurnId.New();
        var results = await Task.WhenAll(
            CollectAsync(runtime.RunTurnAsync(sessionA, Request(turnA), CancellationToken.None)),
            CollectAsync(runtime.RunTurnAsync(sessionB, Request(turnB), CancellationToken.None)));
        for (var index = 0; index < results.Length; index++)
        {
            var events = results[index];
            AssertStreamInvariants(events);
            var metrics = events.Where(static item => item.Kind == AgentEventKind.UsageUpdated).ToArray();
            Assert.That(metrics, Has.Length.EqualTo(2));
            Assert.That(metrics.Select(static item => item.Usage!.Revision), Is.EqualTo(new long[] { 2, 3 }));
            Assert.That(metrics.Select(static item => item.SessionId), Is.All.EqualTo(index == 0 ? sessionA : sessionB));
            Assert.That(metrics.Select(static item => item.TurnId), Is.All.EqualTo(index == 0 ? turnA : turnB));
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        }

        static async IAsyncEnumerable<AgentProviderEvent> Script()
        {
            await Task.Yield();
            var observation = new AgentUsageMetrics("call", AgentUsageScope.CallTotal, 2, "fixture", 10, 0);
            yield return new(AgentEventKind.UsageUpdated) { Usage = observation };
            yield return new(AgentEventKind.UsageUpdated) { Usage = observation };
            yield return new(AgentEventKind.UsageUpdated) { Usage = observation with { Revision = 1 } };
            yield return new(AgentEventKind.UsageUpdated) { Usage = observation with { Revision = 3, InputTokens = -1 } };
            yield return new(AgentEventKind.UsageUpdated) { Usage = observation with { Revision = 3, InputTokens = 20 } };
        }
    }
}
