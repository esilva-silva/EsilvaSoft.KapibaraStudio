using System.Runtime.CompilerServices;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class AgentRuntimeInteractionTests
{
    [Test]
    public async Task ResultAndHumanDecisionUnblockStreamWithoutHoldingSessionLock()
    {
        var provider = new InteractiveProvider();
        var authority = new FakeAuthority();
        await using var runtime = new AgentRuntime([provider], authority);
        var session = await runtime.StartSessionAsync(new("interactive"), CancellationToken.None);
        var turn = AgentTurnId.New();
        authority.Bind(session, turn, provider.Session.CallId, provider.Session.ApprovalId);
        var toolSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var approvalSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = ConsumeAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None), item =>
        {
            if (item.Kind == AgentEventKind.ToolRequested) toolSeen.TrySetResult();
            if (item.Kind == AgentEventKind.ApprovalRequested) approvalSeen.TrySetResult();
        });
        await toolSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(stream.IsCompleted, Is.False);

        var otherSession = await runtime.StartSessionAsync(new("interactive"), CancellationToken.None);
        var crossSession = new AgentToolResult(otherSession, turn, provider.Session.CallId, AgentToolResultStatus.Succeeded);
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.SubmitToolResultAsync(crossSession, CancellationToken.None))!.Code, Is.EqualTo("UnknownToolCall"));
        var wrongCall = new AgentToolResult(session, turn, AgentToolCallId.New(), AgentToolResultStatus.Succeeded);
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.SubmitToolResultAsync(wrongCall, CancellationToken.None))!.Code, Is.EqualTo("UnknownToolCall"));
        var result = new AgentToolResult(session, turn, provider.Session.CallId, AgentToolResultStatus.Succeeded, "safe");
        await runtime.SubmitToolResultAsync(result, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        await approvalSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.That(provider.Session.ReceivedResult, Is.EqualTo(result));

        var wrongTurn = new AgentApprovalDecision(session, AgentTurnId.New(), provider.Session.ApprovalId,
            AgentApprovalOutcome.Granted);
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.DecideApprovalAsync(wrongTurn, CancellationToken.None))!.Code, Is.EqualTo("UnknownApproval"));
        var decision = new AgentApprovalDecision(session, turn, provider.Session.ApprovalId, AgentApprovalOutcome.Granted);
        await runtime.DecideApprovalAsync(decision, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(3));
        var events = await stream.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.That(provider.Session.ReceivedDecision, Is.EqualTo(decision));
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        Assert.That(events.Count(item => item.Kind == AgentEventKind.ToolRequested), Is.EqualTo(1));
        Assert.That(events.Count(item => item.Kind == AgentEventKind.ApprovalRequested), Is.EqualTo(1));
        Assert.That(events.Select(item => item.Sequence), Is.Ordered.Ascending);
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.SubmitToolResultAsync(result, CancellationToken.None))!.Code, Is.EqualTo("UnknownToolCall"));
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.DecideApprovalAsync(decision, CancellationToken.None))!.Code, Is.EqualTo("UnknownApproval"));
    }

    [Test]
    public async Task UntrustedResultAndModelApprovalCannotReachProvider()
    {
        var provider = new InteractiveProvider();
        var authority = new FakeAuthority { AcceptToolResult = false };
        await using var runtime = new AgentRuntime([provider], authority);
        var session = await runtime.StartSessionAsync(new("interactive"), CancellationToken.None);
        var turn = AgentTurnId.New();
        authority.Bind(session, turn, provider.Session.CallId, provider.Session.ApprovalId);
        var toolSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = ConsumeAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None), item =>
        {
            if (item.Kind == AgentEventKind.ToolRequested) toolSeen.TrySetResult();
        });
        await toolSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var result = new AgentToolResult(session, turn, provider.Session.CallId, AgentToolResultStatus.Succeeded);
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.SubmitToolResultAsync(result, CancellationToken.None))!.Code, Is.EqualTo("UntrustedToolResult"));
        Assert.That(provider.Session.ReceivedResult, Is.Null);
        // A rejected result reached nobody, so the call stays answerable instead of being consumed by a forgery.
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.SubmitToolResultAsync(result, CancellationToken.None))!.Code, Is.EqualTo("UntrustedToolResult"));
        Assert.That(provider.Session.ReceivedResult, Is.Null);
        await runtime.CancelTurnAsync(session, turn, CancellationToken.None);
        await stream.WaitAsync(TimeSpan.FromSeconds(3));

        var spoofProvider = new InteractiveProvider { SpoofApproval = true };
        await using var spoofRuntime = new AgentRuntime([spoofProvider], new FakeAuthority());
        var spoofSession = await spoofRuntime.StartSessionAsync(new("interactive"), CancellationToken.None);
        var spoofEvents = await ConsumeAsync(spoofRuntime.RunTurnAsync(spoofSession, Request(AgentTurnId.New()),
            CancellationToken.None));
        Assert.That(spoofEvents.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
        Assert.That(spoofEvents.Single(item => item.Kind == AgentEventKind.AgentError).ErrorCode,
            Is.EqualTo("ProviderProtocolViolation"));
        Assert.That(spoofEvents, Has.None.Matches<AgentEvent>(item => item.Kind == AgentEventKind.ApprovalGranted));
    }

    [Test]
    public async Task SpoofedHumanDecisionIsRejectedWithoutConsumingTheApproval()
    {
        var provider = new InteractiveProvider();
        var authority = new FakeAuthority { AcceptApprovalDecision = false };
        await using var runtime = new AgentRuntime([provider], authority);
        var session = await runtime.StartSessionAsync(new("interactive"), CancellationToken.None);
        var turn = AgentTurnId.New();
        authority.Bind(session, turn, provider.Session.CallId, provider.Session.ApprovalId);
        var approvalSeen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = ConsumeAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None), item =>
        {
            if (item.Kind == AgentEventKind.ApprovalRequested) approvalSeen.TrySetResult();
        });
        await provider.Session.ToolRequestEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await runtime.SubmitToolResultAsync(
            new AgentToolResult(session, turn, provider.Session.CallId, AgentToolResultStatus.Succeeded),
            CancellationToken.None);
        await approvalSeen.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var decision = new AgentApprovalDecision(session, turn, provider.Session.ApprovalId, AgentApprovalOutcome.Granted);
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.DecideApprovalAsync(decision, CancellationToken.None))!.Code,
            Is.EqualTo("UntrustedApprovalDecision"));
        Assert.That(provider.Session.ReceivedDecision, Is.Null);
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.DecideApprovalAsync(decision, CancellationToken.None))!.Code,
            Is.EqualTo("UntrustedApprovalDecision"));
        Assert.That(provider.Session.ReceivedDecision, Is.Null);
        await runtime.CancelTurnAsync(session, turn, CancellationToken.None);
        await stream.WaitAsync(TimeSpan.FromSeconds(3));
    }

    private static AgentTurnRequest Request(AgentTurnId turn) => new(turn, "hello", "tab", 1);

    private static async Task<List<AgentEvent>> ConsumeAsync(IAsyncEnumerable<AgentEvent> stream, Action<AgentEvent>? onEvent = null)
    {
        var events = new List<AgentEvent>();
        await foreach (var item in stream)
        {
            events.Add(item);
            onEvent?.Invoke(item);
        }

        return events;
    }

    private sealed class FakeAuthority : IAgentInteractionAuthority
    {
        private AgentSessionId _session;
        private AgentTurnId _turn;
        private AgentToolCallId _call;
        private AgentApprovalId _approval;

        public bool AcceptToolResult { get; init; } = true;

        public bool AcceptApprovalDecision { get; init; } = true;

        public void Bind(AgentSessionId session, AgentTurnId turn, AgentToolCallId call, AgentApprovalId approval)
        {
            _session = session;
            _turn = turn;
            _call = call;
            _approval = approval;
        }

        public Task<bool> ValidateToolRequestAsync(AgentSessionId sessionId, AgentTurnId turnId,
            AgentToolCallId toolCallId, CancellationToken cancellationToken) =>
            Task.FromResult(sessionId == _session && turnId == _turn && toolCallId == _call);

        public Task<bool> ValidateToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) =>
            Task.FromResult(AcceptToolResult && result.SessionId == _session && result.TurnId == _turn &&
                result.ToolCallId == _call);

        public Task<bool> ValidateApprovalRequestAsync(AgentSessionId sessionId, AgentTurnId turnId,
            AgentApprovalId approvalId, CancellationToken cancellationToken) =>
            Task.FromResult(sessionId == _session && turnId == _turn && approvalId == _approval);

        public Task<bool> ValidateApprovalDecisionAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
            Task.FromResult(AcceptApprovalDecision && decision.SessionId == _session && decision.TurnId == _turn &&
                decision.ApprovalId == _approval);
    }

    private sealed class InteractiveProvider : IAgentProvider
    {
        public string ProviderId => "interactive";

        public bool SpoofApproval { get; init; }

        public InteractiveSession Session { get; } = new();

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
        {
            Session.SpoofApproval = SpoofApproval;
            return Task.FromResult<IAgentSession>(Session);
        }
    }

    private sealed class InteractiveSession : IAgentSession
    {
        private readonly TaskCompletionSource _toolReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _approvalReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AgentToolCallId CallId { get; } = AgentToolCallId.New();

        public AgentApprovalId ApprovalId { get; } = AgentApprovalId.New();

        public bool SpoofApproval { get; set; }

        public AgentToolResult? ReceivedResult { get; private set; }

        public TaskCompletionSource ToolRequestEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AgentApprovalDecision? ReceivedDecision { get; private set; }

        public async IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (SpoofApproval)
            {
                yield return new(AgentEventKind.ApprovalGranted, ApprovalId: ApprovalId);
                yield break;
            }

            yield return new(AgentEventKind.ToolRequested, ToolCallId: CallId);
            ToolRequestEntered.TrySetResult();
            await _toolReceived.Task.WaitAsync(cancellationToken);
            yield return new(AgentEventKind.ApprovalRequested, ApprovalId: ApprovalId);
            await _approvalReceived.Task.WaitAsync(cancellationToken);
            yield return new(AgentEventKind.MessageStarted);
            yield return new(AgentEventKind.MessageDelta, "done");
            yield return new(AgentEventKind.MessageCompleted);
        }

        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken)
        {
            ReceivedResult = result;
            _toolReceived.TrySetResult();
            return Task.CompletedTask;
        }

        public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken)
        {
            ReceivedDecision = decision;
            _approvalReceived.TrySetResult();
            return Task.CompletedTask;
        }

        public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
