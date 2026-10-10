using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.GpuLockChild;
using NUnit.Framework;
using System.Diagnostics;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class GpuModelLoadLockTests
{
    [Test]
    public async Task SeparateWorkspacesContendAcrossProcessesAndLockRecoversAfterAbruptExit()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("Este teste valida o lock de arquivo entre processos no Windows.");

        using var workspaceA = new TemporaryWorkspace();
        using var workspaceB = new TemporaryWorkspace();
        using var profileDirectory = new TemporaryWorkspace();
        Assert.That(workspaceA.Path, Is.Not.EqualTo(workspaceB.Path));
        var sharedLockDirectory = Path.Combine(profileDirectory.Path, "locks");
        Assert.That(IsOutside(sharedLockDirectory, workspaceA.Path), Is.True);
        Assert.That(IsOutside(sharedLockDirectory, workspaceB.Path), Is.True);
        var childAssembly = typeof(GpuLockChildAssemblyMarker).Assembly.Location;
        using var holder = StartChild(childAssembly, "hold", sharedLockDirectory, workspaceA.Path, "run-a");
        try
        {
            var ready = await holder.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(ready, Is.EqualTo("ready"));

            using (var contender = StartChild(childAssembly, "try", sharedLockDirectory, workspaceB.Path, "run-b"))
            {
                await contender.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                Assert.That(contender.ExitCode, Is.EqualTo(9));
                Assert.That(await contender.StandardOutput.ReadToEndAsync(), Is.EqualTo("busy" + Environment.NewLine));
            }

            holder.Kill(entireProcessTree: true);
            await holder.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

            using var recovered = StartChild(childAssembly, "try", sharedLockDirectory, workspaceB.Path, "run-c");
            await recovered.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(recovered.ExitCode, Is.Zero);
            Assert.That(await recovered.StandardOutput.ReadToEndAsync(), Is.EqualTo("acquired" + Environment.NewLine));
        }
        finally
        {
            if (!holder.HasExited)
            {
                holder.Kill(entireProcessTree: true);
                await holder.WaitForExitAsync();
            }
        }
    }

    [Test]
    public void SecondAcquisitionFailsImmediatelyWithDistinctBusyException()
    {
        using var workspace = new TemporaryWorkspace();
        using var first = GpuModelLoadLock.Acquire(Path.Combine(workspace.Path, "tmp"), "test-owner", "run-1");

        var exception = Assert.Throws<GpuModelLoadLockUnavailableException>(() =>
            GpuModelLoadLock.Acquire(Path.Combine(workspace.Path, "tmp"), "other-owner", "run-2"));
        Assert.That(exception!.LockPath, Is.EqualTo(Path.Combine(workspace.Path, "tmp", "kapilab-model-load.lock")));
        Assert.That(first.Owner.Owner, Is.EqualTo("test-owner"));
        Assert.That(first.Owner.RunId, Is.EqualTo("run-1"));
        Assert.That(first.Owner.ProcessId, Is.EqualTo(Environment.ProcessId));
    }

    [Test]
    public void DisposingLeaseReleasesOnlyItsHandleAndAllowsNextOwner()
    {
        using var workspace = new TemporaryWorkspace();
        var path = Path.Combine(workspace.Path, "tmp", "kapilab-model-load.lock");
        var first = GpuModelLoadLock.Acquire(Path.Combine(workspace.Path, "tmp"), "first", "run-1");
        first.Dispose();
        first.Dispose();

        using var next = GpuModelLoadLock.Acquire(Path.Combine(workspace.Path, "tmp"), "next", "run-2");
        Assert.That(next.Owner.Owner, Is.EqualTo("next"));
        Assert.That(File.Exists(path), Is.True, "The reusable lock file is not removed on release.");
    }

    [Test]
    public void ExistingOrphanFileCanBeReusedWithoutTimeoutOrDeletion()
    {
        using var workspace = new TemporaryWorkspace();
        var directory = System.IO.Directory.CreateDirectory(Path.Combine(workspace.Path, "tmp"));
        var path = Path.Combine(directory.FullName, "kapilab-model-load.lock");
        File.WriteAllText(path, "{\"owner\":\"crashed-process\",\"runId\":\"orphaned\"}");

        using var lease = GpuModelLoadLock.Acquire(Path.Combine(workspace.Path, "tmp"), "recovery", "run-after-crash");
        Assert.That(lease.Owner.Owner, Is.EqualTo("recovery"));
        Assert.That(lease.Owner.RunId, Is.EqualTo("run-after-crash"));
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-lock-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => System.IO.Directory.Delete(Path, recursive: true);
    }

    private static Process StartChild(string childAssembly, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(childAssembly);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        return Process.Start(start) ?? throw new InvalidOperationException("Não foi possível iniciar o helper de lock.");
    }

    private static bool IsOutside(string path, string directory)
    {
        var relative = Path.GetRelativePath(directory, path);
        return relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }
}
