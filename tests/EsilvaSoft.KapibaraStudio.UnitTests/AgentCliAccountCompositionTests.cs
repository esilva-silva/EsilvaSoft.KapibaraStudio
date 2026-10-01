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
    private static DesktopAgentAccountManager Manager()
    {
        var handler = new App.ClaudeCodeAccountHandler(
            new ClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions(), new MissingClaudeCodeSystem()));
        return new DesktopAgentAccountManager([handler], [handler]);
    }

    [Test]
    public async Task MissingExecutableIsReportedWithoutStartingAnyProcess()
    {
        var manager = Manager();
        var status = await manager.CheckAsync(ClaudeCodeAgentProvider.Id, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(status.Install, Is.EqualTo(AgentAccountInstallState.NotFound));
            Assert.That(status.Auth, Is.EqualTo(AgentAccountAuthState.NotChecked));
            Assert.That(status.Version, Is.Null);
            Assert.That(status.SubscriptionTier, Is.Null);
        });

        var signIn = await manager.SignInAsync(ClaudeCodeAgentProvider.Id, CancellationToken.None);
        Assert.That(signIn.Outcome, Is.EqualTo(AgentAccountCommandOutcome.ExecutableUnavailable));
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
    public void FailedClaudeCliCommandRemainsFailedInTheDesktopAccountContract()
    {
        var mapped = App.ClaudeCodeAccountHandler.Map(
            new ClaudeCodeAccountCommandResult(ClaudeCodeAccountCommandState.CommandFailed, null));

        Assert.That(mapped.Outcome, Is.EqualTo(AgentAccountCommandOutcome.CommandFailed));
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

    [Test]
    public async Task LateProviderRefreshCannotOverwriteANewerSnapshot()
    {
        var provider = new DelayedProvider("delayed");
        var catalog = new DesktopAgentProviderCatalog(new AgentProviderCatalog([provider]));

        var staleRefresh = catalog.RefreshProviderAsync(provider.ProviderId, CancellationToken.None);
        await provider.FirstCallStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await catalog.RefreshProviderAsync(provider.ProviderId, CancellationToken.None);
        Assert.That(catalog.List().Single().IsAvailable, Is.True);

        provider.CompleteFirstCall(new AgentProviderStatus(false, AgentProviderAuthState.NotConfigured,
            AgentProviderCapabilities.None, unavailableCode: "NotLoggedIn"));
        await staleRefresh;

        Assert.That(catalog.List().Single().IsAvailable, Is.True,
            "A response from a refresh started before the current snapshot must be discarded.");
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

    private sealed class DelayedProvider(string id) : IAgentProvider
    {
        private readonly TaskCompletionSource<AgentProviderStatus> _first = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;

        public TaskCompletionSource FirstCallStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string ProviderId => id;
        public AgentProviderDescriptor Describe() => new(id, id, [AgentAuthenticationMethod.OfficialCliDelegated],
            new AgentProviderCapabilities { Chat = true, Streaming = true, UsesNetwork = true });

        public Task<AgentProviderStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Interlocked.Increment(ref _calls) == 1 ? StartFirstCall() : Task.FromResult(
                new AgentProviderStatus(true, AgentProviderAuthState.Configured,
                    new AgentProviderCapabilities { Chat = true, Streaming = true, UsesNetwork = true }, ["m"], "m"));

        public void CompleteFirstCall(AgentProviderStatus status) => _first.TrySetResult(status);

        private Task<AgentProviderStatus> StartFirstCall()
        {
            FirstCallStarted.TrySetResult();
            return _first.Task;
        }

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Nenhuma sessão nestes testes.");
    }
}
