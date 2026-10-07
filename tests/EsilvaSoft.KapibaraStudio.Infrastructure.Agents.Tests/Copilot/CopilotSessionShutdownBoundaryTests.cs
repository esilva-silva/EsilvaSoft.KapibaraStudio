using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture, Category("Unit")]
internal sealed class CopilotSessionShutdownBoundaryTests
{
    private const string Canary = "private-shutdown-account-path-canary";

    [Test]
    public async Task ShutdownAfterIdleCannotConvertAnAlreadyCompletedTurnToFailure()
    {
        var client = new MemoryCopilotRuntime { CompleteOnSend = false };
        await using var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
        var request = Request("completed synthetic message");
        var pending = CollectAsync(session, request);
        var native = client.Sessions.Single();
        await native.Sent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var callback = native.CaptureEventHandler();
        try
        {
            callback(new SessionIdleEvent { Data = new() { Mode = SessionMode.Interactive } });
            callback(new SessionShutdownEvent { Data = null! });
            var events = await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Multiple(() =>
            {
                Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.AgentError), Is.False);
                Assert.That(native.SendInvocations, Is.EqualTo(1));
                Assert.That(native.Disposals, Is.EqualTo(1));
            });
        }
        finally { await session.CancelTurnAsync(request.TurnId, CancellationToken.None); }
    }

    [TestCase("error")]
    [TestCase("routine")]
    [TestCase(null)]
    public async Task ShutdownBeforeIdleEndsAmbiguousTurnWithoutReplayAndRequiresExplicitNewTurn(string? shutdownType)
    {
        const string reservedId = "shutdown-recovery-session";
        var client = new MemoryCopilotRuntime { CompleteOnSend = false };
        await using var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model") { ReservedProviderSessionId = reservedId }, client);
        var firstRequest = Request("first synthetic message");
        var pending = CollectAsync(session, firstRequest);
        var native = client.Sessions.Single();
        await native.Sent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var callback = native.CaptureEventHandler();
        callback(new SessionShutdownEvent
        {
            Data = shutdownType is null ? null! : new SessionShutdownData
            {
                ShutdownType = new ShutdownType(shutdownType), ErrorReason = Canary, CurrentModel = Canary,
                CodeChanges = new() { FilesModified = [], LinesAdded = 0, LinesRemoved = 0 },
                ModelMetrics = new Dictionary<string, ShutdownModelMetric>(), SessionStartTime = 0, TotalApiDuration = TimeSpan.Zero,
            },
        });
        List<AgentProviderEvent> events;
        var completedWithoutExternalCancellation = true;
        try
        {
            events = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            completedWithoutExternalCancellation = false;
            await session.CancelTurnAsync(firstRequest.TurnId, CancellationToken.None);
            events = await pending.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            await session.CancelTurnAsync(firstRequest.TurnId, CancellationToken.None);
        }

        callback(new AssistantMessageDeltaEvent { Data = new() { MessageId = "late", DeltaContent = Canary } });
        callback(new SessionErrorEvent { Data = new() { ErrorType = "quota", Message = Canary } });
        Assert.Multiple(() =>
        {
            Assert.That(completedWithoutExternalCancellation, Is.True,
                "A session termination must end the active stream without waiting for the host timeout.");
            Assert.That(events.Count(item => item.Kind == AgentEventKind.AgentError), Is.EqualTo(1));
            Assert.That(events.SingleOrDefault(item => item.Kind == AgentEventKind.AgentError)?.Text,
                Is.EqualTo("CopilotSessionStreamFailed"));
            Assert.That(events.Any(item => item.Text?.Contains(Canary, StringComparison.Ordinal) == true), Is.False);
            Assert.That(session.GetCancellationReport(firstRequest.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
            Assert.That(client.Creates, Has.Count.EqualTo(1));
            Assert.That(client.Resumes, Is.Empty, "No automatic retry or resumed work may follow an ambiguous shutdown.");
            Assert.That(native.SendInvocations, Is.EqualTo(1));
            Assert.That(native.Disposals, Is.EqualTo(1));
        });

        // Only a distinct, explicit turn may resume the known ID, with continuation of pending work disabled.
        client.SessionExists = true;
        client.CompleteOnSend = true;
        var secondRequest = Request("new explicit synthetic message");
        var recovered = await CollectAsync(session, secondRequest).WaitAsync(TimeSpan.FromSeconds(2));
        callback(new AssistantMessageDeltaEvent { Data = new() { MessageId = "old-turn", DeltaContent = Canary } });
        Assert.Multiple(() =>
        {
            Assert.That(recovered.Any(item => item.Kind == AgentEventKind.MessageDelta), Is.True);
            Assert.That(recovered.Any(item => item.Kind == AgentEventKind.AgentError), Is.False);
            Assert.That(recovered.Any(item => item.Text?.Contains(Canary, StringComparison.Ordinal) == true), Is.False);
            Assert.That(client.Creates, Has.Count.EqualTo(1));
            Assert.That(client.Resumes, Has.Count.EqualTo(1));
            Assert.That(client.Resumes[0].ContinuePendingWork, Is.False);
            Assert.That(client.Sessions, Has.Count.EqualTo(2));
            Assert.That(client.Sessions.All(item => item.SessionId == reservedId && item.SendInvocations == 1), Is.True);
        });
    }

    private static AgentTurnRequest Request(string message) => new(AgentTurnId.New(), message, "synthetic-tab", 1)
    {
        Plan = new(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None),
    };

    private static async Task<List<AgentProviderEvent>> CollectAsync(CopilotSubscriptionAgentSession session, AgentTurnRequest request)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);
        return events;
    }

    private sealed class NoTools : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => null;
        public string? GetOutputSchemaJson(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
