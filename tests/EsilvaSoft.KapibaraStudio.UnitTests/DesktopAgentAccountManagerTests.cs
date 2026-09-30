using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class DesktopAgentAccountManagerTests
{
    [Test]
    public async Task RegisteredIdsDispatchIndependentlyAndPresentationNeverProbesAccounts()
    {
        var first = new AccountHandler("subscription-a");
        var second = new AccountHandler("subscription-b");
        var manager = new DesktopAgentAccountManager([first, second], [first, second]);

        Assert.That(manager.Describe(first.ProviderId), Is.SameAs(first.Profile));
        Assert.That(manager.DescribeCheckPolicy(second.ProviderId), Is.SameAs(second.CheckPolicy));
        Assert.That(manager.DescribeReadScope(first.ProviderId, null), Is.EqualTo(AgentCliReadScope.None));
        Assert.That(first.Checks + second.Checks, Is.Zero);

        var status = await manager.CheckAsync(second.ProviderId, CancellationToken.None);
        var command = await manager.SignInAsync(first.ProviderId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.Checks, Is.Zero);
            Assert.That(second.Checks, Is.EqualTo(1));
            Assert.That(first.SignIns, Is.EqualTo(1));
            Assert.That(second.SignIns, Is.Zero);
            Assert.That(status.Auth, Is.EqualTo(AgentAccountAuthState.Subscription));
            Assert.That(status.CheckedAtUtc, Is.Not.Null);
            Assert.That(command.Status!.CheckedAtUtc, Is.Not.Null);
        });
    }

    [Test]
    public async Task GlobalSignOutReachesHandlerOnlyAfterExplicitConfirmation()
    {
        var handler = new AccountHandler("subscription-a");
        var manager = new DesktopAgentAccountManager([handler], [handler]);
        Assert.ThrowsAsync<InvalidOperationException>(() => manager.SignOutAsync(handler.ProviderId, false, CancellationToken.None));
        Assert.That(handler.SignOuts, Is.Zero);

        var result = await manager.SignOutAsync(handler.ProviderId, true, CancellationToken.None);
        Assert.That(handler.SignOuts, Is.EqualTo(1));
        Assert.That(result.Status!.Auth, Is.EqualTo(AgentAccountAuthState.SignedOut));
    }

    [Test]
    public void UnknownAndCancelledRequestsDoNotProbeRegisteredAccounts()
    {
        var handler = new AccountHandler("subscription-a");
        var manager = new DesktopAgentAccountManager([handler], [handler]);
        Assert.That(manager.Describe("api-mode"), Is.Null);
        Assert.That(manager.DescribeCheckPolicy("api-mode").AllowsAutomaticCheck, Is.False);
        Assert.That(manager.DescribeReadScope("api-mode", null), Is.EqualTo(AgentCliReadScope.None));
        Assert.ThrowsAsync<ArgumentException>(() => manager.CheckAsync("api-mode", CancellationToken.None));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.CatchAsync<OperationCanceledException>(() => manager.CheckAsync(handler.ProviderId, cancelled.Token));
        Assert.That(handler.Checks, Is.Zero);
    }

    private sealed class AccountHandler(string providerId) : IAgentAccountHandler, IAgentCliAccountPresentationHandler
    {
        public string ProviderId => providerId;
        public AgentAccountCheckPolicy CheckPolicy { get; } = new(true, false);
        public AgentCliProviderProfile Profile { get; } = new("Fixture CLI", "Fixture", "fixture login", "runtime-managed", "runtime-managed");
        public int Checks { get; private set; }
        public int SignIns { get; private set; }
        public int SignOuts { get; private set; }
        public AgentCliReadScope DescribeReadScope(string? candidateWorkspace) => AgentCliReadScope.None;
        public Task<AgentAccountStatus> CheckAsync(CancellationToken cancellationToken)
        {
            Checks++;
            return Task.FromResult(Status(AgentAccountAuthState.Subscription));
        }
        public Task<AgentAccountCommandResult> SignInAsync(CancellationToken cancellationToken)
        {
            SignIns++;
            return Task.FromResult(new AgentAccountCommandResult(AgentAccountCommandOutcome.Completed, Status(AgentAccountAuthState.Subscription)));
        }
        public Task<AgentAccountCommandResult> SignOutAsync(bool userConfirmedGlobalSignOut, CancellationToken cancellationToken)
        {
            Assert.That(userConfirmedGlobalSignOut, Is.True);
            SignOuts++;
            return Task.FromResult(new AgentAccountCommandResult(AgentAccountCommandOutcome.Completed, Status(AgentAccountAuthState.SignedOut)));
        }
        private static AgentAccountStatus Status(AgentAccountAuthState auth) => new(AgentAccountInstallState.Installed, null, auth);
    }
}
