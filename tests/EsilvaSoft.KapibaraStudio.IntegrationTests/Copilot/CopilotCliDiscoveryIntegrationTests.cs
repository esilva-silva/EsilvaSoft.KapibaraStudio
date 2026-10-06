using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

[TestFixture, Category("Integration")]
internal sealed class CopilotCliDiscoveryIntegrationTests
{
    private static readonly string[] LoginArguments = ["login"];
    [Test]
    public void AccountCliUsesNativeUserInstallationOrPath()
    {
        Assert.That(LocalCopilotAccountCommands.FindCliExecutable(string.Empty), Is.Null);
        Assert.That(LocalCopilotAccountCommands.FindInstalledCliExecutable(), Is.EqualTo(
            new LocalCopilotCliConfiguration().ResolveExecutablePath()));
    }

    [Test]
    public void NativeUserInstallationWinsOverStaleProcessPath()
    {
        var local = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "copilot-user"));
        var stale = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "copilot-path"));
        var native = Path.Combine(local, "GitHubCopilotCLI", "copilot.exe");
        var staleExecutable = Path.Combine(stale, "copilot.exe");
        Assert.That(LocalCopilotCliConfiguration.Resolve(null, stale, local, true,
            candidate => candidate == native || candidate == staleExecutable, _ => true), Is.EqualTo(native));
    }

    [Test]
    public void ExplicitSelectionWinsAndMissingSelectionNeverFallsBack()
    {
        var selected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "selected", "copilot.exe"));
        var local = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "copilot-user"));
        var path = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "copilot-path"));
        Assert.That(LocalCopilotCliConfiguration.Resolve(selected, path, local, true, _ => true, _ => true),
            Is.EqualTo(selected));
        var examined = new List<string>();
        Assert.Throws<InvalidOperationException>(() => LocalCopilotCliConfiguration.Resolve(selected, path, local,
            true, candidate => { examined.Add(candidate); return false; }, _ => true));
        Assert.That(examined, Is.EqualTo(new[] { selected }), "An explicit missing CLI must not switch installations.");
    }

    [TestCase("relative/copilot.exe")]
    [TestCase("copilot")]
    public void ExplicitSelectionRejectsRelativePaths(string path) =>
        Assert.Throws<ArgumentException>(() => LocalCopilotCliConfiguration.NormalizeExecutablePath(path, true));

    [TestCase("copilot.cmd")]
    [TestCase("copilot.bat")]
    [TestCase("copilot.ps1")]
    public void ExplicitSelectionRejectsWindowsShellShims(string name) =>
        Assert.Throws<ArgumentException>(() => LocalCopilotCliConfiguration.NormalizeExecutablePath(
            Path.GetFullPath(Path.Combine(Path.GetTempPath(), name)), true));

    [Test]
    public void UnixSelectionRequiresExecutablePermissionAndNeverFallsBack()
    {
        var selected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "selected", "copilot"));
        Assert.Throws<InvalidOperationException>(() => LocalCopilotCliConfiguration.Resolve(selected,
            Path.GetTempPath(), null, false, _ => true, _ => false));
    }

    [Test]
    public async Task InvalidConfigurationBlocksAccountAndRuntimeWithoutStartingProcess()
    {
        var configured = new LocalCopilotCliConfiguration
        {
            ExecutablePath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"),
                OperatingSystem.IsWindows() ? "copilot.exe" : "copilot")),
        };
        var commands = new LocalCopilotAccountCommands(configured);
        Assert.That(commands.IsCliInstalled(), Is.False);
        Assert.That(await commands.RunVisibleAsync("login", CancellationToken.None),
            Is.EqualTo(CopilotAccountCommandState.RuntimeUnavailable));
        using var resources = new LocalCopilotRuntimeResources(configuration: configured);
        Assert.Throws<InvalidOperationException>(() => resources.CreateAccountClient());
        Assert.Throws<InvalidOperationException>(() => resources.CreateSessionClient(null, false));
        Assert.Throws<InvalidOperationException>(() => configured.ValidateExecutablePath(configured.ExecutablePath));
        Assert.That(configured.ValidateExecutablePath(null), Is.Null);
        Assert.That(configured.ExecutablePath, Is.Not.Null, "Validation must not mutate the active selection.");
    }

    [Test]
    public void AccountProcessUsesOfficialHomeAndDoesNotInheritTokensOrRedirectLogin()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("A criação de terminal Linux exige homologação nativa.");
        var cli = Path.Combine(Path.GetTempPath(), OperatingSystem.IsWindows() ? "copilot-test.exe" : "copilot-test");
        var start = LocalCopilotAccountCommands.CreateStartInfo(cli, LoginArguments)!;
        Assert.Multiple(() =>
        {
            Assert.That(start.FileName, Is.EqualTo(cli));
            Assert.That(start.ArgumentList, Is.EqualTo(LoginArguments));
            Assert.That(start.UseShellExecute, Is.False);
            Assert.That(start.CreateNoWindow, Is.False);
            Assert.That(start.RedirectStandardOutput || start.RedirectStandardError || start.RedirectStandardInput, Is.False);
            Assert.That(start.Environment["COPILOT_HOME"], Is.EqualTo(CopilotRuntimeSettings.OfficialCliHomeDirectory()));
            Assert.That(start.Environment.Keys, Does.Not.Contain("GH_TOKEN").And.Not.Contain("GITHUB_TOKEN").And.Not.Contain("COPILOT_GITHUB_TOKEN"));
        });
    }

    [Test]
    public void CliDetectionUsesOnlyAbsoluteExecutableCandidates()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KapibaraStudio.CopilotCliTest"));
        var root = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, OperatingSystem.IsWindows() ? "copilot.exe" : "copilot");
        try
        {
            Assert.That(LocalCopilotAccountCommands.FindCliExecutable(root), Is.Null);
            Assert.That(LocalCopilotAccountCommands.FindCliExecutable("relative-path"), Is.Null);
            File.WriteAllText(executable, "test placeholder");
            Assert.That(LocalCopilotAccountCommands.FindCliExecutable(root), Is.Null,
                "A script or text renamed as copilot is not a native installation.");
            File.Copy(Environment.ProcessPath!, executable, overwrite: true);
            if (OperatingSystem.IsLinux())
            {
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                Assert.That(LocalCopilotAccountCommands.FindCliExecutable(root), Is.Null,
                    "Arquivo sem permissão de execução não é uma CLI disponível.");
                File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
            Assert.That(LocalCopilotAccountCommands.FindCliExecutable(root), Is.EqualTo(executable));
        }
        finally
        {
            if (root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    [Platform("Linux")]
    public void LinuxTerminalCandidateMustBeExecutable()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Permissões POSIX exigem Linux.");

        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KapibaraStudio.CopilotTerminalTest"));
        var root = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        var candidate = Path.Combine(root, "terminal");
        try
        {
            File.WriteAllText(candidate, "test placeholder");
            File.SetUnixFileMode(candidate, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.That(LocalCopilotAccountCommands.IsLinuxTerminalExecutable(candidate), Is.False);
            Assert.That(LocalCopilotAccountCommands.IsLinuxTerminalExecutable("relative-terminal"), Is.False);

            File.SetUnixFileMode(candidate, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            Assert.That(LocalCopilotAccountCommands.IsLinuxTerminalExecutable(candidate), Is.True);
        }
        finally
        {
            if (root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.Ordinal) && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
