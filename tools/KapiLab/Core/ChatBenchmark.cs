using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal sealed record ChatBenchmarkOutcome(LatencySampleDisposition Disposition, TimeSpan? RuntimeTimeToFirstToken,
    TimeSpan? RuntimeElapsed, int? GeneratedTokens, string? Provider, bool? UsedCpuFallback, string? Reason = null);

internal sealed record ChatBenchmarkSample(int RecordIndex, int Iteration, LatencySampleDisposition Disposition,
    TimeSpan ObservedWall, TimeSpan? RuntimeTimeToFirstToken, TimeSpan? RuntimeElapsed,
    int? GeneratedTokens, string? Provider, bool? UsedCpuFallback, string? Reason);

internal sealed record ChatBenchmarkReport(int WarmupRequested, int WarmupExecuted, int Iterations,
    IReadOnlyList<ChatBenchmarkSample> Samples, LatencyStatistics Statistics, bool Complete, bool Cancelled,
    string? StopReason);

/// <summary>Warmup never enters the measured sample list or its distributions.</summary>
internal static class ChatBenchmark
{
    internal const int MaximumWarmup = 5;
    internal const int MaximumIterations = 10;
    internal const int MaximumMeasuredSamples = 500;

    internal static async Task<ChatBenchmarkReport> RunAsync(IReadOnlyList<ModelRunChatCommand.ChatInput> inputs,
        int warmup, int iterations, Func<ModelRunChatCommand.ChatInput, CancellationToken, Task<ChatBenchmarkOutcome>> infer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(infer);
        if (inputs.Count is < 1 or > ModelRunChatCommand.MaximumRecords || warmup is < 0 or > MaximumWarmup ||
            iterations is < 1 or > MaximumIterations || checked(inputs.Count * iterations) > MaximumMeasuredSamples)
            throw new ArgumentOutOfRangeException(nameof(inputs), "Limites do benchmark de chat inválidos.");

        var samples = new List<ChatBenchmarkSample>();
        var warmed = 0;
        try
        {
            while (warmed < warmup)
            {
                cancellationToken.ThrowIfCancellationRequested();
                warmed++;
                var outcome = await infer(inputs[0], cancellationToken).ConfigureAwait(false);
                if (outcome.Disposition is not (LatencySampleDisposition.Valid or LatencySampleDisposition.Truncated))
                    return Report(false, false, outcome.Reason ?? "warmup_unavailable");
            }
            for (var iteration = 0; iteration < iterations; iteration++)
            for (var index = 0; index < inputs.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var started = Stopwatch.GetTimestamp();
                var outcome = await infer(inputs[index], cancellationToken).ConfigureAwait(false);
                var observed = Stopwatch.GetElapsedTime(started);
                samples.Add(new(index, iteration, outcome.Disposition, observed,
                    outcome.RuntimeTimeToFirstToken, outcome.RuntimeElapsed, outcome.GeneratedTokens,
                    outcome.Provider, outcome.UsedCpuFallback, outcome.Reason));
                if (outcome.Reason == "privacy_output") return Report(false, false, "privacy_output");
            }
            return Report(true, false, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Report(false, true, "cancelled");
        }

        ChatBenchmarkReport Report(bool complete, bool cancelled, string? reason) =>
            new(warmup, warmed, iterations, samples,
                LatencyStatistics.Aggregate(samples.Select(static sample => new LatencySample(sample.Disposition,
                    sample.ObservedWall, sample.RuntimeTimeToFirstToken))), complete, cancelled, reason);
    }
}
