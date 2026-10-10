using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class LatencyStatisticsTests
{
    private static readonly int[] KnownLatencyVector = [10, 1, 9, 2, 8, 3, 7, 4, 6, 5];

    [Test]
    public void UsesNearestRankForKnownLatencyVector()
    {
        // Unsorted values: nearest-rank p50=5, p95=10, p99=10.
        var stats = LatencyStatistics.Aggregate(KnownLatencyVector
            .Select(milliseconds => new LatencySample(LatencySampleDisposition.Valid,
                TimeSpan.FromMilliseconds(milliseconds), TimeSpan.FromMilliseconds(milliseconds * 2))));

        Assert.Multiple(() =>
        {
            Assert.That(stats.Observed.SampleCount, Is.EqualTo(10));
            Assert.That(stats.Observed.Minimum, Is.EqualTo(TimeSpan.FromMilliseconds(1)));
            Assert.That(stats.Observed.Mean, Is.EqualTo(TimeSpan.FromMilliseconds(5.5)));
            Assert.That(stats.Observed.P50, Is.EqualTo(TimeSpan.FromMilliseconds(5)));
            Assert.That(stats.Observed.P95, Is.EqualTo(TimeSpan.FromMilliseconds(10)));
            Assert.That(stats.Observed.P99, Is.EqualTo(TimeSpan.FromMilliseconds(10)));
            Assert.That(stats.Observed.Maximum, Is.EqualTo(TimeSpan.FromMilliseconds(10)));
            Assert.That(stats.Runtime.P50, Is.EqualTo(TimeSpan.FromMilliseconds(10)));
            Assert.That(stats.Runtime.P95, Is.EqualTo(TimeSpan.FromMilliseconds(20)));
        });
    }

    [Test]
    public void CountsEveryDispositionAndKeepsMissingLatenciesOutOfZeroStatistics()
    {
        var stats = LatencyStatistics.Aggregate([
            new(LatencySampleDisposition.Valid, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(2)),
            new(LatencySampleDisposition.Null, null, TimeSpan.FromMilliseconds(3)),
            new(LatencySampleDisposition.Refused),
            new(LatencySampleDisposition.Failed, TimeSpan.FromMilliseconds(30)),
            new(LatencySampleDisposition.Truncated),
            new(LatencySampleDisposition.Ignored)
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(stats.TotalSamples, Is.EqualTo(6));
            Assert.That(stats.ValidSamples, Is.EqualTo(1));
            Assert.That(stats.NullSamples, Is.EqualTo(1));
            Assert.That(stats.RefusedSamples, Is.EqualTo(1));
            Assert.That(stats.FailedSamples, Is.EqualTo(1));
            Assert.That(stats.TruncatedSamples, Is.EqualTo(1));
            Assert.That(stats.IgnoredSamples, Is.EqualTo(1));
            Assert.That(stats.Observed.SampleCount, Is.EqualTo(2));
            Assert.That(stats.Observed.MissingCount, Is.EqualTo(4));
            Assert.That(stats.Observed.Minimum, Is.EqualTo(TimeSpan.FromMilliseconds(10)));
            Assert.That(stats.Runtime.SampleCount, Is.EqualTo(2));
            Assert.That(stats.Runtime.MissingCount, Is.EqualTo(4));
            Assert.That(stats.Runtime.Minimum, Is.EqualTo(TimeSpan.FromMilliseconds(2)));
        });
    }

    [Test]
    public void EmptyAndFullyMissingInputsHaveNoLatencyStatistics()
    {
        var empty = LatencyStatistics.Aggregate([]);
        var missing = LatencyStatistics.Aggregate([new(LatencySampleDisposition.Ignored)]);

        Assert.Multiple(() =>
        {
            Assert.That(empty.TotalSamples, Is.Zero);
            Assert.That(empty.Observed.SampleCount, Is.Zero);
            Assert.That(empty.Observed.MissingCount, Is.Zero);
            Assert.That(empty.Observed.P50, Is.Null);
            Assert.That(missing.Observed.SampleCount, Is.Zero);
            Assert.That(missing.Observed.MissingCount, Is.EqualTo(1));
            Assert.That(missing.Observed.Minimum, Is.Null);
            Assert.That(missing.Observed.Mean, Is.Null);
            Assert.That(missing.Runtime.P99, Is.Null);
        });
    }

    [Test]
    public void RejectsNegativeLatencyInsteadOfSilentlyIncludingIt()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LatencyStatistics.Aggregate([
            new LatencySample(LatencySampleDisposition.Valid, TimeSpan.FromMilliseconds(-1))
        ]));
    }
}
