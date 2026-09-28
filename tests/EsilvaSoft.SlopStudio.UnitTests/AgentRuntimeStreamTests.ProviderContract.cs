using System.Runtime.CompilerServices;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// P7-CL3-05: additive provider contract. Display-only observations of provider-native tools, the adapter's
/// cancellation report (Cancelled vs OutcomeUnknown) and typed adapter error codes, without weakening registry,
/// approval, deduplication or terminal guarantees.
/// </summary>
public sealed partial class AgentRuntimeStreamTests
{
    private static readonly string[] UnconfirmedPair = ["ObservedToolUnconfirmed", "ObservedToolUnconfirmed"];
    private static readonly string[] ExpectedPublishedCodes = ["ClaudeCodeNotLoggedIn", "ProviderError", "ProviderError"];

    [Test]
    public async Task DeclaredNativeToolObservationsArePublishedWithNameAndStateOnlyUnderRuntimeIds()
    {
        var read1 = AgentToolCallId.New();
        var read2 = AgentToolCallId.New();
        var glob = AgentToolCallId.New();
        var provider = new ContractProvider(_ => Script(), observable: ["Read", "Glob"]);
        await using var runtime = new AgentRuntime([provider]);
        var sessionId = await runtime.StartSessionAsync(new("contract"), CancellationToken.None);
        var turn = AgentTurnId.New();

        var events = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(turn), CancellationToken.None));

        AssertStreamInvariants(events);
        var tools = events.Where(static item => item.ToolCallId is not null).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(tools.Select(static item => (item.Kind, item.ToolName, item.ToolStatus)), Is.EqualTo(new (AgentEventKind, string?, AgentToolResultStatus?)[]
            {
                (AgentEventKind.ToolRequested, "Read", null), (AgentEventKind.ToolStarted, "Read", null),
                (AgentEventKind.ToolCompleted, "Read", AgentToolResultStatus.Succeeded),
                (AgentEventKind.ToolRequested, "Read", null), (AgentEventKind.ToolStarted, "Read", null),
                (AgentEventKind.ToolFailed, "Read", AgentToolResultStatus.Failed),
                (AgentEventKind.ToolRequested, "Glob", null), (AgentEventKind.ToolStarted, "Glob", null),
                (AgentEventKind.ToolCompleted, "Glob", AgentToolResultStatus.Succeeded),
            }));
            Assert.That(tools.Select(static item => item.ToolOrigin), Is.All.EqualTo(AgentToolOrigin.ProviderObserved));
            Assert.That(tools.Select(static item => item.ToolDestination), Is.All.EqualTo(AgentDataDestinationKind.External));
            Assert.That(tools.Select(static item => item.ToolCallId), Has.None.EqualTo(read1).And.None.EqualTo(read2).And.None.EqualTo(glob),
                "Adapter call IDs never become protocol identifiers.");
            Assert.That(tools.Select(static item => item.ToolCallId).Distinct().Count(), Is.EqualTo(3));
            Assert.That(tools.Single(static item => item.Kind == AgentEventKind.ToolFailed).ErrorCode, Is.EqualTo("NativeToolFailed"));
            Assert.That(tools.Select(static item => item.Text), Is.All.Null);
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        });

        // An observation is never answerable: a forged result for its published ID is refused like any unknown call.
        var forged = new AgentToolResult(sessionId, turn, tools[0].ToolCallId!.Value, AgentToolResultStatus.Succeeded, "{}");
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.SubmitToolResultAsync(forged, CancellationToken.None))!.Code, Is.EqualTo("UnknownToolCall"));

        async IAsyncEnumerable<AgentProviderEvent> Script()
        {
            await Task.Yield();
            yield return new(AgentEventKind.ToolStarted, ToolCallId: read1, ToolName: "Read");
            yield return new(AgentEventKind.ToolCompleted, ToolCallId: read1, ToolName: "Read");
            yield return new(AgentEventKind.ToolStarted, ToolCallId: read2, ToolName: "Read");
            yield return new(AgentEventKind.ToolFailed, "NativeToolFailed", ToolCallId: read2, ToolName: "Read");
            yield return new(AgentEventKind.ToolStarted, ToolCallId: glob, ToolName: "Glob");
            yield return new(AgentEventKind.ToolCompleted, ToolCallId: glob, ToolName: "Glob");
        }
    }

    [Test]
    public async Task ObservationsWithArgumentsContentUndeclaredNamesOrBrokenLifecycleAreDiscarded()
    {
        var withArgs = AgentToolCallId.New();
        var withText = AgentToolCallId.New();
        var undeclared = AgentToolCallId.New();
        var duplicate = AgentToolCallId.New();
        var renamed = AgentToolCallId.New();
        var badCode = AgentToolCallId.New();
        var provider = new ContractProvider(_ => Script(), observable: ["Read", "mcp__slop__find", "bad name", ""]);
        await using var runtime = new AgentRuntime([provider]);
        var sessionId = await runtime.StartSessionAsync(new("contract"), CancellationToken.None);

        var events = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(AgentTurnId.New()), CancellationToken.None));

        AssertStreamInvariants(events);
        var tools = events.Where(static item => item.ToolCallId is not null).ToArray();
        Assert.Multiple(() =>
        {
            // Only the duplicate (started once, completed once), the renamed and the bad-code starts are well formed.
            Assert.That(tools.Count(static item => item.Kind == AgentEventKind.ToolRequested), Is.EqualTo(3));
            Assert.That(tools.Select(static item => item.ToolName), Is.All.EqualTo("Read"));
            Assert.That(tools.Count(static item => item.Kind == AgentEventKind.ToolCompleted), Is.EqualTo(1));
            Assert.That(tools.Where(static item => item.Kind == AgentEventKind.ToolFailed).Select(static item => item.ErrorCode),
                Is.EqualTo(UnconfirmedPair),
                "Unfinished observations close at the turn terminal as unconfirmed, never as success.");
            Assert.That(events.Where(static item => item.Text is not null).Select(static item => item.Text),
                Has.None.Contains("C:\\segredo").And.None.Contains("conteudo-lido"));
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed), "An observation never changes the turn outcome.");
        });

        async IAsyncEnumerable<AgentProviderEvent> Script()
        {
            await Task.Yield();
            yield return new(AgentEventKind.ToolStarted, ToolCallId: withArgs, ToolName: "Read", ArgumentsJson: "{\"file_path\":\"C:\\\\segredo\"}");
            yield return new(AgentEventKind.ToolCompleted, ToolCallId: withArgs, ToolName: "Read");
            yield return new(AgentEventKind.ToolStarted, "conteudo-lido", ToolCallId: withText, ToolName: "Read");
            yield return new(AgentEventKind.ToolStarted, ToolCallId: undeclared, ToolName: "Bash");
            yield return new(AgentEventKind.ToolStarted, ToolCallId: undeclared, ToolName: "mcp__slop__find");
            yield return new(AgentEventKind.ToolCompleted, ToolCallId: AgentToolCallId.New(), ToolName: "Read");
            yield return new(AgentEventKind.ToolStarted, ToolCallId: duplicate, ToolName: "Read");
            yield return new(AgentEventKind.ToolStarted, ToolCallId: duplicate, ToolName: "Read");
            yield return new(AgentEventKind.ToolCompleted, ToolCallId: duplicate, ToolName: "Read");
            yield return new(AgentEventKind.ToolCompleted, ToolCallId: duplicate, ToolName: "Read");
            yield return new(AgentEventKind.ToolStarted, ToolCallId: renamed, ToolName: "Read");
            yield return new(AgentEventKind.ToolCompleted, ToolCallId: renamed, ToolName: "Glob");
            yield return new(AgentEventKind.ToolStarted, ToolCallId: badCode, ToolName: "Read");
            yield return new(AgentEventKind.ToolFailed, "conteudo-lido do arquivo", ToolCallId: badCode, ToolName: "Read");
        }
    }

    [Test]
    public async Task ObservationCannotPassForOrTerminateARegistryCall()
    {
        var registry = new FakeRegistry();
        var call = AgentToolCallId.New();
        // The provider declares the registry tool name and "Read"; the registry name is dropped at session start.
        var provider = new ContractProvider(_ => Script(), observable: [ToolName, "Read"]);
        await using var runtime = AgentRuntimeTestFactory.Dispatch([provider], registry, new FakeBindings(), new TestAgentPrincipalAuthority());
        var sessionId = await runtime.StartSessionAsync(new("contract"), CancellationToken.None);
        registry.Release.TrySetResult();
        var events = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(AgentTurnId.New()), CancellationToken.None))
            .WaitAsync(Wait);

        AssertStreamInvariants(events);
        var ofCall = events.Where(item => item.ToolCallId == call).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(ofCall.Select(static item => item.Kind), Is.EqualTo(new[]
                { AgentEventKind.ToolRequested, AgentEventKind.ToolStarted, AgentEventKind.ToolCompleted }));
            Assert.That(ofCall.Select(static item => item.ToolOrigin), Is.All.EqualTo(AgentToolOrigin.Registry));
            Assert.That(registry.Calls, Has.Count.EqualTo(1), "Observations never dispatch nor re-dispatch.");
            Assert.That(events.Where(static item => item.ToolOrigin == AgentToolOrigin.ProviderObserved), Is.Empty,
                "Neither the registry name nor a registry call ID can be observed.");
        });

        async IAsyncEnumerable<AgentProviderEvent> Script()
        {
            await Task.Yield();
            yield return new(AgentEventKind.ToolStarted, ToolCallId: AgentToolCallId.New(), ToolName: ToolName);
            yield return new(AgentEventKind.ToolRequested, ToolCallId: call, ToolName: ToolName, ArgumentsJson: "{}");
            // Forged lifecycle for the registry call through the observation path: discarded.
            yield return new(AgentEventKind.ToolStarted, ToolCallId: call, ToolName: "Read");
            yield return new(AgentEventKind.ToolFailed, ToolCallId: call, ToolName: "Read");
            await Task.Delay(50);
        }
    }

    [TestCase(AgentTurnCancellationReport.MayHaveTakenEffect, AgentTurnOutcome.OutcomeUnknown, "CancelledAfterSend")]
    [TestCase(AgentTurnCancellationReport.NothingSent, AgentTurnOutcome.Cancelled, null)]
    [TestCase(AgentTurnCancellationReport.NotReported, AgentTurnOutcome.Cancelled, null)]
    [TestCase((AgentTurnCancellationReport)99, AgentTurnOutcome.OutcomeUnknown, "CancelledAfterSend")]
    public async Task UserCancellationUsesTheAdapterReportOnlyToBecomeMoreUncertain(
        AgentTurnCancellationReport report, AgentTurnOutcome expected, string? code)
    {
        var provider = new ContractProvider(Hang, report: _ => report);
        await using var runtime = new AgentRuntime([provider]);
        var sessionId = await runtime.StartSessionAsync(new("contract"), CancellationToken.None);
        var turn = AgentTurnId.New();
        var stream = CollectAsync(runtime.RunTurnAsync(sessionId, Request(turn), CancellationToken.None));
        await provider.Session!.Entered.Task.WaitAsync(Wait);

        await runtime.CancelTurnAsync(sessionId, turn, CancellationToken.None);
        var events = await stream.WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(events.Last().Outcome, Is.EqualTo(expected));
        Assert.That(events.SingleOrDefault(static item => item.Kind == AgentEventKind.AgentError)?.ErrorCode, Is.EqualTo(code));
        Assert.That(provider.Session.ReportedTurns, Is.EqualTo(new[] { turn }), "Queried once, after drain, for this turn only.");
    }

    [Test]
    public async Task ThrowingReportIsConservativeAndConsumerCancellationAlsoHonorsTheReport()
    {
        var throwing = new ContractProvider(Hang, report: _ => throw new InvalidOperationException("SECRET"));
        await using var runtime = new AgentRuntime([throwing]);
        var sessionId = await runtime.StartSessionAsync(new("contract"), CancellationToken.None);
        using var consumer = new CancellationTokenSource();
        var stream = CollectAsync(runtime.RunTurnAsync(sessionId, Request(AgentTurnId.New()), consumer.Token));
        await throwing.Session!.Entered.Task.WaitAsync(Wait);
        await consumer.CancelAsync();
        var events = await stream.WaitAsync(Wait);

        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown));
        Assert.That(events.Select(static item => item.ErrorCode ?? ""), Has.None.Contains("SECRET"));
    }

    [Test]
    public async Task NaturalEndFailureAndTimeoutIgnoreTheReport()
    {
        var calls = 0;
        var provider = new ContractProvider(request => calls++ switch
        {
            0 => Finish(),
            1 => Fail(),
            _ => Hang(request),
        }, report: _ => AgentTurnCancellationReport.MayHaveTakenEffect);
        await using var runtime = new AgentRuntime([provider], options: AgentRuntimeOptions.Default with { TurnTimeout = TimeSpan.FromMilliseconds(300) });
        var sessionId = await runtime.StartSessionAsync(new("contract"), CancellationToken.None);

        var completed = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(AgentTurnId.New()), CancellationToken.None));
        var failed = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(AgentTurnId.New()), CancellationToken.None));
        var timedOut = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(AgentTurnId.New()), CancellationToken.None))
            .WaitAsync(Wait);

        Assert.Multiple(() =>
        {
            Assert.That(completed.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed), "A report never overrides a natural end.");
            Assert.That(failed.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
            Assert.That(timedOut.Last().Outcome, Is.EqualTo(AgentTurnOutcome.TimedOut));
        });

        static async IAsyncEnumerable<AgentProviderEvent> Finish()
        {
            await Task.Yield();
            yield break;
        }

        static async IAsyncEnumerable<AgentProviderEvent> Fail()
        {
            await Task.Yield();
            yield return new(AgentEventKind.AgentError, "free text with C:\\path");
        }
    }

    [Test]
    public async Task OnlyDeclaredSafeAdapterErrorCodesReachTheEvent()
    {
        var codes = new Queue<string>(["ClaudeCodeNotLoggedIn", "Undeclared", "bad code!"]);
        var provider = new ContractProvider(_ => Error(codes.Dequeue()), errorCodes: ["ClaudeCodeNotLoggedIn", "bad code!"]);
        await using var runtime = new AgentRuntime([provider]);
        var sessionId = await runtime.StartSessionAsync(new("contract"), CancellationToken.None);
        var published = new List<string?>();
        for (var index = 0; index < 3; index++)
        {
            var events = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(AgentTurnId.New()), CancellationToken.None));
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Failed), "A typed code is presentation only.");
            published.Add(events.Single(static item => item.Kind == AgentEventKind.AgentError).ErrorCode);
        }

        Assert.That(published, Is.EqualTo(ExpectedPublishedCodes));

        static async IAsyncEnumerable<AgentProviderEvent> Error(string code)
        {
            await Task.Yield();
            yield return new(AgentEventKind.AgentError, code);
        }
    }

    [Test]
    public async Task SessionsWithoutDeclarationsKeepDiscardingAdapterToolEventsAndGenericErrors()
    {
        var provider = new ScriptedProvider((_, _, _) => Script());
        await using var runtime = new AgentRuntime([provider]);
        var sessionId = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);

        var events = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(AgentTurnId.New()), CancellationToken.None));

        Assert.That(events.Where(static item => item.ToolCallId is not null), Is.Empty);
        Assert.That(events.Single(static item => item.Kind == AgentEventKind.AgentError).ErrorCode, Is.EqualTo("ProviderError"));

        static async IAsyncEnumerable<AgentProviderEvent> Script()
        {
            await Task.Yield();
            var id = AgentToolCallId.New();
            yield return new(AgentEventKind.ToolStarted, ToolCallId: id, ToolName: "Read");
            yield return new(AgentEventKind.ToolCompleted, ToolCallId: id, ToolName: "Read");
            yield return new(AgentEventKind.AgentError, "ClaudeCodeNotLoggedIn");
        }
    }

    private static IAsyncEnumerable<AgentProviderEvent> Hang(AgentTurnRequest request) => HangCore();

    private static async IAsyncEnumerable<AgentProviderEvent> HangCore([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        yield break;
    }

    /// <summary>Adapter double declaring the additive members of <see cref="IAgentSession"/>.</summary>
    private sealed class ContractProvider(
        Func<AgentTurnRequest, IAsyncEnumerable<AgentProviderEvent>> script,
        IReadOnlyCollection<string>? observable = null,
        IReadOnlyCollection<string>? errorCodes = null,
        Func<AgentTurnId, AgentTurnCancellationReport>? report = null) : IAgentProvider
    {
        public string ProviderId => "contract";

        public ContractSession? Session { get; private set; }

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
        {
            Session = new ContractSession(script, observable ?? [], errorCodes ?? [], report);
            return Task.FromResult<IAgentSession>(Session);
        }
    }

    private sealed class ContractSession(
        Func<AgentTurnRequest, IAsyncEnumerable<AgentProviderEvent>> script,
        IReadOnlyCollection<string> observable,
        IReadOnlyCollection<string> errorCodes,
        Func<AgentTurnId, AgentTurnCancellationReport>? report) : IAgentSession
    {
        private readonly List<AgentTurnId> _reported = [];

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<AgentTurnId> ReportedTurns
        {
            get
            {
                lock (_reported)
                {
                    return [.. _reported];
                }
            }
        }

        public IReadOnlyCollection<string> ObservableNativeTools => observable;

        public IReadOnlyCollection<string> ProviderErrorCodes => errorCodes;

        public AgentTurnCancellationReport GetCancellationReport(AgentTurnId turnId)
        {
            lock (_reported)
            {
                _reported.Add(turnId);
            }

            return report?.Invoke(turnId) ?? AgentTurnCancellationReport.NotReported;
        }

        public IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            return WithToken(script(request), cancellationToken);
        }

        private static async IAsyncEnumerable<AgentProviderEvent> WithToken(
            IAsyncEnumerable<AgentProviderEvent> source, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var item in source.WithCancellation(cancellationToken))
            {
                yield return item;
            }
        }

        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
