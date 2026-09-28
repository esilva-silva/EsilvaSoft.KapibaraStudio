using System.Runtime.CompilerServices;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using NUnit.Framework;
using static EsilvaSoft.SlopStudio.UnitTests.AgentRuntimeWriteRig;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Lote 10, runtime side of registry writes (AC-11/12/14): deadlines that accommodate the human wait, uncertain
/// outcomes that are never reported as clean nor replayed, and one write per session that never holds execution slots
/// needed by reads. Runtime + real registry + real approval coordinator; the write source is a fake.
/// </summary>
[TestFixture]
public sealed class AgentRuntimeWriteDispatchTests
{
    private static readonly AgentRuntimeOptions ShortTool = new()
    {
        ToolTimeout = TimeSpan.FromMilliseconds(250), ApprovalTimeout = TimeSpan.FromSeconds(5),
        StopTimeout = TimeSpan.FromSeconds(1),
    };

    [Test]
    public async Task HumanWaitLongerThanToolTimeoutDoesNotCancelTheApprovedWrite()
    {
        var call = AgentToolCallId.New();
        var prompt = new ScriptedWritePrompt
        {
            Handler = static async (_, token) =>
            {
                // The human takes longer than the whole execution budget of a tool call.
                await Task.Delay(TimeSpan.FromMilliseconds(700), token);
                return AgentApprovalOutcome.Granted;
            },
        };
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(call), ShortTool, useBridge: false, prompt: prompt);
        var session = await rig.StartAsync();

        var events = await rig.Run(session).EndAsync();

        var terminal = events.Single(item => item.ToolCallId == call && item.Kind is AgentEventKind.ToolCompleted or
            AgentEventKind.ToolFailed);
        Assert.Multiple(() =>
        {
            Assert.That(terminal.Kind, Is.EqualTo(AgentEventKind.ToolCompleted), terminal.ErrorCode);
            Assert.That(terminal.ToolStatus, Is.EqualTo(AgentToolResultStatus.Succeeded));
            Assert.That(rig.Source.Writes, Is.EqualTo(1));
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        });
    }

    [Test]
    public async Task SentWriteThatLosesItsAnswerIsOutcomeUnknownForToolProviderAndTurn()
    {
        var call = AgentToolCallId.New();
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(call), ShortTool, useBridge: false);
        // Crash after the command left: the fake source throws once the write was attempted.
        rig.Source.OnWrite = static _ => throw new IOException("connection reset after send");
        var session = await rig.StartAsync();

        var events = await rig.Run(session).EndAsync();

        var terminal = events.Single(item => item.ToolCallId == call && item.Kind == AgentEventKind.ToolFailed);
        var delivered = rig.Provider.Sessions.Single().Results.Single();
        Assert.Multiple(() =>
        {
            Assert.That(terminal.ToolStatus, Is.EqualTo(AgentToolResultStatus.OutcomeUnknown));
            Assert.That(terminal.ErrorCode, Is.EqualTo("ToolOutcomeUnknown"));
            Assert.That(delivered.Status, Is.EqualTo(AgentToolResultStatus.OutcomeUnknown));
            Assert.That(delivered.Data, Is.Null);
            Assert.That(rig.Source.Writes, Is.EqualTo(1), "No retry after an uncertain send.");
            Assert.That(rig.Audit.Events.Select(item => item.Outcome), Does.Contain(AgentAuditOutcome.Uncertain));
            // The provider ended naturally, but a possibly applied write keeps the turn uncertain.
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown));
            Assert.That(events.Any(item => item.Kind == AgentEventKind.AgentError && item.ErrorCode == "ToolOutcomeUnknown"));
        });
    }

    [Test]
    public async Task AppliedWriteWithPendingAuditIsOutcomeUnknownAndItsDataIsNeverSent()
    {
        var call = AgentToolCallId.New();
        var audit = new ConcurrentMemoryAudit { Fail = static entry => entry.Outcome == AgentAuditOutcome.Succeeded };
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(call), ShortTool, useBridge: false, audit: audit);
        var session = await rig.StartAsync();

        var events = await rig.Run(session).EndAsync();

        var terminal = events.Single(item => item.ToolCallId == call && item.Kind == AgentEventKind.ToolFailed);
        var delivered = rig.Provider.Sessions.Single().Results.Single();
        Assert.Multiple(() =>
        {
            Assert.That(rig.Source.Writes, Is.EqualTo(1));
            Assert.That(terminal.ToolStatus, Is.EqualTo(AgentToolResultStatus.OutcomeUnknown));
            Assert.That(terminal.ErrorCode, Is.EqualTo("AppliedAuditPending"));
            Assert.That(delivered.Status, Is.EqualTo(AgentToolResultStatus.OutcomeUnknown));
            Assert.That(delivered.ErrorCode, Is.EqualTo("AppliedAuditPending"));
            Assert.That(delivered.Data, Is.Null, "Withheld write output is never forwarded.");
            Assert.That(events.Select(item => item.Text ?? string.Empty), Has.None.Contains("\"Applied\""));
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown));
        });
    }

    [Test]
    public async Task RepeatedToolRequestAfterUncertainWriteIsNeverReplayed()
    {
        var call = AgentToolCallId.New();
        await using var rig = new AgentRuntimeWriteRig(Replay(call), ShortTool, useBridge: false);
        rig.Source.OnWrite = static _ => Task.FromResult(
            new AgentMongoWriteResult(AgentMongoWriteStatus.OutcomeUnknown, 0, null, false));
        var session = await rig.StartAsync();

        var events = await rig.Run(session).EndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rig.Source.Writes, Is.EqualTo(1));
            Assert.That(rig.Registry.Invoked, Has.Count.EqualTo(1));
            Assert.That(events.Count(item => item.Kind == AgentEventKind.ToolRequested), Is.EqualTo(1));
            Assert.That(events.Single(item => item.Kind == AgentEventKind.ToolFailed).ToolStatus,
                Is.EqualTo(AgentToolResultStatus.OutcomeUnknown));
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown));
        });

        static WriteScript Replay(AgentToolCallId call) => (session, _, token) => Script(session, call, token);

        static async IAsyncEnumerable<AgentProviderEvent> Script(
            WriteScriptSession session, AgentToolCallId call, [EnumeratorCancellation] CancellationToken token)
        {
            var request = new AgentProviderEvent(AgentEventKind.ToolRequested, ToolCallId: call, ToolName: "update_one",
                ArgumentsJson: Update());
            yield return request;
            await session.NextResultAsync(token);
            // A provider reconnect/retry re-announcing the same call must not dispatch the write again.
            yield return request;
            await Task.Delay(100, token);
        }
    }

    [Test]
    public async Task PendingWriteApprovalHoldsNoSlotNeededByReadsOfAnotherSession()
    {
        var options = ShortTool with
        {
            ToolTimeout = TimeSpan.FromSeconds(2), MaxConcurrentToolsGlobal = 1, MaxConcurrentToolsPerSession = 1,
        };
        var release = new TaskCompletionSource<AgentApprovalOutcome?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = new ScriptedWritePrompt { Handler = (_, _) => release.Task };
        var write = AgentToolCallId.New();
        var read = AgentToolCallId.New();
        await using var rig = new AgentRuntimeWriteRig(ByMessage(write, read), options, useBridge: false, prompt: prompt);
        var a = await rig.StartAsync();
        var b = await rig.StartAsync();

        var writer = rig.Run(a, "write");
        await WaitUntilAsync(() => prompt.Prompts.Count == 1);
        var reader = rig.Run(b, "read");
        var readEvents = await reader.EndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(rig.Registry.Invoked, Does.Contain(AgentToolRegistry.ListConnectionsToolName));
            Assert.That(readEvents.Single(item => item.ToolCallId == read &&
                item.Kind is AgentEventKind.ToolCompleted or AgentEventKind.ToolFailed).ErrorCode,
                Is.Not.EqualTo("ToolDeadlineExceeded"));
            Assert.That(writer.Completion.IsCompleted, Is.False, "The write is still waiting for its human.");
        });

        release.TrySetResult(AgentApprovalOutcome.Granted);
        var writeEvents = await writer.EndAsync();
        Assert.That(writeEvents.Single(item => item.ToolCallId == write && item.Kind == AgentEventKind.ToolCompleted)
            .ToolStatus, Is.EqualTo(AgentToolResultStatus.Succeeded));
        Assert.That(rig.Source.Writes, Is.EqualTo(1));
    }

    [Test]
    public async Task SecondWriteOfTheSameSessionIsBusyWhileTheFirstAwaitsApproval()
    {
        var release = new TaskCompletionSource<AgentApprovalOutcome?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = new ScriptedWritePrompt { Handler = (_, _) => release.Task };
        var first = AgentToolCallId.New();
        var second = AgentToolCallId.New();
        await using var rig = new AgentRuntimeWriteRig(TwoWrites(first, second), ShortTool, useBridge: false,
            prompt: prompt);
        var session = await rig.StartAsync();

        var run = rig.Run(session);
        var busy = await run.WaitForAsync(item => item.ToolCallId == second && item.Kind == AgentEventKind.ToolFailed);
        Assert.Multiple(() =>
        {
            Assert.That(busy.ErrorCode, Is.EqualTo("Busy"));
            Assert.That(busy.ToolStatus, Is.EqualTo(AgentToolResultStatus.Failed));
            Assert.That(prompt.Prompts, Has.Count.EqualTo(1), "Only one human approval at a time per session.");
        });

        release.TrySetResult(AgentApprovalOutcome.Granted);
        var events = await run.EndAsync();
        Assert.That(events.Single(item => item.ToolCallId == first && item.Kind == AgentEventKind.ToolCompleted).ToolStatus,
            Is.EqualTo(AgentToolResultStatus.Succeeded));
        Assert.That(rig.Source.Writes, Is.EqualTo(1));

        static WriteScript TwoWrites(AgentToolCallId first, AgentToolCallId second) =>
            (session, _, token) => Script(session, first, second, token);

        static async IAsyncEnumerable<AgentProviderEvent> Script(WriteScriptSession session, AgentToolCallId first,
            AgentToolCallId second, [EnumeratorCancellation] CancellationToken token)
        {
            yield return new AgentProviderEvent(AgentEventKind.ToolRequested, ToolCallId: first, ToolName: "update_one",
                ArgumentsJson: Update());
            yield return new AgentProviderEvent(AgentEventKind.ToolRequested, ToolCallId: second, ToolName: "update_one",
                ArgumentsJson: Update("{\"$set\":{\"v\":3}}"));
            await session.NextResultAsync(token);
            await session.NextResultAsync(token);
        }
    }

    internal static WriteScript ByMessage(AgentToolCallId write, AgentToolCallId read) =>
        (session, request, token) => request.UserMessage == "read"
            ? One(session, read, AgentToolRegistry.ListConnectionsToolName, "{}", token)
            : One(session, write, "update_one", Update(), token);

    private static async IAsyncEnumerable<AgentProviderEvent> One(WriteScriptSession session, AgentToolCallId call,
        string tool, string arguments, [EnumeratorCancellation] CancellationToken token)
    {
        yield return new AgentProviderEvent(AgentEventKind.ToolRequested, ToolCallId: call, ToolName: tool,
            ArgumentsJson: arguments);
        await session.NextResultAsync(token);
    }

    internal static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Condição não atingida.");
            }

            await Task.Delay(5);
        }
    }
}
