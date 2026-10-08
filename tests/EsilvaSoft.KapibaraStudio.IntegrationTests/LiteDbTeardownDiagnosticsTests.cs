using System.Diagnostics;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class LiteDbTeardownDiagnosticsTests
{
    [Test, Platform("Win")]
    public async Task FailedDeleteObservesChildResourceUserOnceWithoutReleasingItsLock()
    {
        var directory = Path.Combine(Path.GetTempPath(), "KapibaraStudio.Tests", "teardown-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, LiteDbTeardownDiagnostics.LogFileName);
        await File.WriteAllTextAsync(path, "synthetic lock fixture");
        Process? child = null;
        try
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
            };
            start.ArgumentList.Add("-NoLogo");
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add("$lock = [IO.File]::Open($env:KAPIBARA_SYNTHETIC_LOCK_FILE, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::Read); try { [Console]::Out.WriteLine('locked'); [Console]::Out.Flush(); $null = [Console]::In.ReadLineAsync().Wait(30000) } finally { $lock.Dispose() }");
            start.Environment["KAPIBARA_SYNTHETIC_LOCK_FILE"] = path;
            child = Process.Start(start) ?? throw new InvalidOperationException("Processo filho de teste não iniciou.");
            var ready = await child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
            Assert.That(ready, Is.EqualTo("locked"), "O filho precisa confirmar o lock antes da exclusão.");
            var error = Assert.Throws<IOException>(() => Directory.Delete(directory, recursive: true))!;
            var reports = new List<LiteDbTeardownDiagnostics.Snapshot>();
            var firstFailureObserved = false;
            LiteDbTeardownDiagnostics.ObserveFirstFailure(directory, error, ref firstFailureObserved, true, reports.Add);
            LiteDbTeardownDiagnostics.ObserveFirstFailure(directory, error, ref firstFailureObserved, true, reports.Add);

            Assert.That(reports, Has.Count.EqualTo(1));
            var report = reports.Single();
            Assert.Multiple(() =>
            {
                Assert.That(report.FilePath, Is.EqualTo(path));
                Assert.That(report.DirectoryPath, Is.EqualTo(directory));
                Assert.That(report.HResult, Is.EqualTo(error.HResult));
                Assert.That(report.TestCase, Is.EqualTo(TestContext.CurrentContext.Test.FullName));
                Assert.That(report.ObserverProcessId, Is.EqualTo(Environment.ProcessId));
                Assert.That(report.CapturedAtUtc.Offset, Is.EqualTo(TimeSpan.Zero));
                Assert.That(report.NativeError, Is.Zero);
                Assert.That(child.HasExited, Is.False, "A consulta não encerra o processo observado.");
                Assert.That(File.Exists(path), Is.True, "A observação não repete delete nem apaga o arquivo.");
                Assert.Throws<IOException>(() => File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose(),
                    "A consulta não libera o lock do filho.");
            });
            if (!report.ResourceUsers.Any(user => user.ProcessId == (uint)child.Id))
            {
                Assert.Warn("Restart Manager não listou o PID do filho que comprovadamente mantém o arquivo aberto; esta plataforma não comprova identificação do bloqueador.");
            }
            else
            {
                TestContext.Progress.WriteLine($"Restart Manager listou PID sintético {child.Id}; isso identifica usuário do recurso, não atribui a causa histórica.");
                Assert.That(report.ResourceUsers.Single(user => user.ProcessId == (uint)child.Id).StartTimeFileTime, Is.GreaterThan(0));
            }
        }
        finally
        {
            if (child is not null)
            {
                if (!child.HasExited)
                {
                    await child.StandardInput.WriteLineAsync("release");
                    child.StandardInput.Close();
                    // The synthetic child also self-releases after 30 s if the parent cannot signal it.
                    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(40));
                }
                child.Dispose();
            }
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Test, Platform("Win")]
    public void OptOutAndDiagnosticFailureCannotReplaceTheCleanupException()
    {
        var error = new IOException("synthetic sharing violation", unchecked((int)0x80070020));
        var observed = false;
        var emitted = 0;
        LiteDbTeardownDiagnostics.ObserveFirstFailure(Path.GetTempPath(), error, ref observed, false, _ => emitted++);
        Assert.That(emitted, Is.Zero, "Opt-out must not invoke the diagnostic sink.");

        observed = false;
        var caught = Assert.Throws<IOException>(() =>
        {
            try { throw error; }
            catch (IOException original)
            {
                LiteDbTeardownDiagnostics.ObserveFirstFailure(Path.GetTempPath(), original, ref observed, true,
                    _ => throw new InvalidOperationException("synthetic sink failure"));
                throw;
            }
        });
        Assert.That(caught, Is.SameAs(error), "Diagnostic errors cannot replace the original cleanup failure.");
        Assert.That(observed, Is.True);
    }
}
