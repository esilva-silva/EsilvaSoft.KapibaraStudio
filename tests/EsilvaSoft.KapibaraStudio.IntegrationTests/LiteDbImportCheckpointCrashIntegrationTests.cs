using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LiteDbImportCheckpointCrashIntegrationTests
{
    private const string WorkerModeVariable = "KAPIBARA_F6_CRASH_WORKER";
    private const string WorkspaceVariable = "KAPIBARA_F6_CRASH_WORKSPACE";
    private const string ReadyVariable = "KAPIBARA_F6_CRASH_READY";
    private const string CheckpointVariable = "KAPIBARA_F6_CRASH_CHECKPOINT";

    [Test]
    public async Task AbruptWorkerTerminationPreservesCheckpointForRecovery()
    {
        var marker = Guid.NewGuid().ToString("N");
        var root = Path.Combine(Path.GetTempPath(), "KapibaraStudio.F6.Crash", marker);
        Directory.CreateDirectory(root);
        var workspacePath = Path.Combine(root, "workspace.db");
        var readyPath = Path.Combine(root, "checkpoint-ready.txt");
        var checkpointId = Guid.NewGuid();
        using var process = StartWorker(workspacePath, readyPath, checkpointId);
        var processExited = false;

        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(30);
            while (!File.Exists(readyPath) && !process.HasExited && DateTimeOffset.UtcNow < deadline)
                await Task.Delay(25);

            Assert.That(File.Exists(readyPath), Is.True, "O worker não persistiu o receipt antes do deadline.");
            Assert.That(await File.ReadAllTextAsync(readyPath), Is.EqualTo(checkpointId.ToString("N")));

            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
            processExited = true;
            Assert.That(process.ExitCode, Is.Not.EqualTo(0), "A terminação forçada deve interromper o worker abruptamente.");

            using var reopenedOwner = new LiteDbConnectionProfileRepository(workspacePath);
            var pending = await ((IImportCheckpointRepository)reopenedOwner).GetPendingAsync();
            Assert.That(pending, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(pending[0].Id, Is.EqualTo(checkpointId));
                Assert.That(pending[0].State, Is.EqualTo(ImportCheckpointState.Prepared));
                Assert.That(pending[0].Kind, Is.EqualTo(ImportCheckpointKind.LogicalPackage));
                Assert.That(pending[0].TargetDatabase, Is.EqualTo("f6_crash_target"));
                Assert.That(pending[0].SourceSha256, Has.Length.EqualTo(64));
            });
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                processExited = true;
            }
            if (processExited && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task CrashWorkerWaitsAfterPersistingCheckpoint()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(WorkerModeVariable), "1", StringComparison.Ordinal))
            Assert.Ignore("Worker interno: iniciado apenas pelo teste pai de terminação abrupta.");

        var workspacePath = Environment.GetEnvironmentVariable(WorkspaceVariable)
            ?? throw new InvalidOperationException("Caminho LiteDB do worker não informado.");
        var readyPath = Environment.GetEnvironmentVariable(ReadyVariable)
            ?? throw new InvalidOperationException("Handshake do worker não informado.");
        var checkpointIdText = Environment.GetEnvironmentVariable(CheckpointVariable)
            ?? throw new InvalidOperationException("ID do receipt do worker não informado.");
        var checkpointId = Guid.ParseExact(checkpointIdText, "N");
        var checkpoint = CreateCheckpoint(checkpointId);

        using (var owner = new LiteDbConnectionProfileRepository(workspacePath))
            await ((IImportCheckpointRepository)owner).CreateAsync(checkpoint);

        await File.WriteAllTextAsync(readyPath, checkpoint.Id.ToString("N"));
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    private static Process StartWorker(string workspacePath, string readyPath, Guid checkpointId)
    {
        var assemblyPath = typeof(LiteDbImportCheckpointCrashIntegrationTests).Assembly.Location;
        var startInfo = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("vstest");
        startInfo.ArgumentList.Add(assemblyPath);
        startInfo.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName~CrashWorkerWaitsAfterPersistingCheckpoint");
        startInfo.ArgumentList.Add("--logger:console;verbosity=minimal");
        startInfo.Environment[WorkerModeVariable] = "1";
        startInfo.Environment[WorkspaceVariable] = workspacePath;
        startInfo.Environment[ReadyVariable] = readyPath;
        startInfo.Environment[CheckpointVariable] = checkpointId.ToString("N");

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Não foi possível iniciar o worker de crash para o teste LiteDB.");
        return process;
    }

    private static ImportCheckpoint CreateCheckpoint(Guid id)
    {
        var suffix = id.ToString("N");
        return new ImportCheckpoint(ImportCheckpoint.CurrentVersion, id, ImportCheckpointKind.LogicalPackage,
            Guid.NewGuid(), Guid.NewGuid(), Hash("path:" + suffix), Hash("source:" + suffix),
            Hash("plan:" + suffix), "f6_crash_target", null, 0, 1, 0, 0, 0,
            ImportCheckpointState.Prepared, DateTimeOffset.UtcNow);
    }

    private static string Hash(string value) => ImportCheckpointRecovery.Sha256OfText(value);
}
