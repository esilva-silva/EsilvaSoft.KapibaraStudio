using EsilvaSoft.SlopStudio.Application.Agents;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class AgentToolInvocationQuotaTests
{
    [Test]
    public void SessionLimitReleasesACompletedLease()
    {
        var quota = new AgentToolInvocationQuota();
        var session = Guid.NewGuid();
        using var first = quota.TryEnter(session, Guid.NewGuid(), Guid.NewGuid());
        using var second = quota.TryEnter(session, Guid.NewGuid(), Guid.NewGuid());
        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.Not.Null);
        Assert.That(quota.TryEnter(session, Guid.NewGuid(), Guid.NewGuid()), Is.Null);

        first!.Dispose();
        using var replacement = quota.TryEnter(session, Guid.NewGuid(), Guid.NewGuid());
        Assert.That(replacement, Is.Not.Null);
    }

    [Test]
    public void ConnectionAndGlobalLimitsApplyAcrossSessions()
    {
        var quota = new AgentToolInvocationQuota();
        var sharedConnection = Guid.NewGuid();
        var leases = Enumerable.Range(0, 4).Select(_ =>
            quota.TryEnter(Guid.NewGuid(), Guid.NewGuid(), sharedConnection)).ToArray();
        try
        {
            Assert.That(leases, Has.All.Not.Null);
            Assert.That(quota.TryEnter(Guid.NewGuid(), Guid.NewGuid(), sharedConnection), Is.Null);

            var otherConnections = Enumerable.Range(0, 4).Select(_ =>
                quota.TryEnter(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid())).ToArray();
            try
            {
                Assert.That(otherConnections, Has.All.Not.Null);
                Assert.That(quota.TryEnter(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), Is.Null);
                leases[0]!.Dispose();
                using var replacement = quota.TryEnter(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
                Assert.That(replacement, Is.Not.Null);
            }
            finally
            {
                foreach (var lease in otherConnections) lease?.Dispose();
            }
        }
        finally
        {
            foreach (var lease in leases) lease?.Dispose();
        }
    }

    [Test]
    public void TwentyCallsPerTurnStayConsumedAfterLeaseRelease()
    {
        var quota = new AgentToolInvocationQuota();
        var session = Guid.NewGuid();
        var turn = Guid.NewGuid();
        for (var i = 0; i < 20; i++)
        {
            using var lease = quota.TryEnter(session, turn, null);
            Assert.That(lease, Is.Not.Null, $"Call {i + 1} should be admitted.");
        }
        Assert.That(quota.TryEnter(session, turn, null), Is.Null);
        using var nextTurn = quota.TryEnter(session, Guid.NewGuid(), null);
        Assert.That(nextTurn, Is.Not.Null);
    }

    [Test]
    public void ConcurrencySaturationIsBusyButAnExhaustedTurnIsNot()
    {
        var quota = new AgentToolInvocationQuota();
        var session = Guid.NewGuid();
        var turn = Guid.NewGuid();
        using var first = quota.TryEnter(session, turn, null, trackTurn: true, out _);
        using var second = quota.TryEnter(session, turn, null, trackTurn: true, out _);
        var saturated = quota.TryEnter(session, turn, null, trackTurn: true, out var saturatedBusy);

        var exhaustedTurn = Guid.NewGuid();
        var otherSession = Guid.NewGuid();
        for (var i = 0; i < 20; i++)
        {
            using var lease = quota.TryEnter(otherSession, exhaustedTurn, null, trackTurn: true, out _);
            Assert.That(lease, Is.Not.Null);
        }
        var exhausted = quota.TryEnter(otherSession, exhaustedTurn, null, trackTurn: true, out var exhaustedBusy);

        Assert.Multiple(() =>
        {
            Assert.That(saturated, Is.Null);
            Assert.That(saturatedBusy, Is.True, "Sessão saturada é transitória: Busy.");
            Assert.That(exhausted, Is.Null);
            Assert.That(exhaustedBusy, Is.False, "Orçamento do turno esgotado não melhora com nova tentativa.");
        });
    }

    [Test]
    public void TurnlessCallsNeitherSpendATurnBudgetNorFillTheSharedTurnTable()
    {
        var quota = new AgentToolInvocationQuota();
        var externalSession = Guid.NewGuid();
        // More fresh per-call correlation ids than the table can track: none of them may be retained.
        for (var i = 0; i < 4_200; i++)
        {
            using var lease = quota.TryEnter(externalSession, Guid.NewGuid(), null, trackTurn: false, out var busy);
            Assert.That(lease, Is.Not.Null, $"Chamada externa {i + 1} deveria ser admitida.");
            Assert.That(busy, Is.False);
        }
        var sameCorrelation = Guid.NewGuid();
        for (var i = 0; i < 25; i++)
        {
            using var lease = quota.TryEnter(externalSession, sameCorrelation, null, trackTurn: false, out _);
            Assert.That(lease, Is.Not.Null, "Sem orçamento por turno para o ingress externo.");
        }

        using var chatTurn = quota.TryEnter(Guid.NewGuid(), Guid.NewGuid(), null);
        Assert.That(chatTurn, Is.Not.Null, "Um novo turno do chat continua admitido.");
    }

    [Test]
    public void TurnlessCallsStillShareSessionAndGlobalSlots()
    {
        var quota = new AgentToolInvocationQuota();
        var channelSession = Guid.NewGuid();
        using var first = quota.TryEnter(channelSession, Guid.NewGuid(), null, trackTurn: false, out _);
        using var second = quota.TryEnter(channelSession, Guid.NewGuid(), null, trackTurn: false, out _);
        var third = quota.TryEnter(channelSession, Guid.NewGuid(), null, trackTurn: false, out var busy);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            Assert.That(third, Is.Null);
            Assert.That(busy, Is.True);
        });
    }

    [Test]
    public void CancelledSourceKeepsItsSlotUntilItsTaskCompletes()
    {
        var quota = new AgentToolInvocationQuota();
        var session = Guid.NewGuid();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledCall = quota.TryEnter(session, Guid.NewGuid(), null);
        Assert.That(cancelledCall, Is.Not.Null);
        cancelledCall!.HoldUntil(pending.Task);
        cancelledCall.Dispose();

        using var second = quota.TryEnter(session, Guid.NewGuid(), null);
        Assert.That(second, Is.Not.Null);
        Assert.That(quota.TryEnter(session, Guid.NewGuid(), null), Is.Null);
        pending.SetResult();
        Assert.That(SpinWait.SpinUntil(() =>
        {
            using var recovered = quota.TryEnter(session, Guid.NewGuid(), null);
            return recovered is not null;
        }, TimeSpan.FromSeconds(2)), Is.True);
    }
}
