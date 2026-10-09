using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AdministrationMetricHistoryTests
{
    private static readonly string[] ExpectedServerMetricNames =
        ["uptime", "connections.current", "connections.available"];

    [Test]
    public void KeepsOnlySupportedNumbersWithoutRawResponseOrSecrets()
    {
        var history = new AdministrationMetricHistory();
        var profile = Guid.NewGuid();
        var generation = Guid.NewGuid();
        var instant = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

        history.RecordCommand(profile, generation, AdministrationMetricSource.ServerStatus,
            """{"uptime":{"$numberLong":"10"},"connections":{"current":3,"available":40},"host":"private-host","note":"secret"}""",
            observedAt: instant);

        var sample = history.GetSnapshot(profile, generation).Single();
        Assert.Multiple(() =>
        {
            Assert.That(sample.ObservedAt, Is.EqualTo(instant));
            Assert.That(sample.Source, Is.EqualTo(AdministrationMetricSource.ServerStatus));
            Assert.That(sample.Metrics.Select(metric => metric.Name),
                Is.EquivalentTo(ExpectedServerMetricNames));
            Assert.That(sample.Metrics.Single(metric => metric.Name == "uptime").Value, Is.EqualTo(10));
            Assert.That(sample.ToString(), Does.Not.Contain("private-host").And.Not.Contain("secret"));
        });
    }

    [Test]
    public void EvictsOldSamplesAndIsolatesConnectionGeneration()
    {
        var history = new AdministrationMetricHistory();
        var profile = Guid.NewGuid();
        var oldGeneration = Guid.NewGuid();
        var newGeneration = Guid.NewGuid();

        history.RecordCount(profile, oldGeneration, 1, false, false, "db", "items");
        for (var count = 2; count <= AdministrationMetricHistory.Capacity + 1; count++)
            history.RecordCount(profile, newGeneration, count, true, false, "db", "items");

        Assert.Multiple(() =>
        {
            Assert.That(history.GetSnapshot(profile, oldGeneration), Is.Empty);
            Assert.That(history.GetSnapshot(profile, newGeneration), Has.Count.EqualTo(AdministrationMetricHistory.Capacity));
            Assert.That(history.GetSnapshot(Guid.NewGuid(), newGeneration), Is.Empty);
            Assert.That(history.GetSnapshot(profile, newGeneration)[0].Metrics[0].Value,
                Is.EqualTo(AdministrationMetricHistory.Capacity + 1));
        });
    }

    [Test]
    public void DistinguishesExactFilteredAndEstimatedCounts()
    {
        var history = new AdministrationMetricHistory();
        var profile = Guid.NewGuid();

        history.RecordCount(profile, null, 10, false, false, "db", "items");
        history.RecordCount(profile, null, 4, false, true, "db", "items");
        history.RecordCount(profile, null, 11, true, false, "db", "items");

        Assert.That(history.GetSnapshot(profile, null).Select(sample => sample.Source), Is.EqualTo(new[]
        {
            AdministrationMetricSource.EstimatedCount,
            AdministrationMetricSource.FilteredExactCount,
            AdministrationMetricSource.ExactCount
        }));
    }

    [Test]
    public void InvalidOrOversizedResponseDoesNotReplaceEarlierSamples()
    {
        var history = new AdministrationMetricHistory();
        var profile = Guid.NewGuid();
        history.RecordCommand(profile, null, AdministrationMetricSource.CollectionStats, "{\"count\":7}", "db", "items");

        history.RecordCommand(profile, null, AdministrationMetricSource.CollectionStats, "{invalid", "db", "items");
        history.RecordCommand(profile, null, AdministrationMetricSource.CollectionStats,
            new string(' ', AdministrationMetricHistory.MaximumResponseCharacters + 1), "db", "items");

        Assert.That(history.GetSnapshot(profile, null), Has.Count.EqualTo(1));
    }
}
