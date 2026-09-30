using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture, Category("Unit")]
internal sealed class CopilotSessionIsolationTests
{
    private static readonly string[] RestrictedSend = ["restrict", "send"];
    private static readonly string[] RestrictedOnly = ["restrict"];
    private sealed class NoTools : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => null;
        public string? GetOutputSchemaJson(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No tool is planned.");
    }
    private static AgentTurnRequest Request() => new(AgentTurnId.New(), "synthetic prompt", "synthetic-tab", 1)
    {
        Plan = new(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None)
    };
    private static CopilotSubscriptionAgentSession Session(MemoryCopilotRuntime client) =>
        new(new NoTools(), new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
    private static async Task<List<AgentProviderEvent>> CollectAsync(CopilotSubscriptionAgentSession session, AgentTurnRequest request)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);
        return events;
    }

    [Test]
    public async Task StreamingRestrictsAgentsBeforeSendAndDisposesOwnedRuntime()
    {
        var client = new MemoryCopilotRuntime();
        var session = Session(client);
        var events = await CollectAsync(session, Request()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta && item.Text == "synthetic response"), Is.True);
        Assert.That(client.Sessions.Single().Operations, Is.EqualTo(RestrictedSend));
        Assert.That(client.Creates.Single().AvailableTools, Is.Not.Null);
        await session.DisposeAsync();
        await session.DisposeAsync();
        Assert.That(client.Disposals, Is.EqualTo(1));
        Assert.That(client.Sessions.Single().Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task FailedAgentRestrictionDoesNotSendPrompt()
    {
        var client = new MemoryCopilotRuntime { RestrictionSucceeds = false };
        await using var session = Session(client);
        var events = await CollectAsync(session, Request());
        Assert.That(events.Any(item => item.Kind == AgentEventKind.AgentError), Is.True);
        Assert.That(client.Sessions.Single().Operations, Is.EqualTo(RestrictedOnly));
    }

    [TestCase(false, "user")]
    [TestCase(true, "token")]
    public async Task InvalidAuthenticationDoesNotCreateSession(bool authenticated, string type)
    {
        var client = new MemoryCopilotRuntime { Authentication = new(authenticated, type) };
        await using var session = Session(client);
        var events = await CollectAsync(session, Request());
        Assert.That(events.Any(item => item.Kind == AgentEventKind.AgentError), Is.True);
        Assert.That(client.Creates, Is.Empty);
    }

    [Test]
    public async Task MissingPlanDoesNotStartRuntime()
    {
        var client = new MemoryCopilotRuntime();
        await using var session = Session(client);
        var events = await CollectAsync(session, Request() with { Plan = null });
        Assert.That(events.Single().Text, Is.EqualTo("CopilotTurnPlanMissing"));
        Assert.That(client.Starts, Is.Zero);
    }

    [Test]
    public async Task CancellationIsScopedToTheMatchingTurnAndReportsPossibleEffects()
    {
        var client = new MemoryCopilotRuntime { CompleteOnSend = false };
        await using var session = Session(client);
        var request = Request();
        var pending = CollectAsync(session, request);
        await client.Sessions.Single().Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await session.CancelTurnAsync(AgentTurnId.New(), CancellationToken.None);
        Assert.That(client.Sessions.Single().Aborts, Is.Zero);
        await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        // Explicit cancellation and stream cleanup may both request a best-effort abort of this same session.
        Assert.That(client.Sessions.Single().Aborts, Is.GreaterThanOrEqualTo(1));
        Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
    }

    [Test]
    public async Task CancellingOneSessionDoesNotAbortAnotherConcurrentSession()
    {
        var firstClient = new MemoryCopilotRuntime { CompleteOnSend = false };
        var secondClient = new MemoryCopilotRuntime { CompleteOnSend = false };
        await using var first = Session(firstClient);
        await using var second = Session(secondClient);
        var firstRequest = Request();
        var firstTurn = CollectAsync(first, firstRequest);
        var secondTurn = CollectAsync(second, Request());
        await firstClient.Sessions.Single().Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await secondClient.Sessions.Single().Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await first.CancelTurnAsync(firstRequest.TurnId, CancellationToken.None);
        await firstTurn.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(secondClient.Sessions.Single().Aborts, Is.Zero);
        Assert.That(secondTurn.IsCompleted, Is.False);
        secondClient.Sessions.Single().Emit(new GitHub.Copilot.SessionIdleEvent
        {
            Data = new GitHub.Copilot.SessionIdleData { Mode = GitHub.Copilot.SessionMode.Interactive }
        });
        await secondTurn.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(secondClient.Sessions.Single().Aborts, Is.Zero);
    }

    [Test]
    public async Task OptOutRejectsStalePersistentIdAndCreatesTrackedVolatileSession()
    {
        var client = new MemoryCopilotRuntime();
        var storage = new MemoryCopilotSessionStorage(persistent: false);
        var notices = new List<AgentProviderSessionUpdate>();
        await using var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model")
            {
                PersistProviderSession = false, ResumeProviderSessionId = "stale-id", ProviderSessionObserver = notices.Add
            }, client, storage);
        await CollectAsync(session, Request());
        Assert.That(client.Resumes, Is.Empty);
        var id = client.Creates.Single().SessionId!;
        Assert.That(id, Is.Not.EqualTo("stale-id"));
        Assert.That(storage.ContainsSession(id), Is.True);
        Assert.That(storage.Configurations, Is.EqualTo(1));
        Assert.That(notices.Any(item => item.Change == AgentProviderSessionChange.ResumeFallback), Is.True);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ReservedSessionResumeFailureNeverCreatesAnUntrackedReplacement(bool exists)
    {
        var client = new MemoryCopilotRuntime { SessionExists = exists, ResumeFails = true };
        var storage = new MemoryCopilotSessionStorage(persistent: true);
        await using var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model")
            {
                ReservedProviderSessionId = "reserved-id", ResumeProviderSessionId = "reserved-id"
            }, client, storage);
        var events = await CollectAsync(session, Request());
        if (exists)
        {
            Assert.That(client.Resumes, Has.Count.EqualTo(1));
            Assert.That(client.Creates, Is.Empty);
            Assert.That(events.Any(item => item.Kind == AgentEventKind.AgentError), Is.True);
        }
        else
        {
            Assert.That(client.Resumes, Is.Empty);
            Assert.That(client.Creates.Single().SessionId, Is.EqualTo("reserved-id"));
            Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta), Is.True);
        }
    }
}
