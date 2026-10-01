using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
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
    private static async Task<List<AgentProviderEvent>> CollectAsync(CopilotSubscriptionAgentSession session, AgentTurnRequest request,
        CancellationToken cancellationToken = default)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, cancellationToken)) events.Add(item);
        return events;
    }

#pragma warning disable GHCP001 // Assert only that experimental SDK capabilities stay explicitly disabled.
    private static void AssertProductSessionDefaults(SessionConfigBase config)
    {
        Assert.Multiple(() =>
        {
            Assert.That(config.Streaming, Is.True);
            Assert.That(config.Model, Is.EqualTo("synthetic-model"));
            Assert.That(config.EnableConfigDiscovery, Is.False);
            Assert.That(config.EnableExperimentalMode, Is.False);
            Assert.That(config.EnableSessionTelemetry, Is.False);
            Assert.That(config.EnableFileChangeTracking, Is.False);
            Assert.That(config.SkipEmbeddingRetrieval, Is.True);
            Assert.That(config.SkipCustomInstructions, Is.True);
            Assert.That(config.EnableOnDemandInstructionDiscovery, Is.False);
            Assert.That(config.EnableFileHooks, Is.False);
            Assert.That(config.EnableHostGitOperations, Is.False);
            Assert.That(config.EnableSkills, Is.False);
            Assert.That(config.EnableSessionStore, Is.False);
            Assert.That(config.Memory?.Enabled, Is.False);
            Assert.That(config.ToolSearch?.Enabled, Is.False);
            Assert.That(config.CustomAgentsLocalOnly, Is.True);
            Assert.That(config.EnableMcpApps, Is.False);
            Assert.That(config.RequestCanvasRenderer, Is.False);
            Assert.That(config.RequestExtensions, Is.False);
            Assert.That(config.CoauthorEnabled, Is.False);
            Assert.That(config.ManageScheduleEnabled, Is.False);
            Assert.That(config.PluginDirectories, Is.Empty);
            Assert.That(config.InstructionDirectories, Is.Empty);
            Assert.That(config.SkillDirectories, Is.Empty);
            Assert.That(config.CustomAgents, Is.Empty);
            Assert.That(config.Commands, Is.Empty);
            Assert.That(config.Canvases, Is.Empty);
        });
    }
#pragma warning restore GHCP001

    [Test]
    public async Task ProductSessionDefaultsAreAppliedOnCreateAndResume()
    {
        var createClient = new MemoryCopilotRuntime();
        await using (var created = Session(createClient))
        {
            await CollectAsync(created, Request()).WaitAsync(TimeSpan.FromSeconds(5));
            AssertProductSessionDefaults(createClient.Creates.Single());
        }

        const string reservedId = "reserved-session";
        var resumeClient = new MemoryCopilotRuntime { SessionExists = true };
        var storage = new MemoryCopilotSessionStorage(persistent: true);
        storage.ReserveSession(reservedId);
        await using var resumed = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model")
            {
                ReservedProviderSessionId = reservedId,
                ResumeProviderSessionId = reservedId,
            }, resumeClient, storage);
        await CollectAsync(resumed, Request()).WaitAsync(TimeSpan.FromSeconds(5));
        AssertProductSessionDefaults(resumeClient.Resumes.Single());
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
    public async Task FailedTurnSessionDisposalEndsTheInstanceAndPreservesDeliveryReport()
    {
        var cleanupFailure = new IOException("synthetic session cleanup failure");
        var client = new MemoryCopilotRuntime { SessionDisposalFailure = cleanupFailure };
        var session = Session(client);
        var request = Request();

        var failure = Assert.ThrowsAsync<IOException>(async () => await CollectAsync(session, request));

        Assert.That(failure, Is.SameAs(cleanupFailure));
        Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
        await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await CollectAsync(session, Request()));
        var repeatedFailure = Assert.ThrowsAsync<AggregateException>(async () => await session.DisposeAsync())!;
        Assert.That(repeatedFailure.Flatten().InnerExceptions, Is.EqualTo(new[] { cleanupFailure }));
        Assert.That(client.Disposals, Is.EqualTo(1));
        Assert.That(client.Sessions.Single().Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task FailedTurnCleanupReportsSessionAndClientFailures()
    {
        var sessionFailure = new IOException("synthetic session cleanup failure");
        var clientFailure = new IOException("synthetic client cleanup failure");
        var client = new MemoryCopilotRuntime
        {
            SessionDisposalFailure = sessionFailure,
            DisposalFailure = clientFailure,
        };
        var session = Session(client);
        var request = Request();

        var failure = Assert.ThrowsAsync<AggregateException>(async () => await CollectAsync(session, request))!;

        Assert.That(failure.Flatten().InnerExceptions, Is.EquivalentTo(new[] { sessionFailure, clientFailure }));
        Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
        await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
        Assert.ThrowsAsync<AggregateException>(async () => await session.DisposeAsync());
        Assert.That(client.Disposals, Is.EqualTo(1));
        Assert.That(client.Sessions.Single().Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task ConcurrentDisposalSharesCleanupAndReportsAllFailuresAfterClosingOwnedStore()
    {
        var releaseCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sessionFailure = new IOException("synthetic session cleanup failure");
        var clientFailure = new IOException("synthetic client cleanup failure");
        var storeFailure = new IOException("synthetic store cleanup failure");
        var client = new MemoryCopilotRuntime
        {
            CompleteOnSend = false,
            SessionDisposalFailure = sessionFailure,
            SessionDisposalWait = releaseCleanup.Task,
            DisposalFailure = clientFailure,
        };
        var store = new MemoryCopilotSessionStorage(persistent: false) { DisposalFailure = storeFailure };
        var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model") { PersistProviderSession = false },
            client, store, ownsSessionFsStore: true);
        var request = Request();
        var turn = CollectAsync(session, request);
        var nativeSession = client.Sessions.Single();
        await nativeSession.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var first = session.DisposeAsync().AsTask();
        await nativeSession.DisposalStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = session.DisposeAsync().AsTask();
        Assert.That(first, Is.SameAs(second));
        Assert.That(nativeSession.Disposals, Is.EqualTo(1));
        Assert.That(client.Disposals, Is.Zero);
        releaseCleanup.TrySetResult();

        var failure = Assert.ThrowsAsync<AggregateException>(async () => await first)!;
        var repeatedFailure = Assert.ThrowsAsync<AggregateException>(async () => await second);
        Assert.That(repeatedFailure, Is.SameAs(failure));
        Assert.That(failure.Flatten().InnerExceptions, Is.EquivalentTo(new[] { sessionFailure, clientFailure, storeFailure }));
        var turnFailure = Assert.ThrowsAsync<AggregateException>(async () => await turn.WaitAsync(TimeSpan.FromSeconds(5)))!;
        Assert.That(turnFailure.Flatten().InnerExceptions, Is.EquivalentTo(new[] { sessionFailure, clientFailure, storeFailure }));
        Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
        await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
        Assert.That(client.Disposals, Is.EqualTo(1));
        Assert.That(store.Disposals, Is.EqualTo(1));
        Assert.That(nativeSession.Disposals, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DisposalWaitsForPendingCreateOrResumeAndClosesTheLateHandleWithoutSending(bool resume)
    {
        var releaseAcquisition = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new MemoryCopilotRuntime
        {
            SessionAcquisitionWait = releaseAcquisition.Task,
            SessionExists = resume,
        };
        var store = new MemoryCopilotSessionStorage(persistent: true);
        var options = new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "synthetic-model")
        {
            ReservedProviderSessionId = "reserved-id",
            ResumeProviderSessionId = resume ? "reserved-id" : null,
        };
        var session = new CopilotSubscriptionAgentSession(new NoTools(), options, client, store, ownsSessionFsStore: true);
        var request = Request();
        var turn = CollectAsync(session, request);
        await client.SessionAcquisitionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var disposal = session.DisposeAsync().AsTask();
        Assert.That(disposal.IsCompleted, Is.False);
        Assert.That(client.Disposals, Is.Zero);
        Assert.That(store.Disposals, Is.Zero);
        Assert.That(client.Sessions, Is.Empty);
        releaseAcquisition.TrySetResult();

        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await turn.WaitAsync(TimeSpan.FromSeconds(5));
        var nativeSession = client.Sessions.Single();
        Assert.That(nativeSession.Disposals, Is.EqualTo(1));
        Assert.That(nativeSession.Operations, Does.Not.Contain("send"));
        Assert.That(client.Disposals, Is.EqualTo(1));
        Assert.That(store.Disposals, Is.EqualTo(1));
        Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.NothingSent));
        await session.DisposeAsync();
        Assert.That(nativeSession.Disposals, Is.EqualTo(1));
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

    [Test]
    public async Task CancellationFromEstablishedObserverDoesNotInvokeSendOrClaimPossibleEffects()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new MemoryCopilotRuntime();
        var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model")
            {
                ProviderSessionObserver = update =>
                {
                    if (update.Change == AgentProviderSessionChange.Established) cancellation.Cancel();
                },
            }, client);
        await using (session)
        {
            var request = Request();
            await CollectAsync(session, request, cancellation.Token).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Multiple(() =>
            {
                Assert.That(client.Sessions.Single().SendInvocations, Is.Zero);
                Assert.That(client.Sessions.Single().Operations, Is.EqualTo(RestrictedOnly));
                Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.NothingSent));
            });
        }
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
