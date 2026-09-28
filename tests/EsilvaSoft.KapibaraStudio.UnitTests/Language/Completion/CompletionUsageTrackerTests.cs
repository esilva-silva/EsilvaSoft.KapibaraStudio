using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Completion;

[TestFixture]
public sealed class CompletionUsageTrackerTests
{
    [Test]
    public void AcceptedUsageIsScopedAndDecaysWithTheConfiguredTimeConstant()
    {
        var clock = new FakeTimeProvider(new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        var tracker = new CompletionUsageTracker(clock, TimeSpan.FromMinutes(30));
        var key = Key("Nome");

        tracker.RecordAccepted(key);
        var immediate = tracker.GetUsage(key);
        clock.Advance(TimeSpan.FromMinutes(30));

        Assert.Multiple(() =>
        {
            Assert.That(immediate, Is.EqualTo(1 - Math.Exp(-1)).Within(1e-12));
            Assert.That(tracker.GetUsage(key), Is.EqualTo(1 - Math.Exp(-Math.Exp(-1))).Within(1e-12));
            Assert.That(tracker.GetUsage(Key("Email")), Is.Zero);
        });
    }

    [Test]
    public void PromptUndoRecordsOneNegativeSignal()
    {
        var clock = new FakeTimeProvider(new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        var tracker = new CompletionUsageTracker(clock);
        var key = Key("Nome");

        tracker.RecordAccepted(key);
        clock.Advance(TimeSpan.FromSeconds(2));
        var undone = tracker.RecordUndone(key);

        Assert.Multiple(() =>
        {
            Assert.That(undone, Is.True);
            Assert.That(tracker.GetUsage(key), Is.LessThan(0));
            Assert.That(tracker.RecordUndone(key), Is.False, "an acceptance may only be undone once");
        });
    }

    [Test]
    public void LateUndoDoesNotChangeUsage()
    {
        var clock = new FakeTimeProvider(new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        var tracker = new CompletionUsageTracker(clock, undoWindow: TimeSpan.FromSeconds(5));
        var key = Key("Nome");
        tracker.RecordAccepted(key);
        clock.Advance(TimeSpan.FromSeconds(6));
        var before = tracker.GetUsage(key);

        Assert.Multiple(() =>
        {
            Assert.That(tracker.RecordUndone(key), Is.False);
            Assert.That(tracker.GetUsage(key), Is.EqualTo(before).Within(1e-12));
        });
    }

    [Test]
    public void KeyRejectsBlankIdentityParts()
    {
        Assert.That(() => new CompletionUsageKey("connection", "db", "collection", "shape", " "), Throws.ArgumentException);
    }

    private static CompletionUsageKey Key(string symbol) => new("profile-1", "catalogo", "clientes", "FilterKey", symbol);

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan value) => now = now.Add(value);
    }
}
