using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.GpuLockChild;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class BenchmarkProcessCancellationTests
{
    [Test]
    public async Task RealWindowsCtrlCPreservesThePriorSampleAndPublishesAnIncompleteCancelledReport()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Este teste exercita CTRL_C_EVENT real de console Windows; Linux permanece manual.");
        using var workspace = new TemporaryWorkspace();
        var reportPath = Path.Combine(workspace.Path, "report.jsonl");
        var readyPath = Path.Combine(workspace.Path, "ready");
        using var child = StartChild("benchmark-cancel", reportPath, readyPath);
        var childError = child.StandardError.ReadToEndAsync();
        var childOutput = child.StandardOutput.ReadToEndAsync();
        try
        {
            await WaitUntilReadyAsync(child, readyPath);
            Assert.That(await File.ReadAllTextAsync(readyPath), Is.EqualTo(child.Id.ToString(CultureInfo.InvariantCulture)));
            Assert.That(File.Exists(reportPath), Is.False, "A final report must not be published before the interruption is observed.");

            using (var signaler = StartChild("console-cancel", child.Id.ToString(CultureInfo.InvariantCulture)))
            {
                try
                {
                    await signaler.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.That(signaler.ExitCode, Is.Zero, "The isolated signaler must confirm delivery through GenerateConsoleCtrlEvent.");
                }
                finally
                {
                    if (!signaler.HasExited)
                    {
                        signaler.Kill(entireProcessTree: true);
                        await signaler.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    }
                }
            }
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(child.HasExited, Is.True, "Cancellation is only accepted after process exit is confirmed.");
            Assert.That(child.ExitCode, Is.EqualTo(12));
            _ = await Task.WhenAll(childError, childOutput).WaitAsync(TimeSpan.FromSeconds(5));
            var lines = await File.ReadAllLinesAsync(reportPath);
            Assert.That(lines, Has.Length.EqualTo(2), "One completed sample plus the incomplete summary must survive.");
            using var sample = JsonDocument.Parse(lines[0]);
            using var summary = JsonDocument.Parse(lines[1]);
            Assert.Multiple(() =>
            {
                Assert.That(sample.RootElement.GetProperty("schema").GetString(), Is.EqualTo("kapilab-autocomplete-benchmark-sample-v3"));
                Assert.That(sample.RootElement.GetProperty("id").GetString(), Is.EqualTo("prior-sample"));
                Assert.That(sample.RootElement.GetProperty("outcome").GetString(), Is.EqualTo("valid"));
                Assert.That(summary.RootElement.GetProperty("schema").GetString(), Is.EqualTo("kapilab-autocomplete-benchmark-v5"));
                Assert.That(summary.RootElement.GetProperty("complete").GetBoolean(), Is.False);
                Assert.That(summary.RootElement.GetProperty("cancelled").GetBoolean(), Is.True);
                Assert.That(summary.RootElement.GetProperty("measuredSamples").GetInt32(), Is.EqualTo(1));
                Assert.That(summary.RootElement.GetProperty("records").GetArrayLength(), Is.EqualTo(1));
            });
        }
        finally
        {
            if (!child.HasExited)
            {
                child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    private static async Task WaitUntilReadyAsync(Process child, string readyPath)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!File.Exists(readyPath))
        {
            if (child.HasExited) Assert.Fail($"Benchmark fixture exited before readiness: code {child.ExitCode}.");
            await Task.Delay(25, budget.Token);
        }
    }

    private static Process StartChild(params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        start.ArgumentList.Add(typeof(GpuLockChildAssemblyMarker).Assembly.Location);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar a fixture de cancelamento.");
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-process-cancellation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
