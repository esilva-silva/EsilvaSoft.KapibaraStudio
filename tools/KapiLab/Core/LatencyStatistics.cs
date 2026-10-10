namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Outcome assigned to one measured KapiLab operation.</summary>
internal enum LatencySampleDisposition
{
    Valid,
    Null,
    Refused,
    Failed,
    Truncated,
    Ignored
}

/// <param name="Disposition">How the operation completed.</param>
/// <param name="ObservedLatency">End-to-end latency visible to the caller, when observed.</param>
/// <param name="RuntimeLatency">Model/runtime execution latency, when reported by the runtime.</param>
internal sealed record LatencySample(
    LatencySampleDisposition Disposition,
    TimeSpan? ObservedLatency = null,
    TimeSpan? RuntimeLatency = null);

/// <summary>
/// Aggregated latencies. Statistics are null when there are no measurements; missing values are
/// counted separately and are never substituted with zero.
/// </summary>
internal sealed record LatencyDistribution(
    int SampleCount,
    int MissingCount,
    TimeSpan? Minimum,
    TimeSpan? Mean,
    TimeSpan? P50,
    TimeSpan? P95,
    TimeSpan? P99,
    TimeSpan? Maximum);

internal sealed record LatencyStatistics(
    int TotalSamples,
    int ValidSamples,
    int NullSamples,
    int RefusedSamples,
    int FailedSamples,
    int TruncatedSamples,
    int IgnoredSamples,
    LatencyDistribution Observed,
    LatencyDistribution Runtime)
{
    public static LatencyStatistics Aggregate(IEnumerable<LatencySample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);

        var items = samples.ToArray();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!Enum.IsDefined(item.Disposition))
            {
                throw new ArgumentOutOfRangeException(nameof(samples), item.Disposition, "Unknown sample disposition.");
            }

            ValidateLatency(item.ObservedLatency, nameof(item.ObservedLatency));
            ValidateLatency(item.RuntimeLatency, nameof(item.RuntimeLatency));
        }

        return new(
            items.Length,
            Count(LatencySampleDisposition.Valid),
            Count(LatencySampleDisposition.Null),
            Count(LatencySampleDisposition.Refused),
            Count(LatencySampleDisposition.Failed),
            Count(LatencySampleDisposition.Truncated),
            Count(LatencySampleDisposition.Ignored),
            CreateDistribution(items.Select(item => item.ObservedLatency), items.Length),
            CreateDistribution(items.Select(item => item.RuntimeLatency), items.Length));

        int Count(LatencySampleDisposition disposition) => items.Count(item => item.Disposition == disposition);
    }

    private static LatencyDistribution CreateDistribution(IEnumerable<TimeSpan?> values, int totalCount)
    {
        var ordered = values.Where(value => value.HasValue).Select(value => value!.Value.Ticks).Order().ToArray();
        var measuredCount = ordered.Length;
        var missingCount = totalCount - measuredCount;
        if (measuredCount == 0)
        {
            return new(0, missingCount, null, null, null, null, null, null);
        }

        var meanTicks = (long)Math.Round(ordered.Average(value => (double)value), MidpointRounding.AwayFromZero);
        return new(
            measuredCount,
            missingCount,
            TimeSpan.FromTicks(ordered[0]),
            TimeSpan.FromTicks(meanTicks),
            Percentile(ordered, 50),
            Percentile(ordered, 95),
            Percentile(ordered, 99),
            TimeSpan.FromTicks(ordered[^1]));
    }

    private static TimeSpan Percentile(long[] sortedTicks, int percentile)
    {
        // Nearest-rank definition: rank = ceil(p / 100 * n), with one-based ranks.
        var rank = (int)Math.Ceiling(percentile / 100d * sortedTicks.Length);
        return TimeSpan.FromTicks(sortedTicks[rank - 1]);
    }

    private static void ValidateLatency(TimeSpan? latency, string parameterName)
    {
        if (latency is { } value && value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Latency cannot be negative.");
        }
    }
}
