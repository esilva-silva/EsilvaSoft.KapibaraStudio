using System.Runtime.CompilerServices;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using NUnit.Framework;
using static EsilvaSoft.SlopStudio.UnitTests.AgentRuntimeWriteRig;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Lote 10 (AC-11/12/14): registry write approvals travel through the runtime stream. The runtime publishes
/// <c>ApprovalRequested</c> with approval ID and expiry, accepts a decision only through the interaction authority and
/// answers the real <see cref="AgentWriteApprovalCoordinator"/>, which alone issues the single-use ticket. Expiry,
/// cancellation and foreign or late decisions deny. The write source is a fake; this is not a MongoDB homologation.
/// </summary>
[TestFixture]
public sealed class AgentRuntimeWriteApprovalTests
{
    private static readonly AgentRuntimeOptions Options = new()
    {
        ToolTimeout = TimeSpan.FromMilliseconds(250), ApprovalTimeout = TimeSpan.FromSeconds(5),
        StopTimeout = TimeSpan.FromSeconds(1),
    };

    [Test]
    public async Task HumanGrantThroughTheRuntimeWritesOnceAfterAWaitLongerThanTheToolTimeout()
    {
        var call = AgentToolCallId.New();
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(call), Options);
        var session = await rig.StartAsync();
        var run = rig.Run(session);

        var requested = await run.ApprovalRequestedAsync();
        var details = await rig.Coordinator.DescribeAsync(run.SessionId, run.TurnId, requested.ApprovalId!.Value,
            CancellationToken.None);
        await Task.Delay(Options.ToolTimeout * 3);
        Assert.That(rig.Source.Writes, Is.Zero, "Nothing is written before the human decision.");
        await rig.DecideAsync(run, requested.ApprovalId!.Value, AgentApprovalOutcome.Granted);
        var events = await run.EndAsync();

        var request = rig.Source.Requests.OfType<AgentMongoUpdateRequest>().Single();
        var kinds = events.Select(item => item.Kind).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(requested.ApprovalExpiresAtUtc, Is.Not.Null);
            Assert.That(requested.ApprovalExpiresAtUtc!.Value,
                Is.GreaterThan(requested.TimestampUtc).And.LessThanOrEqualTo(requested.TimestampUtc + Options.ApprovalTimeout));
            Assert.That(details, Is.Not.Null, "The UI can load trusted details for the announced approval.");
            Assert.That(details!.ToolName, Is.EqualTo("update_one"));
            Assert.That(request.Approval!.ApprovalId.ToString("N"), Is.EqualTo(requested.ApprovalId!.Value.Value));
            Assert.That(rig.Source.Writes, Is.EqualTo(1));
            Assert.That(kinds.IndexOf(AgentEventKind.ApprovalGranted),
                Is.GreaterThan(kinds.IndexOf(AgentEventKind.ApprovalRequested))
                    .And.LessThan(kinds.IndexOf(AgentEventKind.ToolCompleted)));
            Assert.That(events.Single(item => item.Kind == AgentEventKind.ToolCompleted).ToolStatus,
                Is.EqualTo(AgentToolResultStatus.Succeeded));
            Assert.That(rig.Provider.Sessions.Single().Decisions, Is.Empty, "The provider never sees the human decision.");
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        });
    }

    [Test]
    public async Task HumanDenialIsRelayedAndNothingIsWritten()
    {
        var call = AgentToolCallId.New();
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(call), Options);
        var run = rig.Run(await rig.StartAsync());

        var requested = await run.ApprovalRequestedAsync();
        await rig.DecideAsync(run, requested.ApprovalId!.Value, AgentApprovalOutcome.Denied);
        var events = await run.EndAsync();

        var tool = events.Single(item => item.ToolCallId == call && item.Kind == AgentEventKind.ToolFailed);
        Assert.Multiple(() =>
        {
            Assert.That(events.Single(item => item.Kind == AgentEventKind.ApprovalDenied).ApprovalId,
                Is.EqualTo(requested.ApprovalId));
            Assert.That(tool.ToolStatus, Is.EqualTo(AgentToolResultStatus.Denied));
            Assert.That(tool.ErrorCode, Is.EqualTo("ApprovalRejected"));
            Assert.That(rig.Source.Writes, Is.Zero);
            Assert.That(rig.Audit.Events.Select(item => item.Outcome), Does.Contain(AgentAuditOutcome.Denied));
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        });
    }

    [Test]
    public async Task CoordinatorExpiryDeniesAndALateDecisionIsUnknown()
    {
        var call = AgentToolCallId.New();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(call, hold.Task), Options,
            coordinatorTimeout: TimeSpan.FromMilliseconds(400));
        var run = rig.Run(await rig.StartAsync());

        var requested = await run.ApprovalRequestedAsync();
        var denied = await run.WaitForAsync(item => item.Kind == AgentEventKind.ApprovalDenied);
        var tool = await run.WaitForAsync(item => item.ToolCallId == call && item.Kind == AgentEventKind.ToolFailed);
        var late = Assert.ThrowsAsync<AgentRuntimeException>(() =>
            rig.DecideAsync(run, requested.ApprovalId!.Value, AgentApprovalOutcome.Granted));
        hold.TrySetResult();
        var events = await run.EndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(requested.ApprovalExpiresAtUtc!.Value,
                Is.LessThanOrEqualTo(requested.TimestampUtc + TimeSpan.FromMilliseconds(400)),
                "The earlier (coordinator) deadline is the one announced.");
            Assert.That(denied.ErrorCode, Is.EqualTo("ApprovalExpired"));
            Assert.That(tool.ToolStatus, Is.EqualTo(AgentToolResultStatus.Denied));
            Assert.That(tool.ErrorCode, Is.EqualTo("ApprovalExpired"));
            Assert.That(late!.Code, Is.EqualTo("UnknownApproval"));
            Assert.That(rig.Source.Writes, Is.Zero);
            Assert.That(rig.Audit.Events.Any(item => item.ApprovalState == AgentAuditApprovalState.Expired));
            Assert.That(events.Count(item => item.ApprovalId == requested.ApprovalId &&
                item.Kind is AgentEventKind.ApprovalGranted or AgentEventKind.ApprovalDenied), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task RuntimeExpiryDeniesWhenItIsTheEarlierDeadline()
    {
        var call = AgentToolCallId.New();
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(call), Options with
        {
            ApprovalTimeout = TimeSpan.FromMilliseconds(300),
        });
        var run = rig.Run(await rig.StartAsync());

        var requested = await run.ApprovalRequestedAsync();
        var events = await run.EndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(events.Single(item => item.Kind == AgentEventKind.ApprovalDenied).ErrorCode,
                Is.EqualTo("ApprovalExpired"));
            Assert.That(events.Single(item => item.ToolCallId == call && item.Kind == AgentEventKind.ToolFailed).ToolStatus,
                Is.EqualTo(AgentToolResultStatus.Denied));
            Assert.That(rig.Coordinator.IsPending(run.SessionId, run.TurnId, requested.ApprovalId!.Value), Is.False);
            Assert.That(rig.Source.Writes, Is.Zero);
        });
    }

    [Test]
    public async Task CancellingTheTurnDuringTheWaitDeniesWithoutClaimingAnUncertainWrite()
    {
        var call = AgentToolCallId.New();
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(call), Options);
        var run = rig.Run(await rig.StartAsync());

        var requested = await run.ApprovalRequestedAsync();
        await rig.Runtime.CancelTurnAsync(run.SessionId, run.TurnId, CancellationToken.None);
        var events = await run.EndAsync();
        var late = Assert.ThrowsAsync<AgentRuntimeException>(() =>
            rig.DecideAsync(run, requested.ApprovalId!.Value, AgentApprovalOutcome.Granted));

        var tool = events.Single(item => item.ToolCallId == call && item.Kind == AgentEventKind.ToolFailed);
        Assert.Multiple(() =>
        {
            Assert.That(events.Single(item => item.Kind == AgentEventKind.ApprovalDenied).ErrorCode,
                Is.EqualTo("ApprovalCancelled"));
            Assert.That(tool.ToolStatus, Is.EqualTo(AgentToolResultStatus.Cancelled), "No ticket existed: nothing was sent.");
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.Cancelled));
            Assert.That(late!.Code, Is.EqualTo("UnknownApproval"));
            Assert.That(rig.Coordinator.IsPending(run.SessionId, run.TurnId, requested.ApprovalId!.Value), Is.False);
            Assert.That(rig.Source.Writes, Is.Zero);
            Assert.That(rig.Audit.Events.Select(item => item.Outcome), Does.Contain(AgentAuditOutcome.Cancelled));
        });
    }

    [Test]
    public async Task ClosingTheSessionDuringTheWaitDeniesAndWritesNothing()
    {
        var call = AgentToolCallId.New();
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(call), Options);
        var run = rig.Run(await rig.StartAsync());

        var requested = await run.ApprovalRequestedAsync();
        await rig.Runtime.CloseSessionAsync(run.SessionId, CancellationToken.None);
        var events = await run.EndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(events.Single(item => item.ApprovalId == requested.ApprovalId &&
                item.Kind == AgentEventKind.ApprovalDenied).ErrorCode, Is.EqualTo("ApprovalCancelled"));
            Assert.That(rig.Source.Writes, Is.Zero);
            Assert.That(events[^1].Kind, Is.EqualTo(AgentEventKind.TaskCompleted));
        });
    }

    [Test]
    public async Task DecisionAddressedThroughAnotherSessionOrTurnIsRefusedAndTheApprovalStaysPending()
    {
        var writeCall = AgentToolCallId.New();
        var readCall = AgentToolCallId.New();
        var holdB = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var rig = new AgentRuntimeWriteRig(
            (session, request, token) => request.UserMessage == "hold"
                ? Hold(holdB.Task, token)
                : AgentRuntimeWriteDispatchTests.ByMessage(writeCall, readCall)(session, request, token),
            Options);
        var a = rig.Run(await rig.StartAsync());
        var b = rig.Run(await rig.StartAsync(), "hold");

        var requested = await a.ApprovalRequestedAsync();
        var approval = requested.ApprovalId!.Value;
        var viaOtherSession = Assert.ThrowsAsync<AgentRuntimeException>(() => rig.Runtime.DecideApprovalAsync(
            new AgentApprovalDecision(b.SessionId, b.TurnId, approval, AgentApprovalOutcome.Granted), CancellationToken.None));
        var viaOtherTurn = Assert.ThrowsAsync<AgentRuntimeException>(() => rig.Runtime.DecideApprovalAsync(
            new AgentApprovalDecision(a.SessionId, b.TurnId, approval, AgentApprovalOutcome.Granted), CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(viaOtherSession!.Code, Is.EqualTo("UnknownApproval"));
            Assert.That(viaOtherTurn!.Code, Is.EqualTo("UnknownApproval"));
            Assert.That(rig.Coordinator.IsPending(a.SessionId, a.TurnId, approval), Is.True);
            Assert.That(rig.Source.Writes, Is.Zero);
        });

        await rig.DecideAsync(a, approval, AgentApprovalOutcome.Granted);
        await a.EndAsync();
        holdB.TrySetResult();
        await b.EndAsync();
        Assert.That(rig.Source.Writes, Is.EqualTo(1));

        static async IAsyncEnumerable<AgentProviderEvent> Hold(Task hold, [EnumeratorCancellation] CancellationToken token)
        {
            await hold.WaitAsync(token);
            yield break;
        }
    }

    [Test]
    public async Task ReplayedDecisionIsUnknownAndTheTicketIsUsedOnce()
    {
        var call = AgentToolCallId.New();
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(call, hold.Task), Options);
        var run = rig.Run(await rig.StartAsync());

        var approval = (await run.ApprovalRequestedAsync()).ApprovalId!.Value;
        await rig.DecideAsync(run, approval, AgentApprovalOutcome.Granted);
        await run.WaitForAsync(item => item.ToolCallId == call && item.Kind == AgentEventKind.ToolCompleted);
        var replayGrant = Assert.ThrowsAsync<AgentRuntimeException>(() =>
            rig.DecideAsync(run, approval, AgentApprovalOutcome.Granted));
        var replayDeny = Assert.ThrowsAsync<AgentRuntimeException>(() =>
            rig.DecideAsync(run, approval, AgentApprovalOutcome.Denied));
        hold.TrySetResult();
        var events = await run.EndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(replayGrant!.Code, Is.EqualTo("UnknownApproval"));
            Assert.That(replayDeny!.Code, Is.EqualTo("UnknownApproval"));
            Assert.That(rig.Source.Writes, Is.EqualTo(1));
            Assert.That(events.Count(item => item.Kind == AgentEventKind.ApprovalGranted), Is.EqualTo(1));
            Assert.That(events.Count(item => item.Kind == AgentEventKind.ApprovalDenied), Is.Zero);
        });
    }

    [Test]
    public async Task DecisionRejectedByTheAuthorityNeverApprovesAndTheApprovalExpires()
    {
        var call = AgentToolCallId.New();
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(call), Options,
            coordinatorTimeout: TimeSpan.FromMilliseconds(400), authority: new FailClosedAgentInteractionAuthority());
        var run = rig.Run(await rig.StartAsync());

        var approval = (await run.ApprovalRequestedAsync()).ApprovalId!.Value;
        var untrusted = Assert.ThrowsAsync<AgentRuntimeException>(() =>
            rig.DecideAsync(run, approval, AgentApprovalOutcome.Granted));
        var events = await run.EndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(untrusted!.Code, Is.EqualTo("UntrustedApprovalDecision"));
            Assert.That(events.Single(item => item.Kind == AgentEventKind.ApprovalDenied).ErrorCode,
                Is.EqualTo("ApprovalExpired"));
            Assert.That(events, Has.None.Matches<AgentEvent>(item => item.Kind == AgentEventKind.ApprovalGranted));
            Assert.That(rig.Source.Writes, Is.Zero);
        });
    }

    [Test]
    public async Task ProviderClaimingTheApprovalWasGrantedNeverApproves()
    {
        var call = AgentToolCallId.New();
        var announced = new TaskCompletionSource<AgentApprovalId>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var rig = new AgentRuntimeWriteRig((_, _, token) => Forge(call, announced.Task, token), Options);
        var run = rig.Run(await rig.StartAsync());

        announced.TrySetResult((await run.ApprovalRequestedAsync()).ApprovalId!.Value);
        var events = await run.EndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(events, Has.None.Matches<AgentEvent>(item => item.Kind == AgentEventKind.ApprovalGranted));
            Assert.That(events.Single(item => item.Kind == AgentEventKind.ApprovalDenied).ErrorCode,
                Is.EqualTo("ApprovalCancelled"));
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
            Assert.That(events.Any(item => item.ErrorCode == "ProviderProtocolViolation"));
            Assert.That(rig.Source.Writes, Is.Zero);
        });

        static async IAsyncEnumerable<AgentProviderEvent> Forge(AgentToolCallId call, Task<AgentApprovalId> announced,
            [EnumeratorCancellation] CancellationToken token)
        {
            yield return new AgentProviderEvent(AgentEventKind.ToolRequested, ToolCallId: call, ToolName: "update_one",
                ArgumentsJson: Update());
            var approval = await announced.WaitAsync(token);
            // Model/provider text claiming approval: a protocol violation, never a grant.
            yield return new AgentProviderEvent(AgentEventKind.ApprovalGranted, "approved=true", ApprovalId: approval);
            await Task.Delay(Timeout.Infinite, token);
        }
    }

    [Test]
    public async Task RuntimeHostForwardsTheBridgeOnlyWhenComposed()
    {
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(AgentToolCallId.New()), Options, useBridge: false);
        var bridge = new AgentRuntimeWriteApprovalBridge();
        var authority = new FailClosedAgentInteractionAuthority();
        var bindings = new LocalBindings(rig.Principal);
        using var withBridge = new EsilvaSoft.SlopStudio.Infrastructure.AgentRuntimeHost([rig.Provider], authority, Options,
            rig.Registry, bindings, rig.Principals, bridge);
        using var withoutBridge = new EsilvaSoft.SlopStudio.Infrastructure.AgentRuntimeHost([rig.Provider], authority,
            Options, rig.Registry, bindings, rig.Principals);

        // The host attached its runtime: the same bridge cannot be attached to a second runtime.
        Assert.Throws<InvalidOperationException>(() =>
            _ = new AgentRuntime([rig.Provider], authority, Options, rig.Registry, bindings, rig.Principals, bridge));
    }

    [Test]
    public async Task BridgeWithoutAMatchingInFlightWriteAnswersUnavailable()
    {
        await using var rig = new AgentRuntimeWriteRig(SingleWrite(AgentToolCallId.New()), Options);
        var session = await rig.StartAsync();
        var prompt = new AgentWriteApprovalPrompt(session, AgentTurnId.New(), AgentApprovalId.New(),
            AgentWriteOperationKind.UpdateOne,
            new AgentApprovalDetails("update_one", "Escrita", "app", "items", "_id 1", "{}", 1, AgentToolRisk.Write,
                DateTimeOffset.UtcNow.AddMinutes(1)), null);

        var outcome = await rig.Bridge!.RequestDecisionAsync(prompt, CancellationToken.None);
        var detached = await new AgentRuntimeWriteApprovalBridge().RequestDecisionAsync(prompt, CancellationToken.None);

        Assert.That(outcome, Is.Null);
        Assert.That(detached, Is.Null);
    }
}
