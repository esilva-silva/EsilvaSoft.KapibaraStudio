using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.GpuLockChild;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class RealModelCliCancellationTests
{
    [Test]
    public async Task CtrlCWhileModelRunAutocompleteIsGeneratingReturnsCancelledWithoutPublishingRecord()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Este teste exercita CTRL_C_EVENT real de console Windows; Linux permanece manual.");
        var package = Environment.GetEnvironmentVariable("KAPILAB_REAL_CPU_MODEL");
        if (string.IsNullOrWhiteSpace(package) || !Directory.Exists(package))
            Assert.Ignore("Defina KAPILAB_REAL_CPU_MODEL para habilitar a prova local com pesos reais.");

        using var workspace = new TemporaryWorkspace();
        var inputPath = Path.Combine(workspace.Path, "records.jsonl");
        var readyPath = Path.Combine(workspace.Path, "generation-started");
        await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(new
        {
            id = "synthetic-cancel",
            type = "fim",
            prefix = "db.",
            suffix = "",
        }) + "\n");

        using var child = StartChild("model-run-cancel", Path.GetFullPath(package), inputPath, workspace.Path, readyPath);
        var childError = child.StandardError.ReadToEndAsync();
        var childOutput = child.StandardOutput.ReadToEndAsync();
        try
        {
            await WaitUntilReadyAsync(child, readyPath);
            Assert.That(await File.ReadAllTextAsync(readyPath), Is.EqualTo(child.Id.ToString(CultureInfo.InvariantCulture)),
                "The readiness marker is emitted only after the real ONNX runtime has generated its first token.");

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

            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.That(child.HasExited, Is.True, "Cancellation is only accepted after process exit is confirmed.");
            Assert.That(child.ExitCode, Is.EqualTo(12));
            var output = await childOutput.WaitAsync(TimeSpan.FromSeconds(5));
            var error = await childError.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Multiple(() =>
            {
                Assert.That(output, Is.Empty, "The CLI must not publish an incomplete autocomplete record.");
                Assert.That(error, Does.Contain("model.run.autocomplete cancelled"));
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
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        while (!File.Exists(readyPath))
        {
            if (child.HasExited) Assert.Fail($"KapiLab model-run fixture exited before native inference: code {child.ExitCode}.");
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
        return Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar a fixture de inferência real.");
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-model-cancellation", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
