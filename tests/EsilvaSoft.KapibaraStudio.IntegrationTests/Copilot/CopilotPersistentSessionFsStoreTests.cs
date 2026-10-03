using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using NUnit.Framework;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

#pragma warning disable GHCP001 // Testa o provider experimental do SDK pinado.

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture, Category("Integration")]
internal sealed class CopilotPersistentSessionFsStoreTests
{
    [Test]
    public async Task SessionFilesAndSqliteSurviveStoreReconstructionAndEraseRemovesDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapibara-copilot-sessionfs-" + Guid.NewGuid().ToString("N"));
        const string id = "session-id-sensitive";
        string sessionDirectory;
        try
        {
            await using (var first = new CopilotPersistentSessionFsStore(root))
            {
                var provider = first.CreateProvider(id);
                await ((ISessionFsHandler)provider).WriteFileAsync(new SessionFsWriteFileRequest
                { SessionId = id, Path = "/workspace/transcript.txt", Content = "durable" }, CancellationToken.None);
                await provider.QueryAsync(SessionFsSqliteQueryType.Exec, "CREATE TABLE samples(value TEXT)", null, CancellationToken.None);
                await provider.QueryAsync(SessionFsSqliteQueryType.Run, "INSERT INTO samples VALUES ('persisted')", null, CancellationToken.None);
                sessionDirectory = Directory.GetDirectories(root)
                    .Single(directory => !string.Equals(Path.GetFileName(directory), ".locks", StringComparison.Ordinal));
                Assert.That(Path.GetFileName(sessionDirectory), Does.Not.Contain(id));
            }

            await using (var resumed = new CopilotPersistentSessionFsStore(root))
            {
                Assert.That(resumed.ContainsSession(id), Is.True);
                var provider = resumed.CreateProvider(id);
                var file = await ((ISessionFsHandler)provider).ReadFileAsync(new SessionFsReadFileRequest
                { SessionId = id, Path = "/workspace/transcript.txt" }, CancellationToken.None);
                Assert.That(file.Content, Is.EqualTo("durable"));
                var rows = await provider.QueryAsync(SessionFsSqliteQueryType.Query, "SELECT value FROM samples", null, CancellationToken.None);
                Assert.That(rows!.Rows.Single()["value"], Is.EqualTo("persisted"));
                var limit = await provider.QueryAsync(SessionFsSqliteQueryType.Query, "PRAGMA max_page_count", null, CancellationToken.None);
                var pageSize = await provider.QueryAsync(SessionFsSqliteQueryType.Query, "PRAGMA page_size", null, CancellationToken.None);
                Assert.That(Convert.ToInt64(limit!.Rows.Single()["max_page_count"], CultureInfo.InvariantCulture) * Convert.ToInt64(pageSize!.Rows.Single()["page_size"], CultureInfo.InvariantCulture),
                    Is.LessThanOrEqualTo(CopilotVolatileSessionFsStore.MaxSessionSqliteBytes));
                var nativeCleanupRanBeforeLocalErase = false;
                Assert.That(await resumed.DeleteSessionAsync(id, _ =>
                {
                    nativeCleanupRanBeforeLocalErase = Directory.Exists(sessionDirectory);
                    return Task.CompletedTask;
                }), Is.True);
                Assert.That(nativeCleanupRanBeforeLocalErase, Is.True);
                Assert.That(Directory.Exists(sessionDirectory), Is.False);
                Assert.That(resumed.ContainsSession(id), Is.False);
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [TestCase("../escape")]
    [TestCase("C:\\outside.txt")]
    [TestCase("safe/./file.txt")]
    public async Task ProviderRejectsTraversalAndAbsolutePaths(string path)
    {
        var root = Path.Combine(Path.GetTempPath(), "kapibara-copilot-sessionfs-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = new CopilotPersistentSessionFsStore(root);
            var provider = store.CreateProvider("id");
            var result = await ((ISessionFsHandler)provider).WriteFileAsync(new SessionFsWriteFileRequest
            { SessionId = "id", Path = path, Content = "blocked" }, CancellationToken.None);
            Assert.That(result, Is.Not.Null);
            Assert.That(result!.Code.Value, Is.Not.Empty, "The RPC response should classify the rejected path.");
            var sessionDirectory = Directory.GetDirectories(root)
                .Single(directory => !string.Equals(Path.GetFileName(directory), ".locks", StringComparison.Ordinal));
            var sessionEntries = Directory.GetFileSystemEntries(sessionDirectory).Select(Path.GetFileName).ToArray();
            Assert.That(sessionEntries, Has.Length.EqualTo(1));
            Assert.That(sessionEntries.Single(), Is.EqualTo("files"));
            var filesRoot = Path.Combine(sessionDirectory, "files");
            Assert.That(Directory.GetFileSystemEntries(filesRoot), Is.Empty, "A rejected path must not create any file or directory.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task StoreConfigEnablesPerSessionSqliteAndBindsFactory()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapibara-copilot-sessionfs-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = new CopilotPersistentSessionFsStore(root);
            var config = new SessionConfig();
            store.ConfigureSession(config);
            Assert.That(config.CreateSessionFsProvider, Is.Not.Null);
            Assert.That(CopilotPersistentSessionFsStore.CreateConfiguration(Environment.CurrentDirectory).Capabilities?.Sqlite, Is.True);
            var virtualNamespace = CopilotPersistentSessionFsStore.CreateConfiguration(Environment.CurrentDirectory);
            Assert.That(virtualNamespace.SessionStatePath, Is.EqualTo("/session-state"));
            Assert.That(virtualNamespace.InitialWorkingDirectory, Is.EqualTo("/workspace"));
            Assert.That(virtualNamespace.Conventions, Is.EqualTo(SessionFsSetProviderConventions.Posix),
                "The contained storage namespace must not expand to a Windows host drive.");
            Assert.That(Directory.Exists(root), Is.False, "Constructing the provider for account checks must not touch AppData.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task AbsoluteVirtualRuntimeStateSurvivesReconstructionInsideConfiguredStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapibara-copilot-state-" + Guid.NewGuid().ToString("N"));
        const string id = "reserved-state-id";
        const string virtualFile = "/session-state/reserved-state-id/events.jsonl";
        try
        {
            await using (var first = new CopilotPersistentSessionFsStore(root))
            {
                var provider = first.CreateProvider(id);
                var handler = (ISessionFsHandler)provider;
                var result = await handler.WriteFileAsync(new SessionFsWriteFileRequest
                { SessionId = id, Path = virtualFile, Content = "synthetic persisted event" }, CancellationToken.None);
                Assert.That(result, Is.Null, "An absolute path in the virtual state namespace must remain contained and writable.");
                var sessionDirectory = Directory.GetDirectories(root).Single(directory => Path.GetFileName(directory) != ".locks");
                Assert.That(File.Exists(Path.Combine(sessionDirectory, "files", "session-state", id, "events.jsonl")), Is.True);
            }
            await using var reconstructed = new CopilotPersistentSessionFsStore(root);
            var resumed = (ISessionFsHandler)reconstructed.CreateProvider(id);
            var read = await resumed.ReadFileAsync(new SessionFsReadFileRequest
            { SessionId = id, Path = virtualFile }, CancellationToken.None);
            Assert.That(read.Error, Is.Null);
            Assert.That(read.Content, Is.EqualTo("synthetic persisted event"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PersistentSessionUsesProductSessionFsWhileSharingOfficialCliHome()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapibara-copilot-sessionfs-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = new CopilotPersistentSessionFsStore(root);
            var fsConfiguration = CopilotPersistentSessionFsStore.CreateConfiguration(Environment.CurrentDirectory);
            var clientOptions = CopilotRuntimeSettings.SessionClientOptions(Environment.CurrentDirectory, fsConfiguration,
                cliPath: Path.GetFullPath(Path.Combine(Path.GetTempPath(), "copilot-test")));
            var sessionConfiguration = new SessionConfig();
            store.ConfigureSession(sessionConfiguration);

            Assert.Multiple(() =>
            {
                Assert.That(clientOptions.Mode, Is.EqualTo(CopilotClientMode.CopilotCli));
                Assert.That(clientOptions.UseLoggedInUser, Is.True);
                Assert.That(clientOptions.BaseDirectory, Does.Contain(".copilot"));
                Assert.That(clientOptions.SessionFs, Is.SameAs(fsConfiguration));
                Assert.That(clientOptions.WorkingDirectory, Is.EqualTo(Path.GetFullPath(Environment.CurrentDirectory)),
                    "The captured host process directory remains separate from the virtual storage namespace.");
                Assert.That(fsConfiguration.InitialWorkingDirectory, Is.EqualTo("/workspace"));
                Assert.That(sessionConfiguration.CreateSessionFsProvider, Is.Not.Null);
                Assert.That(fsConfiguration.Capabilities?.Sqlite, Is.True);
                Assert.That(Directory.Exists(root), Is.False,
                    "Configuring a product session must not create storage before that session exists.");
            });
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task PersistentProviderEnforcesPerFileQuotaWithoutLeavingPartialFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapibara-copilot-sessionfs-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = new CopilotPersistentSessionFsStore(root);
            var provider = store.CreateProvider("quota-id");
            var response = await ((ISessionFsHandler)provider).WriteFileAsync(new SessionFsWriteFileRequest
            { SessionId = "quota-id", Path = "/too-large.txt", Content = new string('x', CopilotVolatileSessionFsStore.MaxFileBytes + 1) }, CancellationToken.None);
            Assert.That(response, Is.Not.Null);
            var read = await ((ISessionFsHandler)provider).ReadFileAsync(new SessionFsReadFileRequest
            { SessionId = "quota-id", Path = "/too-large.txt" }, CancellationToken.None);
            Assert.That(read.Error, Is.Not.Null);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task StoresCannotOwnTheSamePersistentSessionConcurrently()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapibara-copilot-sessionfs-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var first = new CopilotPersistentSessionFsStore(root);
            await using var second = new CopilotPersistentSessionFsStore(root);
            var provider = first.CreateProvider("shared-session-id");
            await ((ISessionFsHandler)provider).WriteFileAsync(new SessionFsWriteFileRequest
            { SessionId = "shared-session-id", Path = "/workspace/state.txt", Content = "owned" }, CancellationToken.None);

            Assert.Throws<IOException>(() => second.CreateProvider("shared-session-id"),
                "The same session ID must have a single filesystem owner across stores/processes.");

            var read = await ((ISessionFsHandler)provider).ReadFileAsync(new SessionFsReadFileRequest
            { SessionId = "shared-session-id", Path = "/workspace/state.txt" }, CancellationToken.None);
            Assert.That(read.Content, Is.EqualTo("owned"));

            Assert.That(await first.DeleteSessionAsync("shared-session-id"), Is.True);
            var reopened = second.CreateProvider("shared-session-id");
            Assert.That(await reopened.ExistsAsync(CancellationToken.None), Is.False,
                "A second store may own the ID only after the first store erased and released it.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task DeletionCannotEraseASessionOwnedByAnotherStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapibara-copilot-sessionfs-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var first = new CopilotPersistentSessionFsStore(root);
            await using var second = new CopilotPersistentSessionFsStore(root);
            var provider = first.CreateProvider("active-session-id");
            await ((ISessionFsHandler)provider).WriteFileAsync(new SessionFsWriteFileRequest
            { SessionId = "active-session-id", Path = "/workspace/state.txt", Content = "keep" }, CancellationToken.None);
            var nativeDeleteRan = false;

            Assert.ThrowsAsync<IOException>(async () => await second.DeleteSessionAsync("active-session-id", _ =>
            {
                nativeDeleteRan = true;
                return Task.CompletedTask;
            }),
                "A different store must fail closed when the session has an active owner.");
            Assert.That(nativeDeleteRan, Is.False, "Native deletion must wait until this process owns the session lock.");
            Assert.That(second.ContainsSession("active-session-id"), Is.True);
            var read = await ((ISessionFsHandler)provider).ReadFileAsync(new SessionFsReadFileRequest
            { SessionId = "active-session-id", Path = "/workspace/state.txt" }, CancellationToken.None);
            Assert.That(read.Content, Is.EqualTo("keep"));

            await first.DisposeAsync();
            Assert.That(await second.DeleteSessionAsync("active-session-id"), Is.True);
            Assert.That(second.ContainsSession("active-session-id"), Is.False);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Test]
    public async Task SessionLockIsExclusiveAcrossProcesses()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            Assert.Ignore("A trava cross-process é verificada somente em Windows e Linux nesta suíte.");
        var root = Path.Combine(Path.GetTempPath(), "kapibara-copilot-sessionfs-" + Guid.NewGuid().ToString("N"));
        const string id = "cross-process-session-id";
        try
        {
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
            var lockPath = Path.Combine(root, ".locks", hash + ".lock");

            if (OperatingSystem.IsLinux())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);
                await AssertLinuxFlockSemanticsAsync(lockPath);
            }

            await using var store = new CopilotPersistentSessionFsStore(root);
            _ = store.CreateProvider(id);
            if (OperatingSystem.IsLinux())
            {
                await AssertLinuxFlockBlockedAsync(lockPath);
                return;
            }

            lockPath = lockPath.Replace("'", "''", StringComparison.Ordinal);
            var powershell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                "WindowsPowerShell", "v1.0", "powershell.exe");
            Assert.That(File.Exists(powershell), Is.True, "Windows PowerShell is required for this cross-process lock test.");

            var start = new ProcessStartInfo(powershell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("-NoLogo");
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-Command");
            start.ArgumentList.Add($"try {{ $s=[System.IO.File]::Open('{lockPath}',[System.IO.FileMode]::OpenOrCreate,[System.IO.FileAccess]::ReadWrite,[System.IO.FileShare]::None); $s.Dispose(); 'ACQUIRED' }} catch [System.IO.IOException] {{ 'LOCKED' }}");

            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start lock probe process.");
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
            Assert.That(process.ExitCode, Is.Zero, stderr);
            Assert.That(stdout.Trim(), Is.EqualTo("LOCKED"),
                "The operating system must deny a different process exclusive access while this store owns the session.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static async Task AssertLinuxFlockSemanticsAsync(string lockPath)
    {
        var flock = FindLinuxExecutable("flock");
        var trueCommand = FindLinuxExecutable("true");
        if (flock is null || trueCommand is null)
            Assert.Ignore("Linux cross-process lock probe requires executable `flock` and `true` commands on PATH.");

        var probe = await RunLinuxFlockAsync(flock, lockPath, trueCommand);
        if (probe.ExitCode != 0 || !string.IsNullOrWhiteSpace(probe.StandardError))
            Assert.Ignore("The native `flock` probe is present but cannot acquire a lock on the temporary filesystem; cross-process behavior was not tested.");
    }

    private static async Task AssertLinuxFlockBlockedAsync(string lockPath)
    {
        var flock = FindLinuxExecutable("flock")!;
        var trueCommand = FindLinuxExecutable("true")!;
        var probe = await RunLinuxFlockAsync(flock, lockPath, trueCommand);
        Assert.That(probe.ExitCode, Is.EqualTo(1),
            "A different Linux process must fail to acquire the flock held by FileShare.None.");
        Assert.That(probe.StandardError, Is.Empty,
            "The Linux flock probe must report the expected lock conflict without a command or filesystem error.");
    }

    private static string? FindLinuxExecutable(string name)
    {
        if (!OperatingSystem.IsLinux()) return null;

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.GetFullPath(Path.Combine(directory, name));
            try
            {
                if (File.Exists(candidate) &&
                    (File.GetUnixFileMode(candidate) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0)
                    return candidate;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Ignore unreadable PATH entries and keep looking for a usable system command.
            }
        }

        return null;
    }

    private static async Task<(int ExitCode, string StandardOutput, string StandardError)> RunLinuxFlockAsync(
        string flock, string lockPath, string trueCommand)
    {
        var start = new ProcessStartInfo(flock)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--nonblock");
        start.ArgumentList.Add(lockPath);
        start.ArgumentList.Add(trueCommand);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Linux flock probe process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw;
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    [Test]
    public async Task FailedNativeDeletionRetainsPersistentSessionForRetry()
    {
        var root = Path.Combine(Path.GetTempPath(), "kapibara-copilot-sessionfs-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var store = new CopilotPersistentSessionFsStore(root);
            var provider = store.CreateProvider("retry-session-id");
            await ((ISessionFsHandler)provider).WriteFileAsync(new SessionFsWriteFileRequest
            { SessionId = "retry-session-id", Path = "/workspace/state.txt", Content = "recoverable" }, CancellationToken.None);

            Assert.ThrowsAsync<InvalidOperationException>(async () => await store.DeleteSessionAsync("retry-session-id",
                _ => Task.FromException(new InvalidOperationException("synthetic native delete failure"))));

            var retryProvider = store.CreateProvider("retry-session-id");
            var read = await ((ISessionFsHandler)retryProvider).ReadFileAsync(new SessionFsReadFileRequest
            { SessionId = "retry-session-id", Path = "/workspace/state.txt" }, CancellationToken.None);
            Assert.That(read.Content, Is.EqualTo("recoverable"));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}

#pragma warning restore GHCP001
