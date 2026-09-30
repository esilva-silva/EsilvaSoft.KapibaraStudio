using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Composition-root adapter of the subscription mode (P7-CL5-02) and the per-provider catalog refresh. The provider is
/// supplied an in-memory resource adapter; no process or filesystem access occurs in these tests.
/// </summary>
[TestFixture]
[Category("Unit")]
public sealed class AgentCliAccountCompositionTests
{
    private static App.ClaudeCodeCliAccountManager Manager() =>
        new(new ClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions(), new MissingClaudeCodeSystem()));

    [Test]
    public async Task MissingExecutableIsReportedWithoutStartingAnyProcess()
    {
        var manager = Manager();
        var status = await manager.CheckAsync(ClaudeCodeAgentProvider.Id, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(status.Install, Is.EqualTo(AgentCliInstallState.NotFound));
            Assert.That(status.Auth, Is.EqualTo(AgentCliAuthState.NotChecked));
            Assert.That(status.Version, Is.Null);
            Assert.That(status.SubscriptionTier, Is.Null);
        });

        var signIn = await manager.SignInAsync(ClaudeCodeAgentProvider.Id, CancellationToken.None);
        Assert.That(signIn.Outcome, Is.EqualTo(AgentCliCommandOutcome.ExecutableUnavailable));
    }

    [Test]
    public void SignOutWithoutConfirmationIsRefusedAndOtherProvidersAreNotManaged()
    {
        var manager = Manager();
        Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SignOutAsync(ClaudeCodeAgentProvider.Id, userConfirmedGlobalSignOut: false, CancellationToken.None));
        Assert.ThrowsAsync<ArgumentException>(() => manager.CheckAsync("claude", CancellationToken.None));
        Assert.Multiple(() =>
        {
            Assert.That(manager.Describe("claude"), Is.Null, "O modo API não tem conta por CLI.");
            Assert.That(manager.Describe(ClaudeCodeAgentProvider.Id)!.CliName, Is.EqualTo("Claude Code"));
            Assert.That(manager.Describe(ClaudeCodeAgentProvider.Id)!.SignInCommand, Is.EqualTo("claude auth login"));
        });
    }

    [Test]
    public void ReadScopeIsTheProvidersOwnWorkingDirectoryDecision()
    {
        var manager = Manager();
        var system = new MissingClaudeCodeSystem();
        var home = system.HomeDirectory;
        var accepted = system.AcceptedWorkspace;
        {
            var none = manager.DescribeReadScope(ClaudeCodeAgentProvider.Id, null);
            var ok = manager.DescribeReadScope(ClaudeCodeAgentProvider.Id, accepted);
            var profile = manager.DescribeReadScope(ClaudeCodeAgentProvider.Id, home);
            var missing = manager.DescribeReadScope(ClaudeCodeAgentProvider.Id, Path.Combine(accepted, "nao-existe"));
            var root = manager.DescribeReadScope(ClaudeCodeAgentProvider.Id, Path.GetPathRoot(accepted));
            Assert.Multiple(() =>
            {
                Assert.That(none.Rejection, Is.EqualTo(AgentCliReadScopeRejection.NotProvided));
                Assert.That(none.UsesWorkspace, Is.False);
                Assert.That(none.ReadsRequireApproval, Is.True);
                Assert.That(ok.UsesWorkspace, Is.True);
                Assert.That(ok.Rejection, Is.EqualTo(AgentCliReadScopeRejection.None));
                Assert.That(profile.Rejection, Is.EqualTo(AgentCliReadScopeRejection.UserProfile));
                Assert.That(profile.EffectiveDirectory, Is.Not.EqualTo(home), "Recusada usa a pasta dedicada.");
                Assert.That(missing.Rejection, Is.EqualTo(AgentCliReadScopeRejection.NotFound));
                Assert.That(root.Rejection, Is.EqualTo(AgentCliReadScopeRejection.VolumeRoot));
                Assert.That(manager.DescribeReadScope("claude", accepted), Is.SameAs(AgentCliReadScope.None));
            });
        }
    }

    [Test]
    public async Task ProviderRefreshChecksOnlyTheSelectedProviderAndCarriesTheFamilyLabel()
    {
        var a = new CountingProvider("cli-sub", AgentAuthenticationMethod.OfficialCliDelegated);
        var b = new CountingProvider("api-key", AgentAuthenticationMethod.ApiKey);
        var catalog = new DesktopAgentProviderCatalog(new AgentProviderCatalog([a, b]),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["cli-sub"] = "Claude", ["api-key"] = "Claude" });
        Assert.That(catalog.List().Select(p => p.FamilyName), Is.All.EqualTo("Claude"));

        await catalog.RefreshProviderAsync("cli-sub", CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(a.StatusCalls, Is.EqualTo(1));
            Assert.That(b.StatusCalls, Is.Zero, "Testar um provider não consulta os demais (cofre).");
            Assert.That(catalog.List().Single(p => p.ProviderId == "cli-sub").IsAvailable, Is.True);
            Assert.That(catalog.List().Single(p => p.ProviderId == "api-key").IsAvailable, Is.False);
        });
    }

    [Test]
    public async Task ExperimentalProviderMarkerIsPreservedByCatalogRefresh()
    {
        var codex = new CountingProvider("codex-subscription", AgentAuthenticationMethod.OfficialCliDelegated);
        var catalog = new DesktopAgentProviderCatalog(new AgentProviderCatalog([codex]),
            new Dictionary<string, string>(StringComparer.Ordinal) { [codex.ProviderId] = "OpenAI" },
            new HashSet<string>(StringComparer.Ordinal) { codex.ProviderId });

        Assert.That(catalog.List().Single().IsExperimental, Is.True);
        await catalog.RefreshProviderAsync(codex.ProviderId, CancellationToken.None);
        Assert.That(catalog.List().Single().IsExperimental, Is.True);
    }

    private sealed class CountingProvider(string id, AgentAuthenticationMethod method) : IAgentProvider
    {
        public int StatusCalls { get; private set; }

        public string ProviderId => id;

        public AgentProviderDescriptor Describe() =>
            new(id, id, [method], new AgentProviderCapabilities { Chat = true, Streaming = true, UsesNetwork = true });

        public Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken)
        {
            StatusCalls++;
            return Task.FromResult(new AgentProviderStatus(true, AgentProviderAuthState.Configured,
                new AgentProviderCapabilities { Chat = true, Streaming = true, UsesNetwork = true }, ["m"], "m"));
        }

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Nenhuma sessão nestes testes.");
    }
}
