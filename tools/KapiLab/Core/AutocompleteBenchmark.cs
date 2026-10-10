using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal sealed record AutocompleteBenchmarkInput(string Id, string Type, AutocompleteRequest Request);

internal sealed record AutocompleteBenchmarkOutcome(
    LatencySampleDisposition Disposition,
    TimeSpan? RuntimeTtft,
    string? Provider,
    string? Device,
    bool UsedFallback,
    string? Reason = null)
{
    public double? RuntimeTokensPerSecond { get; init; }

    internal static (LatencySampleDisposition Disposition, string? Reason) ClassifyResult(
        bool hasCompletion, bool isComplete, LocalModelState state)
    {
        if (hasCompletion && !isComplete) return (LatencySampleDisposition.Truncated, "incomplete");
        if (hasCompletion) return (LatencySampleDisposition.Valid, null);
        return (state switch
        {
            LocalModelState.Failed or LocalModelState.Unsupported or LocalModelState.Invalid => LatencySampleDisposition.Failed,
            LocalModelState.NotLoaded or LocalModelState.NotInstalled or LocalModelState.Loading => LatencySampleDisposition.Refused,
            _ => LatencySampleDisposition.Null,
        }, null);
    }
}

internal sealed record AutocompleteBenchmarkRecord(
    string Schema,
    string Id,
    string Type,
    int Iteration,
    string Outcome,
    double ObservedLatencyMilliseconds,
    double? RuntimeTtftMilliseconds,
    double? RuntimeTokensPerSecond,
    string? Provider,
    string? Device,
    bool UsedFallback,
    string? Reason);

internal sealed record AutocompleteBenchmarkReport(
    string Schema,
    int WarmupRequested,
    int WarmupExecuted,
    int Iterations,
    bool Complete,
    bool Cancelled,
    int MeasuredSamples,
    LatencyStatistics Statistics,
    ThroughputDistribution RuntimeTokensPerSecond,
    IReadOnlyList<AutocompleteBenchmarkRecord> Records);

/// <summary>Runtime-reported tokens per second; missing measurements are counted, never replaced with zero.</summary>
internal sealed record ThroughputDistribution(int SampleCount, int MissingCount, double? Minimum, double? Mean,
    double? P50, double? P95, double? P99, double? Maximum);

internal static class AutocompleteBenchmark
{
    internal const int MaximumWarmup = 100;
    internal const int MaximumIterations = 100;
    internal const int MaximumMeasuredSamples = 20_000;

    public static async Task<AutocompleteBenchmarkReport> RunAsync(
        IReadOnlyList<AutocompleteBenchmarkInput> inputs,
        int warmup,
        int iterations,
        Func<AutocompleteBenchmarkInput, CancellationToken, Task<AutocompleteBenchmarkOutcome>> infer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(infer);
        if (inputs.Count == 0) throw new ArgumentException("At least one input is required.", nameof(inputs));
        if (warmup is < 0 or > MaximumWarmup) throw new ArgumentOutOfRangeException(nameof(warmup));
        if (iterations is < 1 or > MaximumIterations) throw new ArgumentOutOfRangeException(nameof(iterations));
        var total = checked(inputs.Count * iterations);
        if (total > MaximumMeasuredSamples) throw new ArgumentOutOfRangeException(nameof(iterations), "Measured sample limit exceeded.");

        var warmupExecuted = 0;
        var cancelled = false;
        for (var warmupIndex = 0; warmupIndex < warmup; warmupIndex++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            warmupExecuted++;
            try
            {
                await infer(inputs[0], cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }
            catch (Exception) { /* Warmup is excluded from measured results; the first measured call records failures. */ }
        }

        var records = new List<AutocompleteBenchmarkRecord>(total);
        var samples = new List<LatencySample>(total);
        for (var iteration = 1; iteration <= iterations && !cancelled; iteration++)
        {
            foreach (var input in inputs)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                var stopwatch = Stopwatch.StartNew();
                AutocompleteBenchmarkOutcome outcome;
                try
                {
                    outcome = await infer(input, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception)
                {
                    outcome = new(LatencySampleDisposition.Failed, null, null, null, false);
                }
                stopwatch.Stop();

                var elapsed = stopwatch.Elapsed;
                samples.Add(new(outcome.Disposition, elapsed, outcome.RuntimeTtft));
                records.Add(new("kapilab-autocomplete-benchmark-sample-v3", input.Id, input.Type, iteration,
                    outcome.Disposition.ToString().ToLowerInvariant(), elapsed.TotalMilliseconds,
                    outcome.RuntimeTtft?.TotalMilliseconds, outcome.RuntimeTokensPerSecond,
                    outcome.Provider, outcome.Device, outcome.UsedFallback, outcome.Reason));
            }
        }

        var complete = !cancelled && records.Count == total;
        return new("kapilab-autocomplete-benchmark-v5", warmup, warmupExecuted, iterations, complete, cancelled, records.Count,
            LatencyStatistics.Aggregate(samples), AggregateThroughput(records), records);
    }

    private static ThroughputDistribution AggregateThroughput(List<AutocompleteBenchmarkRecord> records)
    {
        var values = records.Select(record => record.RuntimeTokensPerSecond).Where(value => value.HasValue)
            .Select(value => value!.Value).Order().ToArray();
        if (values.Any(value => !double.IsFinite(value) || value < 0))
            throw new InvalidDataException("Runtime tokens/sec precisa ser finito e não negativo.");
        if (values.Length == 0)
            return new(0, records.Count, null, null, null, null, null, null);

        return new(values.Length, records.Count - values.Length, values[0], values.Average(),
            Percentile(values, 50), Percentile(values, 95), Percentile(values, 99), values[^1]);
    }

    private static double Percentile(double[] sortedValues, int percentile)
    {
        // Nearest-rank: rank = ceil(p / 100 * n), using one-based ranks as for LatencyStatistics.
        var rank = (int)Math.Ceiling(percentile / 100d * sortedValues.Length);
        return sortedValues[rank - 1];
    }
}
