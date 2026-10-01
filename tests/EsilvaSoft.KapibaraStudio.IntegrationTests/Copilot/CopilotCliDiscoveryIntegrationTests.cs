using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

[TestFixture, Category("Integration")]
internal sealed class CopilotCliDiscoveryIntegrationTests
{
    private static readonly string[] LoginArguments = ["login"];
    [Test]
    public void PackagedAccountCliIsAvailableWithoutAnInstalledCliOnPath()
    {
        Assert.That(LocalCopilotAccountCommands.FindCliExecutable(string.Empty), Is.Null);
        Assert.That(new LocalCopilotAccountCommands().IsCliInstalled(), Is.True);
        var cli = CopilotRuntimeSettings.BundledAccountCliPath();
        Assert.That(cli, Does.Contain(Path.Combine("runtimes", System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier, "copilot-cli")));
        Assert.That(cli, Is.Not.EqualTo(CopilotRuntimeSettings.BundledRuntimePath()),
            "O wrapper headless não é a CLI interativa de login.");
    }

    [Test]
    public void AccountProcessUsesOfficialHomeAndDoesNotInheritTokensOrRedirectLogin()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("A criação de terminal Linux exige homologação nativa.");
        var cli = CopilotRuntimeSettings.BundledAccountCliPath();
        var start = LocalCopilotAccountCommands.CreateStartInfo(cli, LoginArguments)!;
        Assert.Multiple(() =>
        {
            Assert.That(start.FileName, Is.EqualTo(cli));
            Assert.That(start.ArgumentList, Is.EqualTo(LoginArguments));
            Assert.That(start.UseShellExecute, Is.False);
            Assert.That(start.CreateNoWindow, Is.False);
            Assert.That(start.RedirectStandardOutput || start.RedirectStandardError || start.RedirectStandardInput, Is.False);
            Assert.That(start.Environment["COPILOT_HOME"], Is.EqualTo(CopilotRuntimeSettings.AccountClientOptions().BaseDirectory));
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
            Assert.That(LocalCopilotAccountCommands.FindCliExecutable(root), Is.EqualTo(executable));
        }
        finally
        {
            if (root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
