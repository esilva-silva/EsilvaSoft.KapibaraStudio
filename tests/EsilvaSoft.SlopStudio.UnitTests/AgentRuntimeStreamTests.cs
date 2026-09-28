using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Deterministic provider scripts for the lote 5 runtime: ordering, deduplication, bounded queues, tool dispatch
/// through the registry port, cancellation isolation and failing adapters. No real provider, network or MongoDB.
/// </summary>
[TestFixture]
public sealed partial class AgentRuntimeStreamTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private const string ToolName = "list_connections";

    [Test]
    public async Task DuplicateLateAndOrphanProviderEventsYieldOneTerminalPerItem()
    {
        var m1 = AgentMessageId.New();
        var m2 = AgentMessageId.New();
        var provider = new ScriptedProvider((_, _, _) => Script());
        await using var runtime = new AgentRuntime([provider]);
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var request = new AgentContextSnapshot("tab-7", 42, DateTimeOffset.UtcNow, AuthorizedContext: "ctx")
            .ToTurnRequest(AgentTurnId.New(), "hello");

        var events = await CollectAsync(runtime.RunTurnAsync(session, request, CancellationToken.None));

        AssertStreamInvariants(events);
        Assert.That(provider.Sessions[0].LastRequest, Is.EqualTo(request));
        Assert.That(events.Count(item => item.Kind == AgentEventKind.MessageStarted), Is.EqualTo(2));
        Assert.That(string.Concat(events.Where(item => item.Kind == AgentEventKind.MessageDelta).Select(item => item.Text)),
            Is.EqualTo("ab"));
        Assert.That(events.Select(item => item.MessageId), Has.None.EqualTo(m1).And.None.EqualTo(m2),
            "Adapter message keys never become protocol identifiers.");
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        Assert.That(events, Has.None.Matches<AgentEvent>(item => item.Kind is AgentEventKind.ToolCompleted));

        async IAsyncEnumerable<AgentProviderEvent> Script()
        {
            await Task.Yield();
            yield return new(AgentEventKind.MessageDelta, "orphan", MessageId: m1);
            yield return new(AgentEventKind.MessageStarted, MessageId: m1);
            yield return new(AgentEventKind.MessageStarted, MessageId: m1);
            yield return new(AgentEventKind.MessageDelta, "a", MessageId: m1);
            yield return new(AgentEventKind.MessageStarted, MessageId: m2);
            yield return new(AgentEventKind.MessageDelta, "b", MessageId: m2);
            yield return new(AgentEventKind.MessageCompleted, MessageId: m1);
            yield return new(AgentEventKind.MessageCompleted, MessageId: m1);
            yield return new(AgentEventKind.MessageDelta, "late", MessageId: m1);
            yield return new(AgentEventKind.TaskCompleted);
            yield return new(AgentEventKind.ToolCompleted, ToolCallId: AgentToolCallId.New());
        }
    }

    [Test]
    public async Task RuntimeDispatchesThroughRegistryWhileStreamKeepsFlowing()
    {
        var registry = new FakeRegistry();
        var bindings = new FakeBindings();
        var call = AgentToolCallId.New();
        var message = AgentMessageId.New();
        var provider = new ScriptedProvider((session, _, token) => Script(session, token));
        await using var runtime = AgentRuntimeTestFactory.Dispatch([provider], registry, bindings, new TestAgentPrincipalAuthority());
        var sessionId = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turn = AgentTurnId.New();
        var deltaWhileToolRuns = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        var stream = CollectAsync(runtime.RunTurnAsync(sessionId, Request(turn), CancellationToken.None), item =>
        {
            if (item is { Kind: AgentEventKind.MessageDelta, Text: "while tool runs" })
            {
                deltaWhileToolRuns.TrySetResult(registry.CompletedCount);
            }

            return Task.CompletedTask;
        });

        Assert.That(await deltaWhileToolRuns.Task.WaitAsync(Wait), Is.Zero, "The stream is not blocked by the tool.");
        var external = new AgentToolResult(sessionId, turn, call, AgentToolResultStatus.Succeeded, "{\"forged\":true}");
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.SubmitToolResultAsync(external, CancellationToken.None))!.Code, Is.EqualTo("UnknownToolCall"));
        registry.Release.TrySetResult();
        var events = await stream.WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        Assert.That(registry.Calls, Has.Count.EqualTo(1), "A duplicated request is dispatched once.");
        var invocation = registry.Calls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(invocation.Principal, Is.SameAs(bindings.Principal));
            Assert.That(invocation.Context.SessionId, Is.EqualTo(Guid.ParseExact(sessionId.Value, "N")));
            Assert.That(invocation.Context.TurnId, Is.EqualTo(Guid.ParseExact(turn.Value, "N")));
            Assert.That(invocation.Context.ProviderId, Is.EqualTo("scripted"));
            Assert.That(invocation.Destination, Is.EqualTo(AgentOutputDestination.ProviderExternal("scripted")));
            Assert.That(invocation.Arguments, Is.EqualTo("{}"));
        });
        var kinds = events.Where(item => item.ToolCallId == call).Select(item => item.Kind).ToArray();
        Assert.That(kinds, Is.EqualTo(new[] { AgentEventKind.ToolRequested, AgentEventKind.ToolStarted, AgentEventKind.ToolCompleted }));
        Assert.That(events.Where(item => item.ToolCallId == call).Select(item => item.ToolName), Is.All.EqualTo(ToolName));
        Assert.That(events.Where(item => item.ToolCallId == call).Select(item => item.Text), Is.All.Null,
            "Tool data is delivered to the provider only, never copied into UI events.");
        Assert.That(provider.Sessions[0].Results.Single().Data, Is.EqualTo(FakeRegistry.Payload));

        async IAsyncEnumerable<AgentProviderEvent> Script(ScriptedSession session, [EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            yield return new(AgentEventKind.MessageStarted, MessageId: message);
            yield return new(AgentEventKind.ToolRequested, ToolCallId: call, ToolName: ToolName, ArgumentsJson: "{}");
            yield return new(AgentEventKind.ToolRequested, ToolCallId: call, ToolName: ToolName, ArgumentsJson: "{}");
            yield return new(AgentEventKind.MessageDelta, "while tool runs", MessageId: message);
            var result = await session.NextResultAsync(token);
            yield return new(AgentEventKind.MessageDelta, result.Status.ToString(), MessageId: message);
            yield return new(AgentEventKind.MessageCompleted, MessageId: message);
        }
    }

    [Test]
    public async Task BrokerResultSubmittedFromInsideConsumerLoopWithFullQueueDoesNotDeadlock()
    {
        var call = AgentToolCallId.New();
        var message = AgentMessageId.New();
        var yielded = 0;
        var options = new AgentRuntimeOptions { MaxQueuedEvents = 4, MaxDeltaChars = 16 };
        var provider = new ScriptedProvider((session, _, token) => Script(session, token));
        await using var runtime = new AgentRuntime([provider], new FakeAuthority(), options);
        var sessionId = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turn = AgentTurnId.New();
        var submittedWhileBlocked = -1;

        var events = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(turn), CancellationToken.None), async item =>
        {
            if (item.Kind != AgentEventKind.ToolRequested)
            {
                return;
            }

            // Give the pump time to fill the queue, then answer while this consumer is still inside its loop body.
            await WaitUntilAsync(() => Volatile.Read(ref yielded) >= 4);
            var result = new AgentToolResult(sessionId, turn, call, AgentToolResultStatus.Succeeded, "{}");
            await runtime.SubmitToolResultAsync(result, CancellationToken.None).WaitAsync(Wait);
            submittedWhileBlocked = Volatile.Read(ref yielded);
        }).WaitAsync(TimeSpan.FromSeconds(10));

        AssertStreamInvariants(events);
        Assert.That(submittedWhileBlocked, Is.InRange(4, 19), "The result was accepted while the provider was back-pressured.");
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        Assert.That(events.Count(item => item.Kind == AgentEventKind.MessageDelta), Is.EqualTo(20));

        async IAsyncEnumerable<AgentProviderEvent> Script(ScriptedSession session, [EnumeratorCancellation] CancellationToken token)
        {
            await Task.Yield();
            yield return new(AgentEventKind.MessageStarted, MessageId: message);
            yield return new(AgentEventKind.ToolRequested, ToolCallId: call);
            for (var index = 0; index < 20; index++)
            {
                Interlocked.Increment(ref yielded);
                yield return new(AgentEventKind.MessageDelta, $"chunk-{index:D2}-padding"[..16], MessageId: message);
            }

            await session.NextResultAsync(token);
            yield return new(AgentEventKind.MessageCompleted, MessageId: message);
        }
    }

    [Test]
    public async Task StalledConsumerInOneSessionDoesNotStarveAnotherAndEndsWithConsumerUnavailable()
    {
        var options = new AgentRuntimeOptions { MaxQueuedEvents = 4, MaxDeltaChars = 16, ConsumerTimeout = TimeSpan.FromMilliseconds(400) };
        var provider = new ScriptedProvider((_, request, token) => request.UserMessage == "flood" ? Flood(token) : Short());
        await using var runtime = new AgentRuntime([provider], null, options);
        var sessionA = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var sessionB = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);

        await using var stalled = runtime.RunTurnAsync(sessionA, Request(AgentTurnId.New(), "flood"), CancellationToken.None)
            .GetAsyncEnumerator();
        Assert.That(await stalled.MoveNextAsync(), Is.True);

        var other = await CollectAsync(runtime.RunTurnAsync(sessionB, Request(AgentTurnId.New()), CancellationToken.None))
            .WaitAsync(Wait);
        Assert.That(other.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));

        await Task.Delay(TimeSpan.FromMilliseconds(900));
        var rest = new List<AgentEvent> { stalled.Current };
        while (await stalled.MoveNextAsync().AsTask().WaitAsync(Wait))
        {
            rest.Add(stalled.Current);
        }

        AssertStreamInvariants(rest);
        Assert.That(rest.Count, Is.LessThan(12), "The queue stayed bounded while nobody read it.");
        Assert.That(rest.Single(item => item.Kind == AgentEventKind.AgentError).ErrorCode, Is.EqualTo("ConsumerUnavailable"));
        Assert.That(rest.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Cancelled));
        var again = await CollectAsync(runtime.RunTurnAsync(sessionA, Request(AgentTurnId.New()), CancellationToken.None))
            .WaitAsync(Wait);
        Assert.That(again.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));

        static async IAsyncEnumerable<AgentProviderEvent> Flood([EnumeratorCancellation] CancellationToken token)
        {
            var message = AgentMessageId.New();
            yield return new(AgentEventKind.MessageStarted, MessageId: message);
            for (var index = 0; index < 1000; index++)
            {
                token.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return new(AgentEventKind.MessageDelta, "0123456789abcdef", MessageId: message);
            }
        }
    }

    [Test]
    public async Task AbandonedConsumerCancelsOnlyItsTurnAndFreesTheSession()
    {
        var provider = new ScriptedProvider((_, request, token) => request.UserMessage == "wait" ? Waiting(token) : Short());
        await using var runtime = new AgentRuntime([provider]);
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);

        await foreach (var item in runtime.RunTurnAsync(session, Request(AgentTurnId.New(), "wait"), CancellationToken.None))
        {
            if (item.Kind == AgentEventKind.MessageStarted)
            {
                break;
            }
        }

        await provider.Sessions[0].TokenCancelled.Task.WaitAsync(Wait);
        var next = await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None))
            .WaitAsync(Wait);
        Assert.That(next.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
    }

    [Test]
    public async Task CancelDuringDispatchedToolReportsOutcomeUnknownAndLeavesOtherSessionRunning()
    {
        var registry = new FakeRegistry { HonorCancellation = true };
        var provider = new ScriptedProvider((session, request, token) =>
            request.UserMessage == "tool" ? RequestTool(session, token) : Gated(session, token));
        await using var runtime = AgentRuntimeTestFactory.Dispatch([provider], registry, new FakeBindings(), new TestAgentPrincipalAuthority());
        var sessionA = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var sessionB = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turnA = AgentTurnId.New();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var streamA = CollectAsync(runtime.RunTurnAsync(sessionA, Request(turnA, "tool"), CancellationToken.None), item =>
        {
            if (item.Kind == AgentEventKind.ToolStarted) started.TrySetResult();
            return Task.CompletedTask;
        });
        var streamB = CollectAsync(runtime.RunTurnAsync(sessionB, Request(AgentTurnId.New(), "gated"), CancellationToken.None));
        await started.Task.WaitAsync(Wait);
        await provider.Sessions[1].Entered.Task.WaitAsync(Wait);

        await runtime.CancelTurnAsync(sessionA, turnA, CancellationToken.None).WaitAsync(Wait);
        var eventsA = await streamA.WaitAsync(Wait);

        AssertStreamInvariants(eventsA);
        var failed = eventsA.Single(item => item.Kind == AgentEventKind.ToolFailed);
        Assert.That(failed.ToolStatus, Is.EqualTo(AgentToolResultStatus.OutcomeUnknown), "No rollback is claimed.");
        Assert.That(eventsA.Last().Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown));
        Assert.That(eventsA, Has.None.Matches<AgentEvent>(item => item.Kind == AgentEventKind.ToolCompleted));
        Assert.That(provider.Sessions[0].Results, Is.Empty);
        Assert.That(streamB.IsCompleted, Is.False);
        Assert.That(provider.Sessions[1].TokenCancelled.Task.IsCompleted, Is.False, "B has its own CTS.");

        provider.Sessions[1].Release.TrySetResult();
        var eventsB = await streamB.WaitAsync(Wait);
        Assert.That(eventsB.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        Assert.That(eventsB.Select(item => item.SessionId), Is.All.EqualTo(sessionB));
    }

    [Test]
    public async Task LateRegistryResultAfterCancellationIsDiscardedAndNeverDelivered()
    {
        var registry = new FakeRegistry();
        var options = new AgentRuntimeOptions { StopTimeout = TimeSpan.FromMilliseconds(300) };
        var provider = new ScriptedProvider((session, _, token) => RequestTool(session, token));
        await using var runtime = AgentRuntimeTestFactory.Dispatch([provider], registry, new FakeBindings(), new TestAgentPrincipalAuthority(), options);
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turn = AgentTurnId.New();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = CollectAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None), item =>
        {
            if (item.Kind == AgentEventKind.ToolStarted) started.TrySetResult();
            return Task.CompletedTask;
        });
        await started.Task.WaitAsync(Wait);

        await runtime.CancelTurnAsync(session, turn, CancellationToken.None).WaitAsync(Wait);
        var events = await stream.WaitAsync(Wait);
        registry.Release.TrySetResult();
        await WaitUntilAsync(() => registry.CompletedCount == 1);
        await Task.Delay(100);

        AssertStreamInvariants(events);
        Assert.That(events.Single(item => item.Kind == AgentEventKind.ToolFailed).ToolStatus,
            Is.EqualTo(AgentToolResultStatus.OutcomeUnknown));
        Assert.That(provider.Sessions[0].Results, Is.Empty, "A late result of a cancelled turn is not delivered.");
    }

    [Test]
    public async Task CancelDuringApprovalDeniesItAndLateDecisionIsRejected()
    {
        var approval = AgentApprovalId.New();
        var provider = new ScriptedProvider((session, _, token) => AskApproval(session, approval, token));
        await using var runtime = new AgentRuntime([provider], new FakeAuthority());
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turn = AgentTurnId.New();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stream = CollectAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None), item =>
        {
            if (item.Kind == AgentEventKind.ApprovalRequested) requested.TrySetResult();
            return Task.CompletedTask;
        });
        await requested.Task.WaitAsync(Wait);

        await runtime.CancelTurnAsync(session, turn, CancellationToken.None).WaitAsync(Wait);
        var events = await stream.WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(events.Single(item => item.Kind == AgentEventKind.ApprovalDenied).ErrorCode, Is.EqualTo("ApprovalCancelled"));
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Cancelled));
        var late = new AgentApprovalDecision(session, turn, approval, AgentApprovalOutcome.Granted);
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.DecideApprovalAsync(late, CancellationToken.None))!.Code, Is.EqualTo("UnknownApproval"));
        Assert.That(provider.Sessions[0].Decisions, Is.Empty);
    }

    [Test]
    public async Task ExpiredApprovalFailsClosedAndIsDeliveredAsDenial()
    {
        var approval = AgentApprovalId.New();
        var options = new AgentRuntimeOptions { ApprovalTimeout = TimeSpan.FromMilliseconds(200) };
        var provider = new ScriptedProvider((session, _, token) => AskApproval(session, approval, token));
        await using var runtime = new AgentRuntime([provider], new FakeAuthority(), options);
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turn = AgentTurnId.New();

        var events = await CollectAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None)).WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(events.Single(item => item.Kind == AgentEventKind.ApprovalDenied).ErrorCode, Is.EqualTo("ApprovalExpired"));
        Assert.That(events, Has.None.Matches<AgentEvent>(item => item.Kind == AgentEventKind.ApprovalGranted));
        Assert.That(provider.Sessions[0].Decisions.Single().Outcome, Is.EqualTo(AgentApprovalOutcome.Denied));
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
    }

    [Test]
    public async Task ProviderThatThrowsDuringPendingToolEndsEveryItemOnceWithoutLeakingDetails()
    {
        var call = AgentToolCallId.New();
        var provider = new ScriptedProvider((_, _, _) => Throwing());
        await using var runtime = new AgentRuntime([provider], new FakeAuthority());
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);
        var turn = AgentTurnId.New();

        var events = await CollectAsync(runtime.RunTurnAsync(session, Request(turn), CancellationToken.None)).WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(events.Single(item => item.Kind == AgentEventKind.ToolFailed).ToolStatus, Is.EqualTo(AgentToolResultStatus.Cancelled));
        Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).ErrorCode, Is.EqualTo("ProviderFailure"));
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
        Assert.That(string.Join('|', events.Select(item => $"{item.Text}{item.ErrorCode}")), Does.Not.Contain("SECRET"));
        var late = new AgentToolResult(session, turn, call, AgentToolResultStatus.Succeeded);
        Assert.That(Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.SubmitToolResultAsync(late, CancellationToken.None))!.Code, Is.EqualTo("UnknownToolCall"));

        async IAsyncEnumerable<AgentProviderEvent> Throwing()
        {
            await Task.Yield();
            yield return new(AgentEventKind.ToolRequested, ToolCallId: call);
            throw new InvalidOperationException("SECRET_API_KEY");
        }
    }

    [Test]
    public async Task HangingOrThrowingSessionStartIsBoundedAndLateSessionIsDisposed()
    {
        var hanging = new HangingProvider();
        var options = new AgentRuntimeOptions { SessionStartTimeout = TimeSpan.FromMilliseconds(200) };
        await using var runtime = new AgentRuntime([hanging, new ThrowingProvider()], null, options);

        var timeout = Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.StartSessionAsync(new("hanging"), CancellationToken.None).WaitAsync(Wait));
        Assert.That(timeout!.Code, Is.EqualTo("ProviderUnavailable"));
        var late = new ScriptedSession((_, _, _) => Short());
        hanging.Creation.TrySetResult(late);
        await late.DisposedSignal.Task.WaitAsync(Wait);

        var thrown = Assert.ThrowsAsync<AgentRuntimeException>(async () =>
            await runtime.StartSessionAsync(new("throwing"), CancellationToken.None));
        Assert.That(thrown!.Code, Is.EqualTo("ProviderUnavailable"));
        Assert.That(thrown.Message, Does.Not.Contain("SECRET"));
    }

    [Test]
    public async Task TurnBudgetEndsAsTimedOutAndClosesOpenMessage()
    {
        var options = new AgentRuntimeOptions { TurnTimeout = TimeSpan.FromMilliseconds(200) };
        var provider = new ScriptedProvider((_, _, token) => Waiting(token));
        await using var runtime = new AgentRuntime([provider], null, options);
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);

        var events = await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None))
            .WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.TimedOut));
        Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).ErrorCode, Is.EqualTo("TurnTimedOut"));
    }

    [Test]
    public async Task UnknownToolAndUntrustedBindingsNeverReachRegistry()
    {
        var registry = new FakeRegistry();
        var bindings = new FakeBindings();
        bindings.Next.Enqueue(null);
        bindings.Next.Enqueue(new AgentToolBinding(bindings.Principal, AgentOutputDestination.ProviderExternal("other"),
            AgentOutputDataScope.Metadata));
        var calls = new[] { AgentToolCallId.New(), AgentToolCallId.New(), AgentToolCallId.New() };
        var provider = new ScriptedProvider((session, _, token) => Script(session, token));
        await using var runtime = AgentRuntimeTestFactory.Dispatch([provider], registry, bindings, new TestAgentPrincipalAuthority());
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);

        var events = await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None))
            .WaitAsync(Wait);

        AssertStreamInvariants(events);
        Assert.That(registry.Calls, Is.Empty);
        Assert.That(events.Single(item => item.Kind == AgentEventKind.ToolRequested && item.ToolCallId == calls[0]).ToolName,
            Is.Null, "Unknown raw tool names are not echoed.");
        var results = provider.Sessions[0].Results.ToArray();
        Assert.That(results.Select(item => (item.Status, item.ErrorCode)), Is.EqualTo(new[]
        {
            (AgentToolResultStatus.Failed, (string?)"UnknownTool"),
            (AgentToolResultStatus.Denied, (string?)"PermissionDenied"),
            (AgentToolResultStatus.Denied, (string?)"PermissionDenied"),
        }));
        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));

        async IAsyncEnumerable<AgentProviderEvent> Script(ScriptedSession scripted, [EnumeratorCancellation] CancellationToken token)
        {
            yield return new(AgentEventKind.ToolRequested, ToolCallId: calls[0], ToolName: "drop_everything_SECRET", ArgumentsJson: "{}");
            await scripted.NextResultAsync(token);
            yield return new(AgentEventKind.ToolRequested, ToolCallId: calls[1], ToolName: ToolName, ArgumentsJson: "{}");
            await scripted.NextResultAsync(token);
            yield return new(AgentEventKind.ToolRequested, ToolCallId: calls[2], ToolName: ToolName, ArgumentsJson: "{}");
            await scripted.NextResultAsync(token);
        }
    }

    [Test]
    public async Task LargeDeltaIsSplitWithinLimitsWithoutBreakingSurrogatePairs()
    {
        var text = string.Concat(Enumerable.Repeat("a\U0001F600", 5000));
        var message = AgentMessageId.New();
        var options = new AgentRuntimeOptions { MaxDeltaChars = 1000 };
        var provider = new ScriptedProvider((_, _, _) => Script());
        await using var runtime = new AgentRuntime([provider], null, options);
        var session = await runtime.StartSessionAsync(new("scripted"), CancellationToken.None);

        var events = await CollectAsync(runtime.RunTurnAsync(session, Request(AgentTurnId.New()), CancellationToken.None))
            .WaitAsync(Wait);

        var deltas = events.Where(item => item.Kind == AgentEventKind.MessageDelta).Select(item => item.Text!).ToArray();
        Assert.That(string.Concat(deltas), Is.EqualTo(text));
        Assert.That(deltas, Has.All.Length.LessThanOrEqualTo(1000));
        Assert.That(deltas.Select(item => char.IsHighSurrogate(item[^1])), Has.None.True);

        async IAsyncEnumerable<AgentProviderEvent> Script()
        {
            await Task.Yield();
            yield return new(AgentEventKind.MessageStarted, MessageId: message);
            yield return new(AgentEventKind.MessageDelta, text, MessageId: message);
            yield return new(AgentEventKind.MessageCompleted, MessageId: message);
        }
    }

    [Test]
    public async Task EventQueueNeverDropsControlEventsAndRejectsLateOnes()
    {
        long sequence = 0;
        var queue = new AgentRuntimeEventQueue(() => ++sequence, 4, 1024 * 1024, 16);
        var session = AgentSessionId.New();
        AgentEvent Make(long value, AgentEventKind kind) =>
            new(1, Guid.NewGuid(), session, null, value, DateTimeOffset.UtcNow, Guid.Empty, kind);

        for (var index = 0; index < 4; index++)
        {
            Assert.That(await queue.EnqueueFlowAsync(value => Make(value, AgentEventKind.TaskProgress), null, null,
                TimeSpan.FromSeconds(1), CancellationToken.None), Is.EqualTo(AgentEventEnqueueResult.Accepted));
        }

        Assert.That(await queue.EnqueueFlowAsync(value => Make(value, AgentEventKind.TaskProgress), null, null,
            TimeSpan.FromMilliseconds(100), CancellationToken.None), Is.EqualTo(AgentEventEnqueueResult.ConsumerUnavailable));
        Assert.That(queue.TryEnqueueControl(value => Make(value, AgentEventKind.ApprovalRequested)), Is.True);
        Assert.That(queue.Complete([value => Make(value, AgentEventKind.TaskCompleted)]), Is.True);
        Assert.That(queue.TryEnqueueControl(value => Make(value, AgentEventKind.ToolCompleted)), Is.False);
        Assert.That(await queue.EnqueueFlowAsync(value => Make(value, AgentEventKind.TaskProgress), null, null,
            TimeSpan.FromSeconds(1), CancellationToken.None), Is.EqualTo(AgentEventEnqueueResult.Completed));

        var drained = new List<AgentEvent>();
        while (await queue.DequeueAsync() is { } item)
        {
            drained.Add(item);
        }

        Assert.That(drained.Select(item => item.Sequence), Is.EqualTo(new long[] { 1, 2, 3, 4, 5, 6 }));
        Assert.That(drained[^1].Kind, Is.EqualTo(AgentEventKind.TaskCompleted));
    }

    [Test]
    public void RuntimeContractsReferenceNoProviderSdk()
    {
        var forbidden = new[] { "OpenAI", "Anthropic", "ModelContextProtocol", "MongoDB.Driver" };
        foreach (var assembly in new[] { typeof(AgentRuntime).Assembly, typeof(AgentEvent).Assembly })
        {
            Assert.That(assembly.GetReferencedAssemblies().Select(item => item.Name ?? string.Empty),
                Has.None.Matches<string>(name => forbidden.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))),
                assembly.GetName().Name);
        }

        var surface = new[] { typeof(IAgentRuntime), typeof(IAgentProvider), typeof(IAgentSession), typeof(IAgentContextProvider) }
            .SelectMany(type => type.GetMethods())
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .Concat(typeof(AgentEvent).GetProperties().Select(property => property.PropertyType))
            .Concat(typeof(AgentProviderEvent).GetProperties().Select(property => property.PropertyType))
            .SelectMany(Flatten)
            .Select(type => type.Namespace ?? string.Empty)
            .Distinct()
            .ToArray();
        Assert.That(surface, Has.All.Matches<string>(name => name.StartsWith("System", StringComparison.Ordinal) ||
            name.StartsWith("EsilvaSoft.SlopStudio", StringComparison.Ordinal)));

        static IEnumerable<Type> Flatten(Type type) =>
            type.IsGenericType ? type.GetGenericArguments().SelectMany(Flatten).Append(type) : [type];
    }

    private static AgentTurnRequest Request(AgentTurnId id, string message = "short") => new(id, message, "tab-1", 1);

    private static async Task<List<AgentEvent>> CollectAsync(IAsyncEnumerable<AgentEvent> stream, Func<AgentEvent, Task>? onEvent = null)
    {
        var events = new List<AgentEvent>();
        await foreach (var item in stream)
        {
            events.Add(item);
            if (onEvent is not null)
            {
                await onEvent(item);
            }
        }

        return events;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Condition was not reached in time.");
            }

            await Task.Delay(10);
        }
    }

    /// <summary>Contract invariants that every normalized stream must satisfy, whatever the provider did.</summary>
    private static void AssertStreamInvariants(List<AgentEvent> events)
    {
        Assert.That(events, Is.Not.Empty);
        Assert.That(events[0].Kind, Is.EqualTo(AgentEventKind.TaskStarted));
        Assert.That(events[^1].Kind, Is.EqualTo(AgentEventKind.TaskCompleted));
        Assert.That(events.Count(item => item.Kind == AgentEventKind.TaskCompleted), Is.EqualTo(1));
        Assert.That(events.Zip(events.Skip(1)).All(pair => pair.Second.Sequence > pair.First.Sequence), Is.True,
            "Sequence is strictly increasing.");
        Assert.That(events.Select(item => item.EventId).Distinct().Count(), Is.EqualTo(events.Count));
        Assert.That(events.Select(item => (item.SessionId, item.TurnId, item.CorrelationId)).Distinct().Count(), Is.EqualTo(1));
        Assert.That(events.All(item => item.ProtocolVersion == 1), Is.True);

        foreach (var message in events.Where(item => item.Kind == AgentEventKind.MessageStarted).Select(item => item.MessageId))
        {
            var ofMessage = events.Where(item => item.MessageId == message).ToList();
            Assert.That(ofMessage.Count(item => item.Kind == AgentEventKind.MessageStarted), Is.EqualTo(1));
            Assert.That(ofMessage.Count(item => item.Kind == AgentEventKind.MessageCompleted), Is.EqualTo(1));
            Assert.That(ofMessage[^1].Kind, Is.EqualTo(AgentEventKind.MessageCompleted), "No delta after the terminal.");
        }

        foreach (var call in events.Where(item => item.Kind == AgentEventKind.ToolRequested).Select(item => item.ToolCallId))
        {
            var ofCall = events.Where(item => item.ToolCallId == call).ToList();
            Assert.That(ofCall.Count(item => item.Kind == AgentEventKind.ToolRequested), Is.EqualTo(1));
            Assert.That(ofCall.Count(item => item.Kind is AgentEventKind.ToolCompleted or AgentEventKind.ToolFailed), Is.EqualTo(1));
            Assert.That(ofCall[^1].Kind, Is.AnyOf(AgentEventKind.ToolCompleted, AgentEventKind.ToolFailed));
        }

        foreach (var approval in events.Where(item => item.Kind == AgentEventKind.ApprovalRequested).Select(item => item.ApprovalId))
        {
            var ofApproval = events.Where(item => item.ApprovalId == approval).ToList();
            Assert.That(ofApproval.Count(item => item.Kind is AgentEventKind.ApprovalGranted or AgentEventKind.ApprovalDenied),
                Is.EqualTo(1));
        }
    }

    private static async IAsyncEnumerable<AgentProviderEvent> Short()
    {
        await Task.Yield();
        var message = AgentMessageId.New();
        yield return new(AgentEventKind.MessageStarted, MessageId: message);
        yield return new(AgentEventKind.MessageDelta, "ok", MessageId: message);
        yield return new(AgentEventKind.MessageCompleted, MessageId: message);
    }

    private static async IAsyncEnumerable<AgentProviderEvent> Waiting([EnumeratorCancellation] CancellationToken token)
    {
        yield return new(AgentEventKind.MessageStarted);
        await Task.Delay(Timeout.Infinite, token);
    }

    private static async IAsyncEnumerable<AgentProviderEvent> Gated(ScriptedSession session, [EnumeratorCancellation] CancellationToken token)
    {
        yield return new(AgentEventKind.MessageStarted);
        await session.Release.Task.WaitAsync(token);
        yield return new(AgentEventKind.MessageCompleted);
    }

    private static async IAsyncEnumerable<AgentProviderEvent> RequestTool(ScriptedSession session, [EnumeratorCancellation] CancellationToken token)
    {
        yield return new(AgentEventKind.ToolRequested, ToolCallId: AgentToolCallId.New(), ToolName: ToolName, ArgumentsJson: "{}");
        await session.NextResultAsync(token);
    }

    private static async IAsyncEnumerable<AgentProviderEvent> AskApproval(
        ScriptedSession session, AgentApprovalId approval, [EnumeratorCancellation] CancellationToken token)
    {
        yield return new(AgentEventKind.ApprovalRequested, ApprovalId: approval);
        await session.NextDecisionAsync(token);
    }

    private delegate IAsyncEnumerable<AgentProviderEvent> TurnScript(
        ScriptedSession session, AgentTurnRequest request, CancellationToken cancellationToken);

    private sealed class ScriptedProvider(TurnScript script, string providerId = "scripted", bool isLocal = false) : IAgentProvider
    {
        public bool IsLocal => isLocal;

        private readonly List<ScriptedSession> _sessions = [];

        public string ProviderId => providerId;

        public IReadOnlyList<ScriptedSession> Sessions
        {
            get
            {
                lock (_sessions)
                {
                    return [.. _sessions];
                }
            }
        }

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
        {
            var session = new ScriptedSession(script);
            lock (_sessions)
            {
                _sessions.Add(session);
            }

            return Task.FromResult<IAgentSession>(session);
        }
    }

    private sealed class ScriptedSession(TurnScript script) : IAgentSession
    {
        private readonly Channel<AgentToolResult> _results = Channel.CreateUnbounded<AgentToolResult>();
        private readonly Channel<AgentApprovalDecision> _decisions = Channel.CreateUnbounded<AgentApprovalDecision>();

        public ConcurrentQueue<AgentToolResult> Results { get; } = new();

        public ConcurrentQueue<AgentApprovalDecision> Decisions { get; } = new();

        public AgentTurnRequest? LastRequest { get; private set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource TokenCancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource DisposedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IAsyncEnumerable<AgentProviderEvent> RunTurnAsync(AgentTurnRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            cancellationToken.Register(() => TokenCancelled.TrySetResult());
            Entered.TrySetResult();
            return script(this, request, cancellationToken);
        }

        public async Task<AgentToolResult> NextResultAsync(CancellationToken cancellationToken) =>
            await _results.Reader.ReadAsync(cancellationToken);

        public async Task<AgentApprovalDecision> NextDecisionAsync(CancellationToken cancellationToken) =>
            await _decisions.Reader.ReadAsync(cancellationToken);

        public Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken)
        {
            Results.Enqueue(result);
            _results.Writer.TryWrite(result);
            return Task.CompletedTask;
        }

        public Task SubmitApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken)
        {
            Decisions.Enqueue(decision);
            _decisions.Writer.TryWrite(decision);
            return Task.CompletedTask;
        }

        public Task CancelTurnAsync(AgentTurnId turnId, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposedSignal.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class HangingProvider : IAgentProvider
    {
        public string ProviderId => "hanging";

        public TaskCompletionSource<IAgentSession> Creation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            Creation.Task;
    }

    private sealed class ThrowingProvider : IAgentProvider
    {
        public string ProviderId => "throwing";

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("SECRET_TOKEN");
    }

    private sealed class FakeAuthority : IAgentInteractionAuthority
    {
        public Func<AgentToolResult, bool> AcceptResult { get; init; } = static _ => true;

        public Func<AgentApprovalDecision, bool> AcceptDecision { get; init; } = static _ => true;

        public Action<AgentToolCallId>? OnToolRequest { get; set; }

        public Task<bool> ValidateToolRequestAsync(AgentSessionId sessionId, AgentTurnId turnId, AgentToolCallId toolCallId,
            CancellationToken cancellationToken)
        {
            OnToolRequest?.Invoke(toolCallId);
            return Task.FromResult(true);
        }

        public Task<bool> ValidateToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) =>
            Task.FromResult(AcceptResult(result));

        public Task<bool> ValidateApprovalRequestAsync(AgentSessionId sessionId, AgentTurnId turnId, AgentApprovalId approvalId,
            CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> ValidateApprovalDecisionAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
            Task.FromResult(AcceptDecision(decision));
    }

    private sealed class FakeBindings : IAgentToolBindingProvider
    {
        public AgentPrincipal Principal { get; } = new(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);

        public ConcurrentQueue<AgentToolBinding?> Next { get; } = new();

        public Task<AgentToolBinding?> ResolveAsync(AgentSessionId sessionId, AgentTurnId turnId, string providerId,
            string toolName, CancellationToken cancellationToken) =>
            Task.FromResult(Next.TryDequeue(out var configured)
                ? configured
                : Default?.Invoke(providerId) ??
                  new AgentToolBinding(Principal, AgentOutputDestination.ProviderExternal(providerId), AgentOutputDataScope.Metadata));

        public Func<string, AgentToolBinding?>? Default { get; set; }
    }

    private sealed record RegistryCall(
        AgentPrincipal Principal, AgentInvocationContext Context, AgentOutputDestination Destination, string? Arguments);

    private sealed class FakeRegistry : IAgentToolRegistry
    {
        public const string Payload = "{\"connections\":[]}";
        private static readonly AgentToolDescriptor Descriptor = new(ToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]);
        private readonly ConcurrentQueue<RegistryCall> _calls = new();
        private int _completed;

        public bool HonorCancellation { get; init; }

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<RegistryCall> Calls => [.. _calls];

        public int CompletedCount => Volatile.Read(ref _completed);

        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [Descriptor];

        public AgentToolDescriptor? FindDescriptor(string? name) => name == ToolName ? Descriptor : null;

        public string? GetInputSchemaJson(string? name) => null;

        public string? GetOutputSchemaJson(string? name) => null;

        public async Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default)
        {
            _calls.Enqueue(new RegistryCall(principal!, invocationContext!, destination!, argumentsJson));
            try
            {
                await (HonorCancellation ? Release.Task.WaitAsync(cancellationToken) : Release.Task);
                return AgentToolInvocationResult.Success(Payload);
            }
            finally
            {
                Interlocked.Increment(ref _completed);
            }
        }
    }
}
