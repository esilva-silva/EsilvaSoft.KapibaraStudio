using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture]
public sealed class CopilotAccountBoundaryTests
{
    [Test]
    public void AccountDiscoveryAndProductSessionsUseOfficialCliIdentityWithExplicitSessionStorage()
    {
        var account = CopilotRuntimeSettings.AccountClientOptions();
        var persistentSession = CopilotRuntimeSettings.SessionClientOptions();

        Assert.That(account.Mode, Is.EqualTo(CopilotClientMode.CopilotCli));
        Assert.That(account.UseLoggedInUser, Is.True);
        Assert.That(account.BaseDirectory, Is.EqualTo(persistentSession.BaseDirectory));
        Assert.That(account.BaseDirectory, Does.Contain(".copilot"));
        Assert.That(persistentSession.Mode, Is.EqualTo(CopilotClientMode.CopilotCli));
        Assert.That(persistentSession.UseLoggedInUser, Is.True);
        Assert.That(persistentSession.BaseDirectory, Does.Contain(".copilot"));
        Assert.That(persistentSession.SessionFs, Is.Null);
    }

    [TestCase(false, null, CopilotAccountState.NotLoggedIn)]
    [TestCase(true, "user", CopilotAccountState.Subscription)]
    [TestCase(true, "env", CopilotAccountState.OtherAuthentication)]
    [TestCase(true, "gh-cli", CopilotAccountState.OtherAuthentication)]
    [TestCase(true, "api-key", CopilotAccountState.OtherAuthentication)]
    [TestCase(true, "token", CopilotAccountState.OtherAuthentication)]
    [TestCase(true, "hmac", CopilotAccountState.OtherAuthentication)]
    [TestCase(true, "unknown", CopilotAccountState.OtherAuthentication)]
    public void OnlyExplicitUserOAuthIsClassifiedAsSubscription(bool authenticated, string? authType,
        CopilotAccountState expected)
    {
        var status = CopilotSubscriptionAgentProvider.ClassifyAuthentication(authenticated, authType);

        Assert.That(status.State, Is.EqualTo(expected));
    }

    [Test]
    public void ChildEnvironmentDropsCredentialAndProviderOverrideVariables()
    {
        var source = new Dictionary<string, string?>
        {
            ["PATH"] = "safe-path",
            ["HOME"] = "safe-home",
            ["GITHUB_TOKEN"] = "must-not-pass",
            ["GH_TOKEN"] = "must-not-pass",
            ["COPILOT_CLI_PATH"] = "must-not-pass",
            ["COPILOT_HOME"] = "must-not-pass",
            ["ANTHROPIC_API_KEY"] = "must-not-pass",
            ["OPENAI_API_KEY"] = "must-not-pass",
        };

        var child = CopilotRuntimeSettings.BuildChildEnvironment(source);

        Assert.That(child.GetValueOrDefault("PATH"), Is.EqualTo("safe-path"));
        Assert.That(child.GetValueOrDefault("HOME"), Is.EqualTo(OperatingSystem.IsWindows() ? null : "safe-home"));
        Assert.That(child.Keys, Does.Not.Contain("GITHUB_TOKEN"));
        Assert.That(child.Keys, Does.Not.Contain("GH_TOKEN"));
        Assert.That(child.Keys, Does.Not.Contain("COPILOT_CLI_PATH"));
        Assert.That(child.Keys, Does.Not.Contain("COPILOT_HOME"));
        Assert.That(child.Keys, Does.Not.Contain("ANTHROPIC_API_KEY"));
        Assert.That(child.Keys, Does.Not.Contain("OPENAI_API_KEY"));
    }

    [Test]
    public void CliDetectionUsesOnlyAbsoluteExecutableCandidates()
    {
        var root = Path.Combine(Path.GetTempPath(), "KapibaraStudio.CopilotCliTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, OperatingSystem.IsWindows() ? "copilot.exe" : "copilot");
        try
        {
            Assert.That(CopilotAccountCommands.FindCliExecutable(root), Is.Null);
            Assert.That(CopilotAccountCommands.FindCliExecutable("relative-path"), Is.Null);
            File.WriteAllText(executable, "test placeholder");

            Assert.That(CopilotAccountCommands.FindCliExecutable(root), Is.EqualTo(executable));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task PreToolHookAllowsOnlyToolsThatWillPassThroughTheProductRegistry()
    {
        var allowed = new HashSet<string>(["get_workspace_context", "propose_file_edit"], StringComparer.Ordinal);

        var productTool = await CopilotSubscriptionAgentSession.DecideToolPermissionAsync("get_workspace_context", allowed);
        var nativeTool = await CopilotSubscriptionAgentSession.DecideToolPermissionAsync("bash", allowed);
        var unknownTool = await CopilotSubscriptionAgentSession.DecideToolPermissionAsync("unregistered", allowed);
        var missingName = await CopilotSubscriptionAgentSession.DecideToolPermissionAsync(null, allowed);

        Assert.That(productTool?.PermissionDecision, Is.EqualTo("allow"));
        Assert.That(nativeTool?.PermissionDecision, Is.EqualTo("deny"));
        Assert.That(unknownTool?.PermissionDecision, Is.EqualTo("deny"));
        Assert.That(missingName?.PermissionDecision, Is.EqualTo("deny"));
    }
}
