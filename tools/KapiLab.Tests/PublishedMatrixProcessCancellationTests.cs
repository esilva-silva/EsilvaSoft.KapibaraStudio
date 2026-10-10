using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.KapiLab.GpuLockChild;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class PublishedMatrixProcessCancellationTests
{
    [Test]
    public async Task PublishedApphostCtrlCStopsCurrentChildAndPublishesOnlyEarlierResults()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Este teste usa CTRL_C_EVENT e apphost win-x64; Linux manual fica fora desta meta.");
        var apphost = ResolveLockedCpuApphost();
        using var workspace = new TemporaryWorkspace();
        var inputPath = Path.Combine(workspace.Path, "matrix.json");
        var reportPath = Path.Combine(workspace.Path, "reports", "lab", "matrix-result.json");
        var apphostReady = Path.Combine(workspace.Path, "apphost-ready");
        var childReady = Path.Combine(workspace.Path, "child-ready");
        var forbiddenStart = Path.Combine(workspace.Path, "must-not-start");
        var childExecutable = Path.Combine("child", typeof(GpuLockChildAssemblyMarker).Assembly.GetName().Name + ".exe");
        var environmentNames = new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "PATH", "DOTNET_ROOT", "DOTNET_ROOT_X64" };
        var environment = new Dictionary<string, string>();
        var matrix = new
        {
            schema = "kapilab-process-matrix-v1",
            cases = new object[]
            {
                new { id = "completed-before-cancel", executable = childExecutable, arguments = new[] { "matrix-success" },
                    workingDirectory = ".", timeoutSeconds = 30, environmentAllowlist = environmentNames, environment },
                new { id = "active-when-cancelled", executable = childExecutable, arguments = new[] { "matrix-wait-ready", childReady },
                    workingDirectory = ".", timeoutSeconds = 120, environmentAllowlist = environmentNames, environment },
                new { id = "must-not-start", executable = childExecutable, arguments = new[] { "matrix-write-marker", forbiddenStart },
                    workingDirectory = ".", timeoutSeconds = 30, environmentAllowlist = environmentNames, environment },
            }
        };
        await File.WriteAllTextAsync(inputPath, JsonSerializer.Serialize(matrix));

        using var supervisor = StartChild("run-published-matrix", apphost, workspace.Path, inputPath, apphostReady);
        var supervisorOutput = supervisor.StandardOutput.ReadToEndAsync();
        var supervisorError = supervisor.StandardError.ReadToEndAsync();
        int? apphostPid = null;
        int? activeChildPid = null;
        try
        {
            await WaitForFileAsync(supervisor, apphostReady, TimeSpan.FromSeconds(20), "published apphost");
            apphostPid = int.Parse(await File.ReadAllTextAsync(apphostReady), CultureInfo.InvariantCulture);
            Assert.That(IsProcessRunning(apphostPid.Value), Is.True, "The marker identifies the published apphost, not the supervisor fixture.");

            await WaitForFileAsync(supervisor, childReady, TimeSpan.FromSeconds(30), "active matrix child");
            activeChildPid = int.Parse(await File.ReadAllTextAsync(childReady), CultureInfo.InvariantCulture);
            Assert.That(IsProcessRunning(activeChildPid.Value), Is.True, "The second matrix case must be running before Ctrl+C.");
            Assert.That(File.Exists(forbiddenStart), Is.False);
            Assert.That(File.Exists(reportPath), Is.False, "The report is published only after the current child stops.");

            using (var signaler = StartChild("console-cancel", supervisor.Id.ToString(CultureInfo.InvariantCulture)))
            {
                try
                {
                    await signaler.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                    Assert.That(signaler.ExitCode, Is.Zero, "The isolated signaler must deliver CTRL_C_EVENT to the supervisor console.");
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

            await supervisor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Assert.That(supervisor.ExitCode, Is.EqualTo(12));
            Assert.That(await supervisorOutput, Does.Contain("matrix-result.json"));
            _ = await supervisorError;
            await WaitUntilProcessStopsAsync(apphostPid.Value, TimeSpan.FromSeconds(10));
            await WaitUntilProcessStopsAsync(activeChildPid.Value, TimeSpan.FromSeconds(10));
            Assert.That(File.Exists(forbiddenStart), Is.False, "Cases after cancellation must never start.");

            using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
            var cases = report.RootElement.GetProperty("cases");
            Assert.Multiple(() =>
            {
                Assert.That(report.RootElement.GetProperty("schema").GetString(), Is.EqualTo("kapilab-process-matrix-v1"));
                Assert.That(report.RootElement.GetProperty("complete").GetBoolean(), Is.False);
                Assert.That(cases.GetArrayLength(), Is.EqualTo(2), "The completed case is retained; the following case is absent.");
                Assert.That(cases[0].GetProperty("id").GetString(), Is.EqualTo("completed-before-cancel"));
                Assert.That(cases[0].GetProperty("status").GetString(), Is.EqualTo("succeeded"));
                Assert.That(cases[0].GetProperty("exitCode").GetInt32(), Is.Zero);
                Assert.That(cases[1].GetProperty("id").GetString(), Is.EqualTo("active-when-cancelled"));
                Assert.That(cases[1].GetProperty("status").GetString(), Is.EqualTo("cancelled"));
                Assert.That(cases[1].GetProperty("exitCode").ValueKind, Is.EqualTo(JsonValueKind.Null));
            });
        }
        finally
        {
            if (!supervisor.HasExited)
            {
                supervisor.Kill(entireProcessTree: true);
                await supervisor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            if (apphostPid is { } parentId) KillIfRunning(parentId);
            if (activeChildPid is { } childId) KillIfRunning(childId);
        }
    }

    private static string ResolveLockedCpuApphost()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var lockPath = Path.Combine(directory.FullName, "tools", "kapilab.lock.json");
            if (File.Exists(lockPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
                var entry = document.RootElement.GetProperty("entries").EnumerateArray()
                    .Single(item => item.GetProperty("backend").GetString() == "Cpu" && item.GetProperty("rid").GetString() == "win-x64");
                var path = Path.GetFullPath(entry.GetProperty("executable").GetString()!, Path.GetDirectoryName(lockPath)!);
                if (!File.Exists(path)) Assert.Ignore("O apphost CPU publicado e travado no lock não está presente neste checkout.");
                var expectedHash = entry.GetProperty("sha256").GetString();
                var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                Assert.That(actualHash, Is.EqualTo(expectedHash), "The apphost executable must match the checked-in distribution lock.");
                return path;
            }
            directory = directory.Parent;
        }
        Assert.Ignore("Não foi possível localizar tools/kapilab.lock.json a partir do test assembly.");
        return string.Empty;
    }

    private static async Task WaitForFileAsync(Process process, string path, TimeSpan timeout, string subject)
    {
        using var budget = new CancellationTokenSource(timeout);
        while (!File.Exists(path))
        {
            if (process.HasExited) Assert.Fail($"A fixture encerrou antes da prontidão de {subject}: código {process.ExitCode}.");
            await Task.Delay(25, budget.Token);
        }
    }

    private static bool IsProcessRunning(int processId)
    {
        try { using var process = Process.GetProcessById(processId); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task WaitUntilProcessStopsAsync(int processId, TimeSpan timeout)
    {
        using var budget = new CancellationTokenSource(timeout);
        while (IsProcessRunning(processId)) await Task.Delay(25, budget.Token);
    }

    private static void KillIfRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (ArgumentException) { }
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
        return Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar a fixture de cancelamento da matriz publicada.");
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-published-matrix-cancel", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
            var childDirectory = Directory.CreateDirectory(System.IO.Path.Combine(Path, "child"));
            var source = System.IO.Path.GetDirectoryName(typeof(GpuLockChildAssemblyMarker).Assembly.Location)!;
            foreach (var file in Directory.EnumerateFiles(source))
                File.Copy(file, System.IO.Path.Combine(childDirectory.FullName, System.IO.Path.GetFileName(file)));
        }

        public string Path { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
