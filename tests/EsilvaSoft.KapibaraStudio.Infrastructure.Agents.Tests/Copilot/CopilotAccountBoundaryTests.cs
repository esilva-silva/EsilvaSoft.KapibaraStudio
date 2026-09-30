using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture]
[Category("Unit")]
public sealed class CopilotAccountBoundaryTests
{
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

    [TestCase(true)]
    [TestCase(false)]
    public void ChildEnvironmentDropsCredentialAndProviderOverrideVariables(bool isWindows)
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

        var child = CopilotRuntimeSettings.BuildChildEnvironment(source, new TestPlatform(isWindows));

        Assert.That(child.GetValueOrDefault("PATH"), Is.EqualTo("safe-path"));
        Assert.That(child.GetValueOrDefault("HOME"), Is.EqualTo(isWindows ? null : "safe-home"));
        Assert.That(child.Keys, Does.Not.Contain("GITHUB_TOKEN"));
        Assert.That(child.Keys, Does.Not.Contain("GH_TOKEN"));
        Assert.That(child.Keys, Does.Not.Contain("COPILOT_CLI_PATH"));
        Assert.That(child.Keys, Does.Not.Contain("COPILOT_HOME"));
        Assert.That(child.Keys, Does.Not.Contain("ANTHROPIC_API_KEY"));
        Assert.That(child.Keys, Does.Not.Contain("OPENAI_API_KEY"));
    }

    [TestCase(true, "safe-path")]
    [TestCase(false, null)]
    public void ChildEnvironmentHonorsTargetPlatformVariableNameCase(bool isWindows, string? expectedPath)
    {
        var source = new Dictionary<string, string?> { ["path"] = "safe-path", ["gh_token"] = "must-not-pass" };

        var child = CopilotRuntimeSettings.BuildChildEnvironment(source, new TestPlatform(isWindows));

        Assert.That(child.GetValueOrDefault("PATH"), Is.EqualTo(expectedPath));
        Assert.That(child.Keys, Does.Not.Contain("gh_token"));
    }

    private sealed record TestPlatform(bool IsWindows) : IHostPlatformSnapshot
    {
        public bool IsLinux => !IsWindows;
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
