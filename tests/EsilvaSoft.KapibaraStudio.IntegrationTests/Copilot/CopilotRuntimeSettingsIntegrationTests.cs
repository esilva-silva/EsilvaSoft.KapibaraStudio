using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

/// <summary>Reads the installed runtime and host paths without starting a client or modifying host files.</summary>
[TestFixture, Category("Integration")]
public sealed class CopilotRuntimeSettingsIntegrationTests
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
}
