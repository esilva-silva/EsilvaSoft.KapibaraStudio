using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using static EsilvaSoft.SlopStudio.UnitTests.AgentWriteTestDoubles;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>Lote 10 (AC-12): immutable, operation-bound, single-use and expiring write approvals.</summary>
[TestFixture]
public sealed class AgentWriteApprovalCoordinatorTests
{
    [Test]
    public async Task GrantedTicketIsConsumedExactlyOnceAndReplayIsRefused()
    {
        var coordinator = new AgentWriteApprovalCoordinator(new ScriptedWritePrompt());
        var proposal = Proposal();

        var grant = await coordinator.RequestApprovalAsync(proposal, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(grant.Verdict, Is.EqualTo(AgentWriteApprovalVerdict.Granted));
            Assert.That(coordinator.TryConsume(grant.Ticket, Proposal()), Is.EqualTo(AgentWriteApprovalConsumption.Consumed));
            Assert.That(coordinator.TryConsume(grant.Ticket, Proposal()),
                Is.EqualTo(AgentWriteApprovalConsumption.AlreadyConsumed));
        });
    }

    [Test]
    public async Task ConcurrentConsumersOfOneTicketGetExactlyOneConsumption()
    {
        var coordinator = new AgentWriteApprovalCoordinator(new ScriptedWritePrompt());
        var grant = await coordinator.RequestApprovalAsync(Proposal(), CancellationToken.None);
        using var start = new ManualResetEventSlim();

        var attempts = Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            start.Wait();
            return coordinator.TryConsume(grant.Ticket, Proposal());
        })).ToArray();
        start.Set();
        var results = await Task.WhenAll(attempts);

        Assert.That(results.Count(result => result == AgentWriteApprovalConsumption.Consumed), Is.EqualTo(1));
        Assert.That(results.Where(result => result != AgentWriteApprovalConsumption.Consumed),
            Is.All.EqualTo(AgentWriteApprovalConsumption.AlreadyConsumed));
    }

    [Test]
    public async Task TicketOfAnotherOperationIsRefusedAndBurnt()
    {
        var coordinator = new AgentWriteApprovalCoordinator(new ScriptedWritePrompt());
        var grant = await coordinator.RequestApprovalAsync(Proposal(), CancellationToken.None);

        var changed = coordinator.TryConsume(grant.Ticket, Proposal(payload: "{\"$set\":{\"v\":3}}"));

        Assert.That(changed, Is.EqualTo(AgentWriteApprovalConsumption.OperationMismatch));
        // A mismatching attempt burns the ticket: the approved operation can no longer use it either.
        Assert.That(coordinator.TryConsume(grant.Ticket, Proposal()),
            Is.EqualTo(AgentWriteApprovalConsumption.AlreadyConsumed));
    }

    [Test]
    public async Task ChangedGenerationChangesTheOperationHash()
    {
        var coordinator = new AgentWriteApprovalCoordinator(new ScriptedWritePrompt());
        var grant = await coordinator.RequestApprovalAsync(Proposal(), CancellationToken.None);

        Assert.That(Proposal(generation: Guid.NewGuid()).OperationHash, Is.Not.EqualTo(Proposal().OperationHash));
        Assert.That(coordinator.TryConsume(grant.Ticket, Proposal(generation: Guid.NewGuid())),
            Is.EqualTo(AgentWriteApprovalConsumption.OperationMismatch));
    }

    [Test]
    public async Task TicketOfAnotherPrincipalIsRefused()
    {
        var coordinator = new AgentWriteApprovalCoordinator(new ScriptedWritePrompt());
        var grant = await coordinator.RequestApprovalAsync(Proposal(), CancellationToken.None);

        Assert.That(coordinator.TryConsume(grant.Ticket, Proposal(principalId: OtherPrincipalId)),
            Is.EqualTo(AgentWriteApprovalConsumption.PrincipalMismatch));
    }

    [TestCase(true, false, false)]
    [TestCase(false, true, false)]
    [TestCase(false, false, true)]
    public async Task TicketOfAnotherSessionTurnOrCallIsRefused(bool otherSession, bool otherTurn, bool otherCall)
    {
        var coordinator = new AgentWriteApprovalCoordinator(new ScriptedWritePrompt());
        var grant = await coordinator.RequestApprovalAsync(Proposal(), CancellationToken.None);

        var result = coordinator.TryConsume(grant.Ticket, Proposal(
            sessionId: otherSession ? Guid.NewGuid() : null, turnId: otherTurn ? Guid.NewGuid() : null,
            invocationId: otherCall ? Guid.NewGuid() : null));

        Assert.That(result, Is.EqualTo(AgentWriteApprovalConsumption.InvocationMismatch));
    }

    [Test]
    public async Task ForgedTicketWithKnownApprovalIdIsUnknown()
    {
        var coordinator = new AgentWriteApprovalCoordinator(new ScriptedWritePrompt());
        var proposal = Proposal();
        var grant = await coordinator.RequestApprovalAsync(proposal, CancellationToken.None);

        var forged = new AgentWriteApprovalTicket(proposal.ApprovalId, Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(coordinator.TryConsume(forged, proposal), Is.EqualTo(AgentWriteApprovalConsumption.Unknown));
            Assert.That(coordinator.TryConsume(null, proposal), Is.EqualTo(AgentWriteApprovalConsumption.Unknown));
            Assert.That(coordinator.TryConsume(grant.Ticket, proposal), Is.EqualTo(AgentWriteApprovalConsumption.Consumed));
        });
    }

    [Test]
    public async Task TicketExpiresOnTheMonotonicClock()
    {
        var time = new ManualWriteTimeProvider();
        var coordinator = new AgentWriteApprovalCoordinator(new ScriptedWritePrompt(), time,
            ticketLifetime: TimeSpan.FromSeconds(10));
        var grant = await coordinator.RequestApprovalAsync(Proposal(), CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(11));

        Assert.That(coordinator.TryConsume(grant.Ticket, Proposal()), Is.EqualTo(AgentWriteApprovalConsumption.Expired));
        Assert.That(coordinator.TryConsume(grant.Ticket, Proposal()),
            Is.EqualTo(AgentWriteApprovalConsumption.AlreadyConsumed));
    }

    [Test]
    public async Task UnansweredApprovalExpiresWithoutTicket()
    {
        var prompt = new ScriptedWritePrompt
        {
            Handler = static async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return AgentApprovalOutcome.Granted;
            }
        };
        var coordinator = new AgentWriteApprovalCoordinator(prompt, approvalTimeout: TimeSpan.FromMilliseconds(100));

        var grant = await coordinator.RequestApprovalAsync(Proposal(), CancellationToken.None);

        Assert.That(grant.Verdict, Is.EqualTo(AgentWriteApprovalVerdict.Expired));
        Assert.That(grant.Ticket, Is.Null);
    }

    [Test]
    public async Task LateGrantAfterTheWindowIsExpired()
    {
        var time = new ManualWriteTimeProvider();
        var prompt = new ScriptedWritePrompt
        {
            Handler = (_, _) =>
            {
                time.Advance(TimeSpan.FromSeconds(121));
                return Task.FromResult<AgentApprovalOutcome?>(AgentApprovalOutcome.Granted);
            }
        };
        var coordinator = new AgentWriteApprovalCoordinator(prompt, time);

        var grant = await coordinator.RequestApprovalAsync(Proposal(), CancellationToken.None);

        Assert.That(grant.Verdict, Is.EqualTo(AgentWriteApprovalVerdict.Expired));
    }

    [Test]
    public async Task CancelledRejectedFailingAndAbsentPromptsNeverIssueTickets()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var waiting = new ScriptedWritePrompt
        {
            Handler = static async (_, token) =>
            {
                await Task.Delay(Timeout.Infinite, token);
                return AgentApprovalOutcome.Granted;
            }
        };
        var rejecting = new ScriptedWritePrompt
        {
            Handler = static (_, _) => Task.FromResult<AgentApprovalOutcome?>(AgentApprovalOutcome.Denied)
        };
        var failing = new ScriptedWritePrompt { Handler = static (_, _) => throw new InvalidOperationException("ui") };

        var results = new[]
        {
            await new AgentWriteApprovalCoordinator(waiting).RequestApprovalAsync(Proposal(), cancelled.Token),
            await new AgentWriteApprovalCoordinator(rejecting).RequestApprovalAsync(Proposal(), CancellationToken.None),
            await new AgentWriteApprovalCoordinator(failing).RequestApprovalAsync(Proposal(), CancellationToken.None),
            await new AgentWriteApprovalCoordinator(new FailClosedAgentWriteApprovalPrompt())
                .RequestApprovalAsync(Proposal(), CancellationToken.None)
        };

        Assert.That(results.Select(result => result.Verdict), Is.EqualTo(new[]
        {
            AgentWriteApprovalVerdict.Cancelled, AgentWriteApprovalVerdict.Rejected,
            AgentWriteApprovalVerdict.Unavailable, AgentWriteApprovalVerdict.Unavailable
        }));
        Assert.That(results.Select(result => result.Ticket), Is.All.Null);
    }

    [Test]
    public async Task ExternalPrincipalNeverReachesTheHumanPrompt()
    {
        var prompt = new ScriptedWritePrompt();
        var coordinator = new AgentWriteApprovalCoordinator(prompt);

        var grant = await coordinator.RequestApprovalAsync(Proposal(origin: AgentPrincipalOrigin.External),
            CancellationToken.None);

        Assert.That(grant.Verdict, Is.EqualTo(AgentWriteApprovalVerdict.Unavailable));
        Assert.That(prompt.Prompts, Is.Empty);
    }

    [Test]
    public async Task DuplicateApprovalIdCannotReopenAnApproval()
    {
        var coordinator = new AgentWriteApprovalCoordinator(new ScriptedWritePrompt());
        var first = await coordinator.RequestApprovalAsync(Proposal(), CancellationToken.None);

        var second = await coordinator.RequestApprovalAsync(Proposal(), CancellationToken.None);

        Assert.That(first.Verdict, Is.EqualTo(AgentWriteApprovalVerdict.Granted));
        Assert.That(second.Verdict, Is.EqualTo(AgentWriteApprovalVerdict.Unavailable));
    }

    [Test]
    public async Task DetailsAndRuntimeValidationOnlyCoverThePendingApprovalOfItsSessionAndTurn()
    {
        var release = new TaskCompletionSource<AgentApprovalOutcome?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompt = new ScriptedWritePrompt { Handler = (_, _) => release.Task };
        var coordinator = new AgentWriteApprovalCoordinator(prompt);
        var authority = new AgentWriteApprovalInteractionAuthority(coordinator, new FailClosedAgentInteractionAuthority());
        var proposal = Proposal();
        var session = new AgentSessionId(proposal.SessionId.ToString("N"));
        var turn = new AgentTurnId(proposal.TurnId.ToString("N"));
        var approval = new AgentApprovalId(proposal.ApprovalId.ToString("N"));

        var pending = coordinator.RequestApprovalAsync(proposal, CancellationToken.None);
        await WaitUntilAsync(() => prompt.Prompts.Count == 1);
        var details = await coordinator.DescribeAsync(session, turn, approval, CancellationToken.None);
        var otherTurn = await coordinator.DescribeAsync(session, AgentTurnId.New(), approval, CancellationToken.None);
        var validRequest = await authority.ValidateApprovalRequestAsync(session, turn, approval, CancellationToken.None);
        var foreignRequest = await authority.ValidateApprovalRequestAsync(session, turn, AgentApprovalId.New(),
            CancellationToken.None);
        var validDecision = await authority.ValidateApprovalDecisionAsync(
            new AgentApprovalDecision(session, turn, approval, AgentApprovalOutcome.Granted), CancellationToken.None);
        var foreignDecision = await authority.ValidateApprovalDecisionAsync(
            new AgentApprovalDecision(AgentSessionId.New(), turn, approval, AgentApprovalOutcome.Granted),
            CancellationToken.None);
        release.SetResult(AgentApprovalOutcome.Granted);
        await pending;
        var afterDecision = await coordinator.DescribeAsync(session, turn, approval, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(details, Is.Not.Null);
            Assert.That(details!.Target, Is.EqualTo("_id 1"));
            Assert.That(details.Change, Is.EqualTo("{\"$set\":{\"v\":2}}"));
            Assert.That(details.Risk, Is.EqualTo(AgentToolRisk.Write));
            Assert.That(prompt.Prompts[0].BeforeEjson, Is.EqualTo("{\"_id\":1,\"v\":1}"));
            Assert.That(otherTurn, Is.Null);
            // P7-L10-WIRE review L3: a provider echoing the pending coordinator ID is not a trusted approval request.
            Assert.That(validRequest, Is.False);
            Assert.That(validDecision, Is.True, "Only the human decision's identity is recognized for coordinator IDs.");
            Assert.That(foreignRequest, Is.False);
            Assert.That(foreignDecision, Is.False);
            Assert.That(afterDecision, Is.Null);
        });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Condição não atingida.");
            await Task.Delay(5);
        }
    }
}
