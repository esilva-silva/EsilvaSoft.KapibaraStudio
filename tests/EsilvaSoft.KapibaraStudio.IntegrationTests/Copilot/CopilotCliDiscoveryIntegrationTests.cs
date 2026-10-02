using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

[TestFixture, Category("Integration")]
internal sealed class CopilotCliDiscoveryIntegrationTests
{
    private static readonly string[] LoginArguments = ["login"];
    [Test]
    public void AccountCliRequiresUserInstalledNativeExecutableOnPath()
    {
        Assert.That(LocalCopilotAccountCommands.FindCliExecutable(string.Empty), Is.Null);
        Assert.That(LocalCopilotAccountCommands.FindInstalledCliExecutable(), Is.EqualTo(
            LocalCopilotAccountCommands.FindCliExecutable(Environment.GetEnvironmentVariable("PATH"))));
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
