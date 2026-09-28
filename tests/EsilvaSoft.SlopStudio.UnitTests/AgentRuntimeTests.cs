using System.Runtime.CompilerServices;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class AgentRuntimeTests
{
    [Test]
    public async Task SessionsHaveOpaqueIdsAndIndependentTurns()
    {
        var provider = new FakeProvider();
        await using var runtime = new AgentRuntime([provider]);
        var sessionA = await runtime.StartSessionAsync(new("fake"), CancellationToken.None);
        var sessionB = await runtime.StartSessionAsync(new("fake"), CancellationToken.None);
        Assert.That(sessionA, Is.Not.EqualTo(sessionB));
        Assert.That(sessionA.IsValid, Is.True);

        var turnA = AgentTurnId.New();
        var turnB = AgentTurnId.New();
        var eventsA = ConsumeAsync(runtime.RunTurnAsync(sessionA, Request(turnA), CancellationToken.None));
        var eventsB = ConsumeAsync(runtime.RunTurnAsync(sessionB, Request(turnB), CancellationToken.None));
        await Task.WhenAll(provider.Sessions.Select(session => session.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3))));

        var busy = Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await ConsumeAsync(runtime.RunTurnAsync(sessionA, Request(AgentTurnId.New()), CancellationToken.None)));
        Assert.That(busy!.Code, Is.EqualTo("SessionBusy"));

        await runtime.CancelTurnAsync(sessionA, turnA, CancellationToken.None);
        var completedA = await eventsA.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(completedA.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Cancelled));
        Assert.That(eventsB.IsCompleted, Is.False);

        provider.Sessions[1].Release.TrySetResult();
        var completedB = await eventsB.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(completedB.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        Assert.That(completedB.Select(item => item.Sequence), Is.Ordered.Ascending);
        Assert.That(completedB.Select(item => item.Sequence).Distinct().Count(), Is.EqualTo(completedB.Count));
        Assert.That(completedB.Count(item => item.Kind == AgentEventKind.TaskCompleted), Is.EqualTo(1));
    }

    [Test]
    public async Task DuplicateTurnAndInvalidIdsFailExplicitly()
    {
        var provider = new FakeProvider();
        await using var runtime = new AgentRuntime([provider]);
        var session = await runtime.StartSessionAsync(new("fake"), CancellationToken.None);
        var turn = AgentTurnId.New();
        var first = ConsumeAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None));
        await provider.Sessions[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        provider.Sessions[0].Release.TrySetResult();
        await first.WaitAsync(TimeSpan.FromSeconds(3));

        var duplicate = Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await ConsumeAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None)));
        Assert.That(duplicate!.Code, Is.EqualTo("DuplicateTurnId"));

        var invalid = Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await ConsumeAsync(runtime.RunTurnAsync(new AgentSessionId("bad"), Request(AgentTurnId.New()), CancellationToken.None)));
        Assert.That(invalid!.Code, Is.EqualTo("InvalidSessionId"));
        Assert.That(Assert.Throws<AgentRuntimeException>(() => new AgentRuntime([provider, provider]))!.Code,
            Is.EqualTo("DuplicateProviderId"));
    }

    [Test]
    public async Task ProviderFailureProducesOnlySanitizedError()
    {
        var provider = new FakeProvider { Fail = true };
        await using var runtime = new AgentRuntime([provider]);
        var session = await runtime.StartSessionAsync(new("fake"), CancellationToken.None);
        var events = await ConsumeAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None));

        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
        var error = events.Single(item => item.Kind == AgentEventKind.AgentError);
        Assert.That(error.ErrorCode, Is.EqualTo("ProviderFailure"));
        Assert.That(string.Join('|', events.Select(item => item.Text)), Does.Not.Contain("SECRET_CONNECTION_URI"));

        provider.Sessions[0].FailNext = false;
        provider.Sessions[0].Release.TrySetResult();
        var recovery = await ConsumeAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None));
        Assert.That(recovery.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
    }

    [Test]
    public async Task ToolRequestWithoutTrustedPrincipalFailsClosed()
    {
        var provider = new FakeProvider { EmitToolRequest = true };
        await using var runtime = new AgentRuntime([provider]);
        var session = await runtime.StartSessionAsync(new("fake"), CancellationToken.None);
        var events = await ConsumeAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None));

        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
        Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).ErrorCode,
            Is.EqualTo("ProviderProtocolViolation"));
        Assert.That(events, Has.None.Matches<AgentEvent>(item => item.Kind == AgentEventKind.ToolRequested));
    }

    [Test]
    public async Task CloseSessionCancelsActiveTurnAndRemovesSession()
    {
        var provider = new FakeProvider();
        await using var runtime = new AgentRuntime([provider]);
        var session = await runtime.StartSessionAsync(new("fake"), CancellationToken.None);
        var eventsTask = ConsumeAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None));
        await provider.Sessions[0].Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await runtime.CloseSessionAsync(session, CancellationToken.None);
        var events = await eventsTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Cancelled));
        Assert.That(provider.Sessions[0].Disposed, Is.True);
        var missing = Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await ConsumeAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None)));
        Assert.That(missing!.Code, Is.EqualTo("UnknownSession"));
    }

    [Test]
    public async Task StuckProviderCancellationReturnsUnknownAndDefersDisposalUntilStreamStops()
    {
        var provider = new NonCooperativeProvider();
        await using var runtime = new AgentRuntime([provider]);
        var session = await runtime.StartSessionAsync(new("stuck"), CancellationToken.None);
        var turn = AgentTurnId.New();
        var eventsTask = ConsumeAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None));
        await provider.Session.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));

        var cancellation = Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.CancelTurnAsync(session, turn, CancellationToken.None));
        Assert.That(cancellation!.Code, Is.EqualTo("CancellationUnconfirmed"));
        var events = await eventsTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown));
        Assert.That(provider.Session.DisposeCount, Is.Zero);

        provider.Session.ReleaseMove.TrySetResult();
        provider.Session.ReleaseCancel.TrySetResult();
        await provider.Session.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(provider.Session.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task StuckEnumeratorDisposalCannotRaceProviderDisposal()
    {
        var provider = new StuckDisposalProvider();
        await using var runtime = new AgentRuntime([provider]);
        var session = await runtime.StartSessionAsync(new("stuck-dispose"), CancellationToken.None);
        var events = await ConsumeAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None))
            .WaitAsync(TimeSpan.FromSeconds(7));

        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown));
        Assert.That(provider.Session.DisposeCount, Is.Zero);
        provider.Session.ReleaseEnumerator.TrySetResult();
        await provider.Session.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(provider.Session.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task StuckProviderDisposalReturnsWithoutRepeatingDisposal()
    {
        var provider = new StuckSessionDisposalProvider();
        await using var runtime = new AgentRuntime([provider]);
        var session = await runtime.StartSessionAsync(new("stuck-session-dispose"), CancellationToken.None);

        var closing = Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.CloseSessionAsync(session, CancellationToken.None));
        Assert.That(closing!.Code, Is.EqualTo("SessionShutdownUnconfirmed"));
        Assert.That(provider.Session.DisposeCount, Is.EqualTo(1));
        provider.Session.ReleaseDisposal.TrySetResult();
        await provider.Session.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(provider.Session.DisposeCount, Is.EqualTo(1));
    }

    private static AgentTurnRequest Request(AgentTurnId id) => new(id, "hello", "tab-1", 1);

    private static async Task<List<AgentEvent>> ConsumeAsync(IAsyncEnumerable<AgentEvent> stream)
    {
        var events = new List<AgentEvent>();
        await foreach (var item in stream)
        {
            events.Add(item);
        }

        return events;
    }

    private sealed class FakeProvider : IAgentProvider
    {
        public string ProviderId => "fake";

        public bool Fail { get; init; }

        public bool EmitToolRequest { get; init; }

        public List<FakeSession> Sessions { get; } = [];

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
        {
            var session = new FakeSession(Fail, EmitToolRequest);
            Sessions.Add(session);
            return Task.FromResult<IAgentSession>(session);
        }
    }

    private sealed class FakeSession(bool fail, bool emitToolRequest) : IAgentSession
    {
        public bool FailNext { get; set; } = fail;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Disposed { get; private set; }

        public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(
            AgentTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            yield return new(AgentEventKind.MessageStarted);
            if (FailNext)
            {
                throw new InvalidOperationException("SECRET_CONNECTION_URI");
            }

            if (emitToolRequest)
            {
                yield return new(AgentEventKind.ToolRequested, "untrusted tool call");
                yield break;
            }

            await Release.Task.WaitAsync(cancellationToken);
            yield return new(AgentEventKind.MessageDelta, "answer");
            yield return new(AgentEventKind.MessageCompleted);
            yield return new(AgentEventKind.MessageCompleted);
        }

        public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NonCooperativeProvider : IAgentProvider
    {
        public string ProviderId => "stuck";

        public NonCooperativeSession Session { get; } = new();

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IAgentSession>(Session);
    }

    private sealed class NonCooperativeSession : IAgentSession
    {
        private int _disposeCount;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseMove { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseCancel { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(
            AgentTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return new(AgentEventKind.MessageStarted);
            Entered.TrySetResult();
            await ReleaseMove.Task;
            yield return new(AgentEventKind.MessageCompleted);
        }

        public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => ReleaseCancel.Task;

        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StuckDisposalProvider : IAgentProvider
    {
        public string ProviderId => "stuck-dispose";

        public StuckDisposalSession Session { get; } = new();

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IAgentSession>(Session);
    }

    private sealed class StuckSessionDisposalProvider : IAgentProvider
    {
        public string ProviderId => "stuck-session-dispose";

        public StuckSessionDisposalSession Session { get; } = new();

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IAgentSession>(Session);
    }

    private sealed class StuckSessionDisposalSession : IAgentSession
    {
        private int _disposeCount;

        public TaskCompletionSource ReleaseDisposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(
            AgentTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }

        public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            await ReleaseDisposal.Task;
            Disposed.TrySetResult();
        }
    }

    private sealed class StuckDisposalSession : IAgentSession
    {
        private int _disposeCount;

        public TaskCompletionSource ReleaseEnumerator { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken) =>
            new StuckEnumerable(ReleaseEnumerator.Task);

        public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }

        private sealed class StuckEnumerable(Task release) : IAsyncEnumerable<AgentProviderEvent>, IAsyncEnumerator<AgentProviderEvent>
        {
            private bool _read;

            public AgentProviderEvent Current => new(AgentEventKind.MessageStarted);

            public IAsyncEnumerator<AgentProviderEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;

            public ValueTask<bool> MoveNextAsync()
            {
                if (_read)
                {
                    return ValueTask.FromResult(false);
                }

                _read = true;
                return ValueTask.FromResult(true);
            }

            public async ValueTask DisposeAsync() => await release;
        }
    }
}
