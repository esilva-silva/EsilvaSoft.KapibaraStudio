using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class AutocompleteBenchmarkTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Test]
    public async Task WarmupCallsAreExcludedAndMeasuredOutcomesAreAggregated()
    {
        var input = new AutocompleteBenchmarkInput("case-1", "fim", new AutocompleteRequest("db.", ""));
        var calls = 0;
        var report = await AutocompleteBenchmark.RunAsync([input], warmup: 2, iterations: 3,
            (_, _) =>
            {
                calls++;
                AutocompleteBenchmarkOutcome outcome = calls switch
                {
                    1 or 2 => new(LatencySampleDisposition.Failed, null, "fixture", "CPU", false),
                    3 => new(LatencySampleDisposition.Valid, TimeSpan.FromMilliseconds(4), "test-provider", "CPU", false)
                    {
                        RuntimeTokensPerSecond = 37.5,
                    },
                    4 => new(LatencySampleDisposition.Null, null, "test-provider", "CPU", false),
                    _ => new AutocompleteBenchmarkOutcome(LatencySampleDisposition.Refused, null, "test-provider", "CPU", false)
                    {
                        RuntimeTokensPerSecond = 12.5,
                    },
                };
                return Task.FromResult(outcome);
            });

        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(5));
            Assert.That(report.WarmupRequested, Is.EqualTo(2));
            Assert.That(report.WarmupExecuted, Is.EqualTo(2));
            Assert.That(report.MeasuredSamples, Is.EqualTo(3));
            Assert.That(report.Statistics.TotalSamples, Is.EqualTo(3));
            Assert.That(report.Statistics.ValidSamples, Is.EqualTo(1));
            Assert.That(report.Statistics.NullSamples, Is.EqualTo(1));
            Assert.That(report.Statistics.RefusedSamples, Is.EqualTo(1));
            Assert.That(report.Statistics.FailedSamples, Is.Zero);
            Assert.That(report.Statistics.Runtime.SampleCount, Is.EqualTo(1));
            Assert.That(report.Statistics.Runtime.P50, Is.EqualTo(TimeSpan.FromMilliseconds(4)));
            Assert.That(report.Schema, Is.EqualTo("kapilab-autocomplete-benchmark-v5"));
            Assert.That(report.Records[0].Schema, Is.EqualTo("kapilab-autocomplete-benchmark-sample-v3"));
            Assert.That(report.Records[0].RuntimeTokensPerSecond, Is.EqualTo(37.5));
            Assert.That(report.Records[1].RuntimeTokensPerSecond, Is.Null);
            Assert.That(report.Records[0].Iteration, Is.EqualTo(1));
            Assert.That(report.Records[1].Iteration, Is.EqualTo(2));
            Assert.That(report.Records[2].Iteration, Is.EqualTo(3));
            Assert.That(report.RuntimeTokensPerSecond.SampleCount, Is.EqualTo(2));
            Assert.That(report.RuntimeTokensPerSecond.MissingCount, Is.EqualTo(1));
            Assert.That(report.RuntimeTokensPerSecond.Minimum, Is.EqualTo(12.5));
            Assert.That(report.RuntimeTokensPerSecond.Mean, Is.EqualTo(25));
            Assert.That(report.RuntimeTokensPerSecond.P50, Is.EqualTo(12.5));
            Assert.That(report.RuntimeTokensPerSecond.P95, Is.EqualTo(37.5));
            Assert.That(report.RuntimeTokensPerSecond.P99, Is.EqualTo(37.5));
            Assert.That(report.RuntimeTokensPerSecond.Maximum, Is.EqualTo(37.5));
        });

        var summaryJson = JsonSerializer.Serialize(new { runtimeTokensPerSecond = report.RuntimeTokensPerSecond },
            JsonOptions);
        using var summary = JsonDocument.Parse(summaryJson);
        Assert.Multiple(() =>
        {
            Assert.That(summary.RootElement.GetProperty("runtimeTokensPerSecond").GetProperty("sampleCount").GetInt32(), Is.EqualTo(2));
            Assert.That(summary.RootElement.GetProperty("runtimeTokensPerSecond").GetProperty("missingCount").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.RootElement.GetProperty("runtimeTokensPerSecond").GetProperty("mean").GetDouble(), Is.EqualTo(25));
        });
    }

    [Test]
    public async Task IncompleteGenerationIsCountedAsTruncatedAndKeepsItsReason()
    {
        var classification = AutocompleteBenchmarkOutcome.ClassifyResult(true, false, EsilvaSoft.KapibaraStudio.LocalAi.Core.LocalModelState.Ready);
        var input = new AutocompleteBenchmarkInput("case-incomplete", "fim", new AutocompleteRequest("db.", ""));
        var report = await AutocompleteBenchmark.RunAsync([input], warmup: 0, iterations: 1,
            (_, _) => Task.FromResult(new AutocompleteBenchmarkOutcome(classification.Disposition,
                TimeSpan.FromMilliseconds(3), "fixture", "CPU", false, classification.Reason)
            {
                RuntimeTokensPerSecond = 10,
            }));

        Assert.Multiple(() =>
        {
            Assert.That(report.Statistics.TruncatedSamples, Is.EqualTo(1));
            Assert.That(report.Statistics.ValidSamples, Is.Zero);
            Assert.That(report.Records, Has.Count.EqualTo(1));
            Assert.That(report.Records[0].Outcome, Is.EqualTo("truncated"));
            Assert.That(report.Records[0].Reason, Is.EqualTo("incomplete"));
            Assert.That(report.Records[0].RuntimeTtftMilliseconds, Is.EqualTo(3));
            Assert.That(report.RuntimeTokensPerSecond.SampleCount, Is.EqualTo(1));
            Assert.That(AutocompleteBenchmarkOutcome.ClassifyResult(true, true,
                EsilvaSoft.KapibaraStudio.LocalAi.Core.LocalModelState.Ready).Disposition, Is.EqualTo(LatencySampleDisposition.Valid));
        });
    }

    [Test]
    public async Task ExceptionsAreCountedAsFailuresAndMissingRuntimeMetricsStayAbsent()
    {
        var input = new AutocompleteBenchmarkInput("case-1", "fim", new AutocompleteRequest("db.", ""));
        var call = 0;
        var report = await AutocompleteBenchmark.RunAsync([input], warmup: 1, iterations: 2,
            (_, _) => ++call == 2
                ? Task.FromException<AutocompleteBenchmarkOutcome>(new InvalidOperationException("fixture"))
                : Task.FromResult(new AutocompleteBenchmarkOutcome(LatencySampleDisposition.Valid, null, null, "CPU", false)));

        Assert.Multiple(() =>
        {
            Assert.That(report.WarmupExecuted, Is.EqualTo(1));
            Assert.That(report.Statistics.FailedSamples, Is.EqualTo(1));
            Assert.That(report.Statistics.ValidSamples, Is.EqualTo(1));
            Assert.That(report.Statistics.Runtime.SampleCount, Is.Zero);
            Assert.That(report.Statistics.Runtime.MissingCount, Is.EqualTo(2));
            Assert.That(report.Statistics.Runtime.P50, Is.Null);
            Assert.That(report.Records[0].Outcome, Is.EqualTo("failed"));
            Assert.That(report.Records[1].RuntimeTtftMilliseconds, Is.Null);
            Assert.That(report.Records[0].RuntimeTokensPerSecond, Is.Null);
            Assert.That(report.Records[1].RuntimeTokensPerSecond, Is.Null);
            Assert.That(report.RuntimeTokensPerSecond.SampleCount, Is.Zero);
            Assert.That(report.RuntimeTokensPerSecond.MissingCount, Is.EqualTo(2));
            Assert.That(report.RuntimeTokensPerSecond.Minimum, Is.Null);
            Assert.That(report.RuntimeTokensPerSecond.Mean, Is.Null);
            Assert.That(report.RuntimeTokensPerSecond.P50, Is.Null);
            Assert.That(report.RuntimeTokensPerSecond.P95, Is.Null);
            Assert.That(report.RuntimeTokensPerSecond.P99, Is.Null);
            Assert.That(report.RuntimeTokensPerSecond.Maximum, Is.Null);
        });
    }

    [Test]
    public async Task CancellationReturnsMeasuredPartialReportThatCannotBeMistakenForComplete()
    {
        var input = new AutocompleteBenchmarkInput("case-1", "fim", new AutocompleteRequest("db.", ""));
        using var cancellation = new CancellationTokenSource();
        var calls = 0;

        var report = await AutocompleteBenchmark.RunAsync([input], warmup: 0, iterations: 3,
            (_, token) =>
            {
                calls++;
                if (calls == 1)
                    return Task.FromResult(new AutocompleteBenchmarkOutcome(LatencySampleDisposition.Valid, null, "fixture", "CPU", false));
                cancellation.Cancel();
                return Task.FromCanceled<AutocompleteBenchmarkOutcome>(token);
            }, cancellation.Token);

        Assert.Multiple(() =>
        {
            Assert.That(report.Schema, Is.EqualTo("kapilab-autocomplete-benchmark-v5"));
            Assert.That(report.Complete, Is.False);
            Assert.That(report.Cancelled, Is.True);
            Assert.That(report.MeasuredSamples, Is.EqualTo(1));
            Assert.That(report.Records, Has.Count.EqualTo(1));
            Assert.That(report.Statistics.ValidSamples, Is.EqualTo(1));
        });
    }
}
