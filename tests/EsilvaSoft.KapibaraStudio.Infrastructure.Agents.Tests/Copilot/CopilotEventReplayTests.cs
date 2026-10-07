using System.Reflection;
using System.Threading.Channels;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture, Category("Unit")]
internal sealed class CopilotEventReplayTests
{
    private static readonly Type SessionType = typeof(CopilotSubscriptionAgentSession);
    private static readonly Type TurnType = SessionType.GetNestedType("ActiveTurn", BindingFlags.NonPublic)!;
    private static readonly MethodInfo EventHandler = SessionType.GetMethod("OnEvent", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly HashSet<string> NoTools = new(StringComparer.Ordinal);
    private static readonly AgentEventKind[] CompletedWithTwoDeltas =
        [AgentEventKind.MessageStarted, AgentEventKind.MessageDelta, AgentEventKind.MessageDelta, AgentEventKind.MessageCompleted];

    [Test]
    public void ReplayedDeltaIsIgnoredButIdenticalTextFromAnotherEventIsPreserved()
    {
        using var turn = CreateTurn();
        var replay = Delta(Guid.NewGuid(), "same text");
        Dispatch(turn, replay);
        Dispatch(turn, replay);
        Dispatch(turn, Delta(Guid.NewGuid(), "same text"));
        var final = new AssistantMessageEvent
        {
            Id = Guid.NewGuid(), Data = new() { MessageId = "message", Content = "same textsame text" },
        };
        Dispatch(turn, final);
        Dispatch(turn, final);
        Dispatch(turn, Delta(Guid.NewGuid(), "late text"));

        var events = ReadEvents(turn);
        Assert.Multiple(() =>
        {
            Assert.That(events.Select(item => item.Kind), Is.EqualTo(CompletedWithTwoDeltas));
            Assert.That(string.Concat(events.Where(item => item.Kind == AgentEventKind.MessageDelta).Select(item => item.Text)),
                Is.EqualTo("same textsame text"));
            Assert.That(events.Select(item => item.MessageId).Distinct().Count(), Is.EqualTo(1));
            Assert.That(Done(turn).IsCompleted, Is.False);
        });
    }

    [Test]
    public void ReplayedOfficialUsageEventIsPublishedOnceAndLateUsageCannotUpdateTheCompletedTurn()
    {
        using var turn = CreateTurn();
        var usage = new AssistantUsageEvent
        {
            Id = Guid.NewGuid(),
            Data = new AssistantUsageData
            {
                ApiCallId = "model-call-usage-1",
                Model = "fixture-model",
                InputTokens = 0,
                OutputTokens = null,
                CacheReadTokens = null,
            },
        };
        Dispatch(turn, usage);
        Dispatch(turn, usage);
        Dispatch(turn, Idle());
        Dispatch(turn, new AssistantUsageEvent
        {
            Id = Guid.NewGuid(),
            Data = new AssistantUsageData
            { ApiCallId = "late-model-call", Model = "fixture-model", InputTokens = 77, OutputTokens = 2 },
        });

        var events = ReadEvents(turn);
        var metric = events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(metric.Kind, Is.EqualTo(AgentEventKind.UsageUpdated));
            Assert.That(metric.Usage!.ObservationId, Is.EqualTo("model-call-usage-1"));
            Assert.That(metric.Usage.Scope, Is.EqualTo(AgentUsageScope.CallTotal));
            Assert.That(metric.Usage.InputTokens, Is.Zero, "A reported zero is known, not missing.");
            Assert.That(metric.Usage.OutputTokens, Is.Null, "An absent field remains unknown.");
            Assert.That(metric.Usage.CacheReadTokens, Is.Null, "Missing cache data remains unknown.");
            Assert.That(Done(turn).IsCompleted, Is.True);
        });
    }

    [Test]
    public async Task ConcurrentReplaysAreMappedOnce()
    {
        using var turn = CreateTurn();
        var replay = Delta(Guid.NewGuid(), "concurrent text");
        await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => Dispatch(turn, replay))));
        var events = ReadEvents(turn);
        Assert.Multiple(() =>
        {
            Assert.That(events.Count(item => item.Kind == AgentEventKind.MessageStarted), Is.EqualTo(1));
            Assert.That(events.Count(item => item.Kind == AgentEventKind.MessageDelta), Is.EqualTo(1));
            Assert.That(events.Any(item => item.Kind == AgentEventKind.AgentError), Is.False);
        });
    }

    [Test]
    public void ExactIdentityLimitAcceptsKnownReplaysThenOverflowFailsClosedWithoutEviction()
    {
        using var turn = CreateTurn();
        var first = new AssistantMessageStartEvent { Id = Guid.NewGuid(), Data = new() { MessageId = "message" } };
        Dispatch(turn, first);
        for (var index = 1; index < CopilotSubscriptionAgentSession.MaximumSdkEventIdsPerTurn; index++)
            Dispatch(turn, new AssistantMessageStartEvent { Id = Guid.NewGuid(), Data = new() { MessageId = "message" } });

        Assert.That(Done(turn).IsCompleted, Is.False, "The exact limit must be allowed.");
        Assert.That(ReadEvents(turn).Single().Kind, Is.EqualTo(AgentEventKind.MessageStarted));
        Dispatch(turn, first);
        Assert.That(Done(turn).IsCompleted, Is.False, "A replay at the limit must not consume another slot.");
        Assert.That(ReadEvents(turn), Is.Empty);

        var overflow = Delta(Guid.NewGuid(), "private-overflow-content");
        Dispatch(turn, overflow);
        Dispatch(turn, overflow);
        Dispatch(turn, first);
        Dispatch(turn, Delta(Guid.NewGuid(), "private-late-content"));
        var error = ReadEvents(turn).Single();
        Assert.Multiple(() =>
        {
            Assert.That(Done(turn).IsCompleted, Is.True);
            Assert.That(error.Kind, Is.EqualTo(AgentEventKind.AgentError));
            Assert.That(error.Text, Is.EqualTo("CopilotSessionStreamFailed"));
            Assert.That(error.MessageId, Is.Null);
        });
    }

    [Test]
    public void MissingIdentityDoesNotInventDeduplicationEvidence()
    {
        using var turn = CreateTurn();
        Dispatch(turn, Delta(Guid.Empty, "unidentified text"));
        Dispatch(turn, Delta(Guid.Empty, "unidentified text"));
        Assert.That(ReadEvents(turn).Count(item => item.Kind == AgentEventKind.MessageDelta), Is.EqualTo(2));
    }

    [TestCase("idle")]
    [TestCase("error")]
    [TestCase("cancel")]
    public async Task CapturedCallbackAfterTerminalStaysOnOriginatingTurnAndFreshTurnAcceptsReusedId(string terminal)
    {
        var client = new MemoryCopilotRuntime { CompleteOnSend = false };
        await using var session = new CopilotSubscriptionAgentSession(new EmptyRegistry(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
        var firstRequest = Request();
        var firstRun = CollectAsync(session, firstRequest);
        var firstNative = client.Sessions.Single();
        await firstNative.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var obsoleteHandler = firstNative.CaptureEventHandler();
        var reusedId = Guid.NewGuid();
        firstNative.Emit(Delta(reusedId, "first turn"));
        if (terminal == "cancel") await session.CancelTurnAsync(firstRequest.TurnId, CancellationToken.None);
        else firstNative.Emit(terminal == "idle" ? Idle() : Error());
        var firstEvents = await firstRun.WaitAsync(TimeSpan.FromSeconds(5));

        var secondRequest = Request();
        var secondRun = CollectAsync(session, secondRequest);
        var secondNative = client.Sessions.Last();
        await secondNative.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            obsoleteHandler(Delta(Guid.NewGuid(), "private-obsolete-content"));
            obsoleteHandler(Error());
            obsoleteHandler(Idle());
            Assert.That(secondRun.IsCompleted, Is.False, "An old terminal callback cannot finish the current turn.");
            secondNative.Emit(Delta(reusedId, "second turn"));
            secondNative.Emit(Idle());
            var secondEvents = await secondRun.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(firstEvents.Count(item => item.Kind == AgentEventKind.MessageDelta && item.Text == "first turn"), Is.EqualTo(1));
                Assert.That(secondEvents.Count(item => item.Kind == AgentEventKind.MessageDelta && item.Text == "second turn"), Is.EqualTo(1));
                Assert.That(secondEvents.Any(item => item.Kind == AgentEventKind.AgentError || item.Text == "private-obsolete-content"), Is.False);
                Assert.That(secondEvents.Where(item => item.Kind == AgentEventKind.MessageDelta).Select(item => item.MessageId).Distinct().Count(), Is.EqualTo(1));
                Assert.That(firstEvents.First(item => item.Kind == AgentEventKind.MessageDelta).MessageId,
                    Is.Not.EqualTo(secondEvents.First(item => item.Kind == AgentEventKind.MessageDelta).MessageId));
                Assert.That(firstNative.Disposals, Is.EqualTo(1));
                Assert.That(secondNative.Disposals, Is.EqualTo(1));
                Assert.That(session.GetCancellationReport(secondRequest.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
            });
        }
        finally
        {
            await session.CancelTurnAsync(secondRequest.TurnId, CancellationToken.None);
            await secondRun.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static SessionIdleEvent Idle() => new() { Id = Guid.NewGuid(), Data = new() { Mode = SessionMode.Interactive } };
    private static SessionErrorEvent Error() => new()
    {
        Id = Guid.NewGuid(), Data = new() { ErrorType = "quota", Message = "private-error-content" },
    };
    private static AgentTurnRequest Request() => new(AgentTurnId.New(), "synthetic prompt", "synthetic-tab", 1)
    {
        Plan = new(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None),
    };

    private static async Task<List<AgentProviderEvent>> CollectAsync(CopilotSubscriptionAgentSession session, AgentTurnRequest request)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);
        return events;
    }

    private sealed class EmptyRegistry : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => null;
        public string? GetOutputSchemaJson(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No tool is planned.");
    }

    private static AssistantMessageDeltaEvent Delta(Guid id, string text) => new()
    {
        Id = id, Data = new() { MessageId = "message", DeltaContent = text },
    };

    private static IDisposable CreateTurn() => (IDisposable)Activator.CreateInstance(TurnType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null,
        args: [AgentTurnId.New(), CancellationToken.None], culture: null)!;

    private static void Dispatch(object turn, SessionEvent evt) => EventHandler.Invoke(null, [turn, evt, NoTools]);
    private static Task Done(object turn) => ((TaskCompletionSource)TurnType.GetProperty("Done")!.GetValue(turn)!).Task;

    private static List<AgentProviderEvent> ReadEvents(object turn)
    {
        var channel = (Channel<AgentProviderEvent>)TurnType.GetProperty("Events")!.GetValue(turn)!;
        var events = new List<AgentProviderEvent>();
        while (channel.Reader.TryRead(out var item)) events.Add(item);
        return events;
    }
}
