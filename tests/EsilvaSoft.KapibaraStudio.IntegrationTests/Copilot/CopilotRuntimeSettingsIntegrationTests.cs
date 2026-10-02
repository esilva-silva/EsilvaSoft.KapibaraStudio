using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

/// <summary>Builds SDK account/session options without starting the CLI or modifying host files.</summary>
[TestFixture, Category("Integration")]
public sealed class CopilotRuntimeSettingsIntegrationTests
{
    [Test]
    public void AccountDiscoveryAndProductSessionsUseOfficialCliIdentityWithExplicitSessionStorage()
    {
        var cli = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "copilot"));
        var account = CopilotRuntimeSettings.AccountClientOptions(cli);
        var persistentSession = CopilotRuntimeSettings.SessionClientOptions(cliPath: cli);

        Assert.That(account.Mode, Is.EqualTo(CopilotClientMode.CopilotCli));
        Assert.That(account.UseLoggedInUser, Is.True);
        Assert.That(account.BaseDirectory, Is.EqualTo(persistentSession.BaseDirectory));
        Assert.That(account.BaseDirectory, Does.Contain(".copilot"));
        Assert.That(persistentSession.Mode, Is.EqualTo(CopilotClientMode.CopilotCli));
        Assert.That(persistentSession.UseLoggedInUser, Is.True);
        Assert.That(persistentSession.BaseDirectory, Does.Contain(".copilot"));
        Assert.That(persistentSession.SessionFs, Is.Null);
        Assert.That(account.Connection, Is.Not.Null);
    }
}
