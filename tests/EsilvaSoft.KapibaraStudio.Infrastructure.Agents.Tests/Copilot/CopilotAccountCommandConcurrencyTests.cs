using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture, Category("Unit")]
internal sealed class CopilotAccountCommandConcurrencyTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [TestCase("login")]
    [TestCase("logout")]
    public async Task AuthenticationProbeInvalidatedByAccountActionCannotStartCatalog(string action)
    {
        var resources = new Resources();
        var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        resources.Account.AuthenticationWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldCheck = provider.CheckAccountAndModelsAsync();
        await resources.Account.AuthenticationEntered.Task.WaitAsync(Deadline);
        var accountAction = action == "login" ? provider.LoginAsync() : provider.LogoutAsync(true);
        try
        {
            var invalidated = await provider.GetStatusAsync(CancellationToken.None);
            resources.Account.AuthenticationWait.TrySetResult();
            Assert.That((await oldCheck.WaitAsync(Deadline)).State, Is.EqualTo(CopilotAccountState.Unavailable));
            Assert.That(resources.Account.CatalogCalls, Is.Zero,
                "Authentication from the previous account revision cannot authorize a model lookup.");
            Assert.That(resources.Account.Disposals, Is.EqualTo(1));
            Assert.That(await provider.GetStatusAsync(CancellationToken.None), Is.SameAs(invalidated));
            Assert.Throws<InvalidOperationException>(() => provider.CreateSessionAsync(
                new AgentSessionOptions(provider.ProviderId, "synthetic-model") { PersistProviderSession = false },
                CancellationToken.None));
            Assert.That(resources.SessionCreations, Is.Zero);

            commands.Completion.TrySetResult(CopilotAccountCommandState.CommandFailed);
            await accountAction.WaitAsync(Deadline);
            resources.Account.AuthenticationWait = null;
            Assert.That((await provider.CheckAccountAndModelsAsync()).State, Is.EqualTo(CopilotAccountState.Subscription));
            Assert.That(resources.Account.CatalogCalls, Is.EqualTo(1), "Only a fresh explicit check may query models.");
            Assert.That(commands.Actions, Is.EqualTo(1), "Recovery does not repeat the account action.");
        }
        finally
        {
            resources.Account.AuthenticationWait?.TrySetResult();
            commands.Completion.TrySetResult(CopilotAccountCommandState.CommandFailed);
            await Task.WhenAll(oldCheck, accountAction).WaitAsync(Deadline);
        }
    }

    [TestCase(false, "user")]
    [TestCase(true, "token")]
    public async Task CancelledLogoutRecheckCannotPublishLateAuthentication(bool authenticated, string authType)
    {
        var resources = new Resources();
        var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        await provider.CheckAccountAndModelsAsync();
        resources.Account.Authentication = new(authenticated, authType);
        resources.Account.AuthenticationWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var logout = provider.LogoutAsync(true, cancellation.Token);
        var invalidated = await provider.GetStatusAsync(CancellationToken.None);
        try
        {
            commands.Completion.SetResult(CopilotAccountCommandState.Completed);
            await resources.Account.AuthenticationEntered.Task.WaitAsync(Deadline);
            cancellation.Cancel();
            resources.Account.AuthenticationWait.TrySetResult();

            Assert.CatchAsync<OperationCanceledException>(async () => await logout.WaitAsync(Deadline));
            Assert.That(await provider.GetStatusAsync(CancellationToken.None), Is.SameAs(invalidated),
                "A cancelled post-logout check cannot replace the invalidated account snapshot.");
            Assert.That(resources.Account.Disposals, Is.EqualTo(2));
            Assert.That(resources.Account.CatalogCalls, Is.EqualTo(1), "No model lookup follows a cancelled authentication response.");
            Assert.Throws<InvalidOperationException>(() => provider.CreateSessionAsync(
                new AgentSessionOptions(provider.ProviderId, "synthetic-model") { PersistProviderSession = false },
                CancellationToken.None));
            Assert.That(resources.SessionCreations, Is.Zero);

            resources.Account.AuthenticationWait = null;
            resources.Account.Authentication = new(true, "user");
            Assert.That((await provider.CheckAccountAndModelsAsync()).State, Is.EqualTo(CopilotAccountState.Subscription));
            await using var recovered = await provider.CreateSessionAsync(
                new AgentSessionOptions(provider.ProviderId, "synthetic-model") { PersistProviderSession = false },
                CancellationToken.None);
            Assert.That(resources.SessionCreations, Is.EqualTo(1));
            Assert.That(commands.Actions, Is.EqualTo(1), "Recovery never repeats the logout command.");
        }
        finally { resources.Account.AuthenticationWait?.TrySetResult(); }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AccountProbeStartedBeforeLogoutCannotRestoreOldAvailability(bool catalogFails)
    {
        var resources = new Resources();
        var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        resources.Account.CatalogWait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        resources.Account.FailCatalog = catalogFails;
        var oldCheck = provider.CheckAccountAndModelsAsync();
        await resources.Account.CatalogEntered.Task.WaitAsync(Deadline);
        var queuedCheck = provider.CheckAccountAndModelsAsync();
        var logout = provider.LogoutAsync(true);
        try
        {
            var duringCommand = await provider.CheckAccountAndModelsAsync();
            Assert.That(duringCommand.State, Is.EqualTo(CopilotAccountState.Unavailable));
            Assert.That(resources.AccountCreations, Is.EqualTo(1), "A visible account action must not start another probe.");
            resources.Account.CatalogWait.SetResult();
            var oldResult = await oldCheck.WaitAsync(Deadline);
            Assert.That(oldResult.State, Is.EqualTo(CopilotAccountState.Unavailable));
            Assert.That((await queuedCheck.WaitAsync(Deadline)).State, Is.EqualTo(CopilotAccountState.Unavailable));
            Assert.That(resources.AccountCreations, Is.EqualTo(1), "An obsolete queued probe must not start a client.");
            Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.False);
            Assert.That(resources.Account.Disposals, Is.EqualTo(1));
            commands.Completion.SetResult(CopilotAccountCommandState.CommandFailed);
            await logout.WaitAsync(Deadline);
            Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.False);
        }
        finally
        {
            resources.Account.CatalogWait.TrySetResult();
            commands.Completion.TrySetResult(CopilotAccountCommandState.CommandFailed);
            await Task.WhenAll(oldCheck, queuedCheck, logout).WaitAsync(Deadline);
        }
    }

    [TestCase(CopilotAccountCommandState.CommandFailed)]
    [TestCase(CopilotAccountCommandState.StillRunning)]
    [TestCase(CopilotAccountCommandState.RuntimeUnavailable)]
    [TestCase(CopilotAccountCommandState.NoVisibleTerminal)]
    [TestCase(CopilotAccountCommandState.StartFailed)]
    public async Task UnconfirmedLogoutResultRequiresFreshCheckBeforeCreatingSession(CopilotAccountCommandState state)
    {
        var resources = new Resources();
        var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        await provider.CheckAccountAndModelsAsync();
        Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.True);
        var logout = provider.LogoutAsync(true);
        Assert.Throws<InvalidOperationException>(() => provider.CreateSessionAsync(
            new AgentSessionOptions(provider.ProviderId, "synthetic-model") { PersistProviderSession = false },
            CancellationToken.None));
        commands.Completion.SetResult(state);
        Assert.That((await logout.WaitAsync(Deadline)).Account, Is.Null);
        var unavailable = await provider.GetStatusAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(unavailable.IsAvailable, Is.False);
            Assert.That(unavailable.Models, Is.Empty, "An unconfirmed logout must discard the previous model catalog.");
        });
        Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.CreateSessionAsync(
            new AgentSessionOptions(provider.ProviderId, "synthetic-model") { PersistProviderSession = false },
            CancellationToken.None));
        Assert.That(resources.SessionCreations, Is.Zero);

        resources.Account.Authentication = new(false, null);
        Assert.That((await provider.CheckAccountAndModelsAsync()).State, Is.EqualTo(CopilotAccountState.NotLoggedIn));
        resources.Account.Authentication = new(true, "user");
        await provider.CheckAccountAndModelsAsync();
        await using var session = await provider.CreateSessionAsync(
            new AgentSessionOptions(provider.ProviderId, "synthetic-model") { PersistProviderSession = false },
            CancellationToken.None);
        Assert.That(resources.SessionCreations, Is.EqualTo(1), "Only a fresh successful check restores session creation.");
    }

    [TestCase(CopilotAccountCommandState.CommandFailed)]
    [TestCase(CopilotAccountCommandState.StillRunning)]
    [TestCase(CopilotAccountCommandState.RuntimeUnavailable)]
    [TestCase(CopilotAccountCommandState.NoVisibleTerminal)]
    [TestCase(CopilotAccountCommandState.StartFailed)]
    public async Task UnconfirmedLoginResultRequiresFreshCheckBeforeCreatingSession(CopilotAccountCommandState state)
    {
        var resources = new Resources();
        var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        await provider.CheckAccountAndModelsAsync();
        Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.True);

        var login = provider.LoginAsync();
        commands.Completion.SetResult(state);
        var result = await login.WaitAsync(Deadline);
        var unavailable = await provider.GetStatusAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.State, Is.EqualTo(state));
            Assert.That(result.Account, Is.Null);
            Assert.That(unavailable.IsAvailable, Is.False,
                "An unconfirmed login cannot preserve the account snapshot from before the CLI action.");
        });
        Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.CreateSessionAsync(
            new AgentSessionOptions(provider.ProviderId, "synthetic-model") { PersistProviderSession = false },
            CancellationToken.None));
        Assert.That(resources.SessionCreations, Is.Zero);

        resources.Account.Authentication = new(false, "user");
        Assert.That((await provider.CheckAccountAndModelsAsync()).State, Is.EqualTo(CopilotAccountState.NotLoggedIn));
        Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.False);
        resources.Account.Authentication = new(true, "user");
        Assert.That((await provider.CheckAccountAndModelsAsync()).State, Is.EqualTo(CopilotAccountState.Subscription));
        await using var session = await provider.CreateSessionAsync(
            new AgentSessionOptions(provider.ProviderId, "synthetic-model") { PersistProviderSession = false },
            CancellationToken.None);
        Assert.That(resources.SessionCreations, Is.EqualTo(1), "Only a fresh successful check restores session creation.");
    }

    [Test]
    public async Task CancellingVisibleLoginWaitDoesNotRestorePreviousAccount()
    {
        var resources = new Resources();
        var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        await provider.CheckAccountAndModelsAsync();
        using var cancellation = new CancellationTokenSource();
        var login = provider.LoginAsync(cancellation.Token);
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await login.WaitAsync(Deadline));
        Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.False);
        await provider.CheckAccountAndModelsAsync();
        Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.True);
    }

    [Test]
    public async Task CancellingVisibleLogoutWaitRequiresFreshAccountCheckBeforeCreatingSession()
    {
        var resources = new Resources();
        var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        await provider.CheckAccountAndModelsAsync();
        using var cancellation = new CancellationTokenSource();
        var logout = provider.LogoutAsync(true, cancellation.Token);
        Assert.That(commands.Actions, Is.EqualTo(1), "Cancellation occurs after the confirmed account action starts.");
        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await logout.WaitAsync(Deadline));
        var unavailable = await provider.GetStatusAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(unavailable.IsAvailable, Is.False);
            Assert.That(unavailable.Models, Is.Empty, "Stopping the wait cannot restore the previous account catalog.");
            Assert.That(commands.Completion.Task.IsCompleted, Is.False,
                "Cancelling the wait does not complete or reverse the external account action.");
        });
        Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.CreateSessionAsync(
            new AgentSessionOptions(provider.ProviderId, "synthetic-model") { PersistProviderSession = false },
            CancellationToken.None));
        Assert.That(resources.SessionCreations, Is.Zero);

        resources.Account.Authentication = new(false, null);
        Assert.That((await provider.CheckAccountAndModelsAsync()).State, Is.EqualTo(CopilotAccountState.NotLoggedIn));
        Assert.That((await provider.GetStatusAsync(CancellationToken.None)).Models, Is.Empty);
        resources.Account.Authentication = new(true, "user");
        Assert.That((await provider.CheckAccountAndModelsAsync()).State, Is.EqualTo(CopilotAccountState.Subscription));
        await using var session = await provider.CreateSessionAsync(
            new AgentSessionOptions(provider.ProviderId, "synthetic-model") { PersistProviderSession = false },
            CancellationToken.None);
        Assert.That(resources.SessionCreations, Is.EqualTo(1), "A fresh successful check enables session creation again.");
    }

    [Test]
    public async Task CompletedLogoutChecksNewAccountAndRejectsOverlappingLogin()
    {
        var resources = new Resources();
        var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        await provider.CheckAccountAndModelsAsync();
        var logout = provider.LogoutAsync(true);
        try
        {
            Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.LoginAsync());
            Assert.That(commands.Actions, Is.EqualTo(1));
            resources.Account.Authentication = new(false, null);
            commands.Completion.SetResult(CopilotAccountCommandState.Completed);
            var result = await logout.WaitAsync(Deadline);
            Assert.That(result.Account?.State, Is.EqualTo(CopilotAccountState.NotLoggedIn));
            Assert.That(resources.AccountCreations, Is.EqualTo(2));
            Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.False);
        }
        finally
        {
            commands.Completion.TrySetResult(CopilotAccountCommandState.CommandFailed);
            await logout.WaitAsync(Deadline);
        }
    }

    [Test]
    public async Task RejectedConfirmationAndPreCancelledActionKeepVerifiedSnapshot()
    {
        var resources = new Resources();
        var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        await provider.CheckAccountAndModelsAsync();
        Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.LogoutAsync(false));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await provider.LoginAsync(cancellation.Token));
        Assert.ThrowsAsync<OperationCanceledException>(async () => await provider.LogoutAsync(true, cancellation.Token));
        Assert.That(commands.Actions, Is.Zero);
        Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.True);
    }

    [Test]
    public async Task SynchronousLauncherFailureReleasesActionAndKeepsAccountUnavailable()
    {
        var resources = new Resources();
        var commands = new Commands { FailLaunch = true };
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        await provider.CheckAccountAndModelsAsync();
        Assert.ThrowsAsync<IOException>(async () => await provider.LoginAsync());
        Assert.That((await provider.GetStatusAsync(CancellationToken.None)).IsAvailable, Is.False);
        commands.FailLaunch = false;
        commands.Completion.SetResult(CopilotAccountCommandState.Completed);
        var retry = await provider.LoginAsync();
        Assert.That(retry.Account?.State, Is.EqualTo(CopilotAccountState.Subscription));
        Assert.That(commands.Actions, Is.EqualTo(2));
    }

    private sealed class Resources : ICopilotRuntimeResources
    {
        public AccountClient Account { get; } = new();
        public int AccountCreations;
        public int SessionCreations;
        public ICopilotSessionFsStore VolatileStore { get; } = new MemoryCopilotSessionStorage(false);
        public ICopilotSessionFsStore PersistentStore { get; } = new MemoryCopilotSessionStorage(true);
        public ICopilotRuntimeClient CreateAccountClient() { AccountCreations++; return Account; }
        public ICopilotRuntimeClient CreateSessionClient(string? workingDirectory, bool persistent)
        { SessionCreations++; return new MemoryCopilotRuntime(); }
        public ICopilotRuntimeClient CreateCleanupClient(bool volatileSession) => throw new NotSupportedException();
        public void Dispose() { }
    }

    private sealed class AccountClient : ICopilotRuntimeClient
    {
        public CopilotRuntimeAuthentication Authentication = new(true, "user");
        public TaskCompletionSource AuthenticationEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? AuthenticationWait;
        public int CatalogCalls;
        public TaskCompletionSource CatalogEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource? CatalogWait;
        public bool FailCatalog;
        public int Disposals;
        public Task StartAsync(CancellationToken token) => Task.CompletedTask;
        public async Task<CopilotRuntimeAuthentication> GetAuthStatusAsync(CancellationToken token)
        {
            if (AuthenticationWait is { } wait)
            {
                AuthenticationEntered.TrySetResult();
                await wait.Task;
            }
            return Authentication;
        }
        public async Task<IReadOnlyList<string>> ListModelIdsAsync(CancellationToken token)
        {
            CatalogCalls++;
            CatalogEntered.TrySetResult();
            if (CatalogWait is { } wait) await wait.Task;
            if (FailCatalog) throw new IOException("synthetic catalog failure");
            return ["synthetic-model"];
        }
        public Task<bool> HasSessionAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task DeleteSessionAsync(string id, CancellationToken token) => throw new NotSupportedException();
        public Task<ICopilotRuntimeSession> CreateSessionAsync(SessionConfig config, CancellationToken token) => throw new NotSupportedException();
        public Task<ICopilotRuntimeSession> ResumeSessionAsync(string id, ResumeSessionConfig config, CancellationToken token) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }

    private sealed class Commands : ICopilotAccountCommands
    {
        public TaskCompletionSource<CopilotAccountCommandState> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Actions;
        public bool FailLaunch;
        public bool IsCliInstalled() => true;
        public Task<CopilotAccountCommandState> RunVisibleAsync(string action, CancellationToken token)
        {
            Actions++;
            if (FailLaunch) throw new IOException("synthetic launcher failure");
            return Completion.Task.WaitAsync(token);
        }
    }

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
}
