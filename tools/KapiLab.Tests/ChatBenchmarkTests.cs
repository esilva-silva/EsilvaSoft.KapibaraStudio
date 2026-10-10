using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class ChatBenchmarkTests
{
    private static readonly AutocompleteSettings Settings = new()
    {
        ModelPath = "package", ContextTokens = 4096, MaximumCompletionTokens = 128,
    };

    [Test]
    public async Task WarmupIsExcludedAndMissingRuntimeTtftStaysNull()
    {
        var calls = 0;
        var inputs = new[]
        {
            new ModelRunChatCommand.ChatInput("a", "Pergunta A"),
            new ModelRunChatCommand.ChatInput("b", "Pergunta B"),
        };
        var report = await ChatBenchmark.RunAsync(inputs, 2, 2, (_, _) =>
        {
            calls++;
            var ttft = calls > 2 && calls % 2 != 0 ? TimeSpan.FromMilliseconds(5) : (TimeSpan?)null;
            return Task.FromResult(new ChatBenchmarkOutcome(LatencySampleDisposition.Valid, ttft,
                TimeSpan.FromMilliseconds(8), 3, "cpu", false));
        });
        Assert.Multiple(() =>
        {
            Assert.That(calls, Is.EqualTo(6));
            Assert.That(report.WarmupExecuted, Is.EqualTo(2));
            Assert.That(report.Samples, Has.Count.EqualTo(4));
            Assert.That(report.Statistics.ValidSamples, Is.EqualTo(4));
            Assert.That(report.Statistics.Observed.SampleCount, Is.EqualTo(4));
            Assert.That(report.Statistics.Runtime.SampleCount, Is.EqualTo(2));
            Assert.That(report.Statistics.Runtime.MissingCount, Is.EqualTo(2));
            Assert.That(report.Samples[1].RuntimeTimeToFirstToken, Is.Null);
        });
    }

    [Test]
    public async Task FakeChatServiceClassifiesTruncationRefusalAndSplitSecretWithoutEmittingText()
    {
        var truncatedService = new ModelRunChatCommandTests.ChatServiceFake
        {
            Chunks = ["resposta"], Complete = false, FirstToken = TimeSpan.FromMilliseconds(2),
        };
        var truncated = await BenchChatCommand.InferAsync(truncatedService, Settings, new("a", "Pergunta"));
        var refusedService = new ModelRunChatCommandTests.ChatServiceFake
        {
            UnavailableReason = LocalModelUnavailableReason.CapabilityMissing,
        };
        var refused = await BenchChatCommand.InferAsync(refusedService, Settings, new("b", "Pergunta"));
        var sensitiveService = new ModelRunChatCommandTests.ChatServiceFake { Chunks = ["pass", "word=abc"] };
        var sensitive = await BenchChatCommand.InferAsync(sensitiveService, Settings, new("c", "Pergunta"));
        Assert.Multiple(() =>
        {
            Assert.That(truncated.Disposition, Is.EqualTo(LatencySampleDisposition.Truncated));
            Assert.That(truncated.RuntimeTimeToFirstToken, Is.EqualTo(TimeSpan.FromMilliseconds(2)));
            Assert.That(refused.Disposition, Is.EqualTo(LatencySampleDisposition.Refused));
            Assert.That(refused.RuntimeTimeToFirstToken, Is.Null);
            Assert.That(sensitive.Disposition, Is.EqualTo(LatencySampleDisposition.Refused));
            Assert.That(sensitive.Reason, Is.EqualTo("privacy_output"));
            Assert.That(typeof(ChatBenchmarkOutcome).GetProperties().Select(property => property.Name),
                Does.Not.Contain("Text").And.Not.Contain("Message"));
        });
    }

    [Test]
    public async Task CancellationReturnsPartialReportWithoutInventedLatency()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var report = await ChatBenchmark.RunAsync([new("a", "Pergunta")], 1, 1,
            (_, token) => { token.ThrowIfCancellationRequested(); throw new AssertionException("Não deve executar inferência."); },
            cancellation.Token);
        Assert.Multiple(() =>
        {
            Assert.That(report.Cancelled, Is.True);
            Assert.That(report.Complete, Is.False);
            Assert.That(report.Samples, Is.Empty);
            Assert.That(report.Statistics.Runtime.Mean, Is.Null);
            Assert.That(report.Statistics.Observed.Mean, Is.Null);
        });
    }

    [Test]
    public async Task GpuLockBusyDuringWarmupMapsToExitCodeNineWithoutMeasuredSamples()
    {
        var report = await ChatBenchmark.RunAsync([new("a", "Pergunta")], 1, 1,
            (_, _) => Task.FromResult(new ChatBenchmarkOutcome(LatencySampleDisposition.Failed,
                null, null, null, null, null, "gpu_lock_busy")));
        Assert.Multiple(() =>
        {
            Assert.That(report.StopReason, Is.EqualTo("gpu_lock_busy"));
            Assert.That(report.Samples, Is.Empty);
            Assert.That(BenchChatCommand.MapExitCode(report), Is.EqualTo(9));
        });
    }
}
