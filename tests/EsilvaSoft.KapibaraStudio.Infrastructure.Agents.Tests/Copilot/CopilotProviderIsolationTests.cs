using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture, Category("Unit")]
internal sealed class CopilotProviderIsolationTests
{
    private sealed class NoTools : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => null;
        public string? GetOutputSchemaJson(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Resources : ICopilotRuntimeResources
    {
        public MemoryCopilotRuntime Account { get; } = new();
        public MemoryCopilotRuntime Session { get; } = new();
        public MemoryCopilotRuntime Cleanup { get; } = new();
        public MemoryCopilotSessionStorage Volatile { get; } = new(false);
        public MemoryCopilotSessionStorage Persistent { get; } = new(true);
        public ICopilotSessionFsStore VolatileStore => Volatile;
        public ICopilotSessionFsStore PersistentStore => Persistent;
        public int AccountCalls;
        public int SessionCalls;
        public int CleanupCalls;
        public int Disposals;
        public bool LastPersistent;
        public string? LastWorkingDirectory;
        public ICopilotRuntimeClient CreateAccountClient() { AccountCalls++; return Account; }
        public ICopilotRuntimeClient CreateSessionClient(string? workingDirectory, bool persistent)
        {
            SessionCalls++; LastPersistent = persistent; LastWorkingDirectory = workingDirectory; return Session;
        }
        public ICopilotRuntimeClient CreateCleanupClient(bool volatileSession) { CleanupCalls++; return Cleanup; }
        public void Dispose() => Disposals++;
    }

    private sealed class Commands : ICopilotAccountCommands
    {
        public bool Installed = true;
        public int Probes;
        public CopilotAccountCommandState Result = CopilotAccountCommandState.Completed;
        public readonly List<string> Actions = [];
        public bool IsCliInstalled() { Probes++; return Installed; }
        public Task<CopilotAccountCommandState> RunVisibleAsync(string action, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); Actions.Add(action); return Task.FromResult(Result);
        }
    }

    private sealed class ResourceFactory(Resources resources) : ICopilotRuntimeResourcesFactory
    {
        public int Creations;
        public ICopilotRuntimeResources Create() { Creations++; return resources; }
    }

    [Test]
    public async Task PassiveCatalogDoesNotProbeCliOrCreateSdkClient()
    {
        var resources = new Resources(); var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        Assert.That(await provider.GetStatusAsync(CancellationToken.None), Is.SameAs(AgentProviderStatus.NotReported));
        Assert.That(commands.Probes, Is.Zero);
        Assert.That(resources.AccountCalls, Is.Zero);
        Assert.That(resources.SessionCalls, Is.Zero);
    }

    [Test]
    public async Task MissingCliPreventsAllClientCreation()
    {
        var resources = new Resources(); var commands = new Commands { Installed = false };
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        Assert.That((await provider.CheckAccountAndModelsAsync()).State, Is.EqualTo(CopilotAccountState.CliNotInstalled));
        Assert.That(resources.AccountCalls, Is.Zero);
    }

    [TestCase(false, "user", CopilotAccountState.NotLoggedIn)]
    [TestCase(true, "token", CopilotAccountState.OtherAuthentication)]
    public async Task AccountCheckUsesInjectedClientAndNeverCreatesSession(bool authenticated, string type, CopilotAccountState expected)
    {
        var resources = new Resources();
        resources.Account.Authentication = new(authenticated, type);
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, new Commands());
        Assert.That((await provider.CheckAccountAndModelsAsync()).State, Is.EqualTo(expected));
        Assert.That(resources.Account.Disposals, Is.EqualTo(1));
        Assert.That(resources.SessionCalls, Is.Zero);
        Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.False);
    }

    [Test]
    public async Task AccountModelAndReservationGuardsPrecedeSessionClientCreation()
    {
        var resources = new Resources();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, new Commands());
        var options = new AgentSessionOptions(provider.ProviderId, "synthetic-model");
        Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.CreateSessionAsync(options, CancellationToken.None));
        await provider.CheckAccountAndModelsAsync();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.CreateSessionAsync(options, CancellationToken.None));
        Assert.That(resources.SessionCalls, Is.Zero);
        await using var session = await provider.CreateSessionAsync(options with
        {
            PersistProviderSession = false, WorkingDirectory = "synthetic-captured-directory"
        }, CancellationToken.None);
        Assert.That(resources.SessionCalls, Is.EqualTo(1));
        Assert.That(resources.LastPersistent, Is.False);
        Assert.That(resources.LastWorkingDirectory, Is.EqualTo("synthetic-captured-directory"));
        Assert.That(resources.Session.Starts, Is.Zero, "Creating a host session alone must not start a runtime.");
    }

    [Test]
    public async Task LogoutRequiresConfirmationBeforeCallingCommands()
    {
        var resources = new Resources(); var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.LogoutAsync(false));
        Assert.That(commands.Actions, Is.Empty);
        Assert.That(resources.AccountCalls, Is.Zero);
    }

    [TestCase(CopilotAccountCommandState.CommandFailed, 0)]
    [TestCase(CopilotAccountCommandState.Completed, 1)]
    public async Task LoginRefreshesAccountOnlyAfterCommandCompletes(CopilotAccountCommandState state, int refreshes)
    {
        var resources = new Resources(); var commands = new Commands { Result = state };
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        Assert.That((await provider.LoginAsync()).State, Is.EqualTo(state));
        Assert.That(commands.Actions.Single(), Is.EqualTo("login"));
        Assert.That(resources.AccountCalls, Is.EqualTo(refreshes));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task NativeCleanupFailureKeepsLocalStateAndCanRetry(bool volatileSession)
    {
        var resources = new Resources();
        var store = volatileSession ? resources.Volatile : resources.Persistent;
        store.ReserveSession("synthetic-id");
        resources.Cleanup.SessionExists = true;
        resources.Cleanup.DeleteFailure = new IOException("synthetic deletion failure");
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, new Commands());
        Assert.ThrowsAsync<IOException>(async () => await provider.DeleteProviderSessionAsync("synthetic-id", CancellationToken.None));
        Assert.That(store.ContainsSession("synthetic-id"), Is.True);
        resources.Cleanup.DeleteFailure = null;
        await provider.DeleteProviderSessionAsync("synthetic-id", CancellationToken.None);
        Assert.That(store.ContainsSession("synthetic-id"), Is.False);
        Assert.That(resources.CleanupCalls, Is.EqualTo(2));
        Assert.That(resources.AccountCalls, Is.Zero);
    }

    [Test]
    public void CompositionPreservesInjectedPortsAndProviderOwnsResourcesOnce()
    {
        var resources = new Resources(); var commands = new Commands(); var factory = new ResourceFactory(resources);
        var services = new ServiceCollection();
        services.AddSingleton<IAgentToolRegistry>(new NoTools());
        services.AddSingleton<ICopilotAccountCommands>(commands);
        services.AddSingleton<ICopilotRuntimeResourcesFactory>(factory);
        services.AddKapibaraStudioCopilotSubscriptionAgentProvider();
        using var container = services.BuildServiceProvider();
        var provider = container.GetRequiredService<CopilotSubscriptionAgentProvider>();
        Assert.That(container.GetRequiredService<IAgentProvider>(), Is.SameAs(provider));
        Assert.That(factory.Creations, Is.EqualTo(1));
        Assert.That(resources.AccountCalls, Is.Zero);
        provider.Dispose(); provider.Dispose();
        Assert.That(resources.Disposals, Is.EqualTo(1));
    }
}
