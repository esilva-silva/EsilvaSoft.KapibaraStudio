using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ActivityRefreshBudgetTests
{
    [Test]
    public void LimitsRepeatedReadsPerProfileButAllowsLaterRefreshAndOtherProfile()
    {
        var budget = new ActivityRefreshBudget();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var started = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(budget.TryBegin(first, started, out _), Is.True);
            Assert.That(budget.TryBegin(first, started.AddSeconds(1), out var wait), Is.False);
            Assert.That(wait, Is.EqualTo(TimeSpan.FromSeconds(9)));
            Assert.That(budget.TryBegin(second, started.AddSeconds(1), out _), Is.True);
            Assert.That(budget.TryBegin(first, started.AddSeconds(10), out _), Is.True);
        });
    }

    [Test]
    public void ConcurrentRequestsReserveOnlyOneSlot()
    {
        var budget = new ActivityRefreshBudget();
        var profile = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var accepted = Enumerable.Range(0, 20).AsParallel()
            .Count(index => budget.TryBegin(profile, now, out _));

        Assert.That(accepted, Is.EqualTo(1));
    }
}
