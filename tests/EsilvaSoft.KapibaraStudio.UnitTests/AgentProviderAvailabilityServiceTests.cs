using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AgentProviderAvailabilityServiceTests
{
    private const string ProviderId = "copilot-test";

    [Test]
    public async Task ConcurrentStartupAndForcedRetryShareOneAccountAndCatalogCheck()
    {
        var catalog = new Catalog(ProviderId);
        var accounts = new Accounts(new AgentAccountCheckPolicy(true, true));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        accounts.Check = async (_, token) =>
        {
            entered.SetResult();
            await release.Task.WaitAsync(token);
            return new AgentAccountStatus(AgentAccountInstallState.Installed, "1.0", AgentAccountAuthState.Subscription);
        };
        var service = new AgentProviderAvailabilityService(catalog, accounts: accounts);

        var startup = service.CheckAsync(ProviderId);
        await entered.Task;
        var retry = service.CheckAsync(ProviderId, force: true);
        release.SetResult();
        var results = await Task.WhenAll(startup, retry);

        Assert.Multiple(() =>
        {
            Assert.That(accounts.CheckCalls, Is.EqualTo(1));
            Assert.That(catalog.RefreshCalls, Is.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(results[1]));
            Assert.That(service.CurrentAccountStatus(ProviderId)?.Auth, Is.EqualTo(AgentAccountAuthState.Subscription));
        });
    }

    [Test]
    public async Task SavedProviderStartupChecksOnlyRegisteredEligibleSelection()
    {
        var catalog = new Catalog(ProviderId, "other-provider");
        var accounts = new Accounts(new AgentAccountCheckPolicy(true, true));
        var service = new AgentProviderAvailabilityService(catalog, accounts: accounts);

        var skippedUnknown = await service.InitializeSavedProviderAsync("not-registered");
        var initialized = await service.InitializeSavedProviderAsync(ProviderId);
        var skippedIneligible = await service.InitializeSavedProviderAsync("other-provider");

        Assert.Multiple(() =>
        {
            Assert.That(skippedUnknown, Is.Null);
            Assert.That(initialized?.State, Is.EqualTo(AgentProviderAvailabilityState.Available));
            Assert.That(skippedIneligible, Is.Null);
            Assert.That(accounts.CheckedProviders, Is.EqualTo(new[] { ProviderId }));
            Assert.That(catalog.RefreshedProviders, Is.EqualTo(new[] { ProviderId }));
        });
    }

    [Test]
    public async Task CollapsedWorkspaceStartupUsesAvailabilityWithoutComposingChatServicesAndForceBypassesCache()
    {
        var catalog = new Catalog(ProviderId);
        var accounts = new Accounts(new AgentAccountCheckPolicy(true, true));
        var availability = new AgentProviderAvailabilityService(catalog, accounts: accounts);
        var serviceCompositionCalls = 0;
        var factory = new AgentChatServicesFactory(() =>
        {
            serviceCompositionCalls++;
            return AgentChatServices.Unavailable;
        }, availability);

        await factory.InitializeSavedProviderAsync(ProviderId);
        await availability.CheckAsync(ProviderId, force: true);

        Assert.Multiple(() =>
        {
            Assert.That(serviceCompositionCalls, Is.Zero, "A collapsed panel must not resolve chat/runtime/tool services.");
            Assert.That(accounts.CheckedProviders, Is.EqualTo(new[] { ProviderId, ProviderId }),
                "A manual forced check bypasses the successful startup cache.");
            Assert.That(catalog.RefreshCalls, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task FailedAccountCheckClearsPreviousSignedInSnapshotWithoutReportingSignedOut()
    {
        var catalog = new Catalog(ProviderId);
        var accounts = new Accounts(new AgentAccountCheckPolicy(true, true));
        var service = new AgentProviderAvailabilityService(catalog, accounts: accounts);

        await service.CheckAsync(ProviderId);
        accounts.Check = (_, _) => Task.FromException<AgentAccountStatus>(new IOException("simulated check failure"));
        var failed = await service.CheckAsync(ProviderId, force: true);

        Assert.Multiple(() =>
        {
            Assert.That(failed.State, Is.EqualTo(AgentProviderAvailabilityState.Failed));
            Assert.That(failed.Code, Is.EqualTo("StatusFailed"));
            Assert.That(service.CurrentAccountStatus(ProviderId), Is.Null,
                "A failed check must not expose the previous subscription state or imply SignedOut.");
        });
    }

    [Test]
    public async Task TimedOutCheckClearsOldStatusAndForcedRetryRecovers()
    {
        var catalog = new Catalog(ProviderId);
        var accounts = new Accounts(new AgentAccountCheckPolicy(true, true));
        var service = new AgentProviderAvailabilityService(catalog, accounts: accounts)
        {
            Timeout = TimeSpan.FromMilliseconds(40),
        };

        await service.CheckAsync(ProviderId);
        accounts.Check = async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        };
        var timedOut = await service.CheckAsync(ProviderId, force: true);
        var staleStatus = service.CurrentAccountStatus(ProviderId);

        accounts.Check = (_, _) => Task.FromResult(new AgentAccountStatus(
            AgentAccountInstallState.Installed, "1.1", AgentAccountAuthState.Subscription));
        var recovered = await service.CheckAsync(ProviderId, force: true);

        Assert.Multiple(() =>
        {
            Assert.That(timedOut.State, Is.EqualTo(AgentProviderAvailabilityState.Failed));
            Assert.That(timedOut.TimedOut, Is.True);
            Assert.That(staleStatus, Is.Null, "Timeout cannot leave an earlier signed-in snapshot visible.");
            Assert.That(recovered.State, Is.EqualTo(AgentProviderAvailabilityState.Available));
            Assert.That(service.CurrentAccountStatus(ProviderId)?.Auth, Is.EqualTo(AgentAccountAuthState.Subscription));
            Assert.That(accounts.CheckCalls, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task AutomaticCheckSkipsCredentialPromptRiskAndCatalogProbeButExplicitRetryRunsIt()
    {
        var catalog = new Catalog(ProviderId);
        var accounts = new Accounts(new AgentAccountCheckPolicy(false, true, MayShowCredentialDialog: true));
        var service = new AgentProviderAvailabilityService(catalog, accounts: accounts);

        var automatic = await service.CheckAsync(ProviderId);
        var explicitRetry = await service.CheckAsync(ProviderId, force: true);

        Assert.Multiple(() =>
        {
            Assert.That(automatic.State, Is.EqualTo(AgentProviderAvailabilityState.NotChecked));
            Assert.That(automatic.Code, Is.EqualTo("ExplicitCheckRequired"));
            Assert.That(accounts.CheckCalls, Is.EqualTo(1));
            Assert.That(catalog.RefreshCalls, Is.EqualTo(1), "Automatic checks skip provider status too, avoiding keyring/AppServer access.");
            Assert.That(explicitRetry.State, Is.EqualTo(AgentProviderAvailabilityState.Available));
        });
    }

    [Test]
    public async Task PostLoginRefreshWaitsForPreCommandFlightThenStartsFreshCheck()
    {
        var catalog = new Catalog(ProviderId);
        var accounts = new Accounts(new AgentAccountCheckPolicy(true, true));
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        accounts.Check = async (_, token) =>
        {
            if (accounts.CheckCalls == 1)
            {
                firstEntered.SetResult();
                await releaseFirst.Task.WaitAsync(token);
                return new AgentAccountStatus(AgentAccountInstallState.Installed, "1.0", AgentAccountAuthState.SignedOut);
            }

            return new AgentAccountStatus(AgentAccountInstallState.Installed, "1.0", AgentAccountAuthState.Subscription);
        };
        var service = new AgentProviderAvailabilityService(catalog, accounts: accounts);

        var beforeLogin = service.CheckAsync(ProviderId);
        await firstEntered.Task;
        var afterLogin = service.RefreshAfterAccountCommandAsync(ProviderId);
        releaseFirst.SetResult();
        var refreshed = await afterLogin;
        await beforeLogin;

        Assert.Multiple(() =>
        {
            Assert.That(accounts.CheckCalls, Is.EqualTo(2));
            Assert.That(catalog.RefreshCalls, Is.EqualTo(2));
            Assert.That(refreshed.State, Is.EqualTo(AgentProviderAvailabilityState.Available));
            Assert.That(service.CurrentAccountStatus(ProviderId)?.Auth, Is.EqualTo(AgentAccountAuthState.Subscription));
        });
    }

    [Test]
    public async Task StopCancelsSharedCheckBeforeProvidersAreDisposed()
    {
        var catalog = new Catalog(ProviderId);
        var accounts = new Accounts(new AgentAccountCheckPolicy(true, true));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        accounts.Check = async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("unreachable");
        };
        var service = new AgentProviderAvailabilityService(catalog, accounts: accounts);

        var running = service.CheckAsync(ProviderId);
        await entered.Task;
        await service.StopAsync(TimeSpan.FromSeconds(1));
        var stopped = await running;
        var later = await service.CheckAsync(ProviderId, force: true);

        Assert.Multiple(() =>
        {
            Assert.That(stopped.State, Is.EqualTo(AgentProviderAvailabilityState.NotChecked));
            Assert.That(stopped.Code, Is.EqualTo("ApplicationStopping"));
            Assert.That(later.State, Is.EqualTo(AgentProviderAvailabilityState.NotChecked));
            Assert.That(accounts.CheckCalls, Is.EqualTo(1));
            Assert.That(catalog.RefreshCalls, Is.Zero);
        });
    }

    [TestCase("CopilotCliNotInstalled", AgentProviderAvailabilityState.CliMissing)]
    [TestCase("CopilotLoginRequired", AgentProviderAvailabilityState.NotConnected)]
    [TestCase("CopilotSubscriptionRequired", AgentProviderAvailabilityState.NotConnected)]
    public async Task CopilotSpecificAccountCodesMapToActionableStates(string code, AgentProviderAvailabilityState expected)
    {
        var catalog = new Catalog(ProviderId) { NextUnavailableCode = code };
        var accounts = new Accounts(new AgentAccountCheckPolicy(true, true));
        var service = new AgentProviderAvailabilityService(catalog, accounts: accounts);

        var result = await service.CheckAsync(ProviderId, force: true);

        Assert.That(result.State, Is.EqualTo(expected));
    }

    private sealed class Catalog(params string[] ids) : IAgentProviderCatalog
    {
        private readonly IReadOnlyList<AgentProviderPresentation> _items = ids.Select(id => new AgentProviderPresentation(
            id, id, AgentDataDestinationKind.External, true, ["model"], [AgentAuthenticationMethod.OfficialCliDelegated],
            AgentProviderAuthState.Configured)).ToArray();

        public int RefreshCalls { get; private set; }
        public List<string> RefreshedProviders { get; } = [];
        public string? NextUnavailableCode { get; init; }
        public IReadOnlyList<AgentProviderPresentation> List() => _items;
        public Task RefreshAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RefreshProviderAsync(string providerId, CancellationToken cancellationToken)
        {
            RefreshCalls++;
            RefreshedProviders.Add(providerId);
            if (NextUnavailableCode is { } code && _items.FirstOrDefault(item => item.ProviderId == providerId) is { } existing)
            {
                var index = Array.FindIndex((AgentProviderPresentation[])_items, item => item.ProviderId == providerId);
                ((AgentProviderPresentation[])_items)[index] = existing with
                {
                    IsAvailable = false,
                    UnavailableReason = code,
                };
            }
            return Task.CompletedTask;
        }
    }

    private sealed class Accounts(AgentAccountCheckPolicy policy) : IAgentAccountManager
    {
        public int CheckCalls { get; private set; }
        public List<string> CheckedProviders { get; } = [];
        public Func<string, CancellationToken, Task<AgentAccountStatus>> Check { get; set; } =
            (_, _) => Task.FromResult(new AgentAccountStatus(AgentAccountInstallState.Installed, "1.0", AgentAccountAuthState.Subscription));

        public AgentAccountCheckPolicy DescribeCheckPolicy(string providerId) =>
            providerId == ProviderId ? policy : AgentAccountCheckPolicy.Unsupported;

        public Task<AgentAccountStatus> CheckAsync(string providerId, CancellationToken cancellationToken)
        {
            CheckCalls++;
            CheckedProviders.Add(providerId);
            return Check(providerId, cancellationToken);
        }

        public Task<AgentAccountCommandResult> SignInAsync(string providerId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AgentAccountCommandResult> SignOutAsync(string providerId, bool userConfirmedGlobalSignOut,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
