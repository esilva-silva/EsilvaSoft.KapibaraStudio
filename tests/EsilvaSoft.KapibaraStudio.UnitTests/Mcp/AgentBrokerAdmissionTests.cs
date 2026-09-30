using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Mcp;

/// <summary>Pure admission policy tests with a controlled clock and no broker, storage, or IPC.</summary>
[TestFixture, Category("Unit")]
public sealed class AgentBrokerAdmissionTests
{
    [Test]
    public void ChannelBudgetIsSharedAcrossConcurrentConnectionsAndRefillsOverTime()
    {
        var time = new ManualTime();
        var admission = new AgentBrokerCallAdmission(burst: 3, callsPerMinute: 60, maximumInFlightPerChannel: 2, time);
        var channel = Guid.NewGuid();
        var other = Guid.NewGuid();

        Assert.That(admission.TryAdmit(channel, out var first), Is.EqualTo(AgentBrokerAdmission.Admitted));
        Assert.That(admission.TryAdmit(channel, out var second), Is.EqualTo(AgentBrokerAdmission.Admitted));
        Assert.That(admission.TryAdmit(channel, out _), Is.EqualTo(AgentBrokerAdmission.Busy),
            "Terceira em voo do mesmo canal: Busy antes do registry.");
        first!();
        first();
        Assert.That(admission.TryAdmit(channel, out var third), Is.EqualTo(AgentBrokerAdmission.Admitted),
            "Busy não gasta ficha; liberação dupla é idempotente (só uma vaga foi devolvida).");
        Assert.That(admission.TryAdmit(channel, out _), Is.EqualTo(AgentBrokerAdmission.Busy));
        third!();
        Assert.That(admission.TryAdmit(channel, out _), Is.EqualTo(AgentBrokerAdmission.RateLimited),
            "Rajada de 3 esgotada.");
        Assert.That(admission.TryAdmit(other, out var otherLease), Is.EqualTo(AgentBrokerAdmission.Admitted),
            "Outro canal tem orçamento próprio.");

        second!();
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.That(admission.TryAdmit(channel, out var refilled), Is.EqualTo(AgentBrokerAdmission.Admitted),
            "Uma ficha por segundo com 60/min.");
        Assert.That(admission.TryAdmit(channel, out _), Is.EqualTo(AgentBrokerAdmission.RateLimited));
        refilled!();
        otherLease!();
    }

    [Test]
    public void OptionsRejectOutOfRangeCallBudgets()
    {
        var valid = new AgentBrokerOptions { WorkspaceId = Guid.NewGuid() };
        Assert.Multiple(() =>
        {
            Assert.That(() => (valid with { CallBurstPerChannel = 0 }).Validate(), Throws.ArgumentException);
            Assert.That(() => (valid with { CallBurstPerChannel = 1_001 }).Validate(), Throws.ArgumentException);
            Assert.That(() => (valid with { CallsPerMinutePerChannel = 0 }).Validate(), Throws.ArgumentException);
            Assert.That(() => (valid with { CallsPerMinutePerChannel = 6_001 }).Validate(), Throws.ArgumentException);
            Assert.That(valid.Validate, Throws.Nothing);
        });
    }

    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref _ticks);
        public void Advance(TimeSpan by) => Interlocked.Add(ref _ticks, by.Ticks);
    }
}
