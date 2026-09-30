using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

[TestFixture, Category("Integration")]
internal sealed class CopilotCliDiscoveryIntegrationTests
{
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
