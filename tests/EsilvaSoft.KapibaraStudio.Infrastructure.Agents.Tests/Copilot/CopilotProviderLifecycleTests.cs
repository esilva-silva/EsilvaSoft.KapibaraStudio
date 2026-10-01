using System.Collections.Concurrent;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture, Category("Unit")]
internal sealed class CopilotProviderLifecycleTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);
    private static readonly string[] AccountCloseOrder = ["account-closed", "owner-closed"];
    private static readonly string[] CleanupCloseOrder = ["cleanup-closed", "owner-closed"];

    [Test]
    public async Task DisposeWaitsForPendingAccountClientAndRejectsQueuedRefresh()
    {
        var resources = new Resources();
        resources.Account.WaitForCatalog = true;
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, new Commands());
        var activeCheck = provider.CheckAccountAndModelsAsync();
        await resources.Account.CatalogEntered.Task.WaitAsync(Deadline);
        var queuedCheck = provider.CheckAccountAndModelsAsync();
        var disposal = Task.Factory.StartNew(provider.Dispose, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.That(SpinWait.SpinUntil(() => IsTerminal(provider), Deadline), Is.True);
            Assert.That(resources.Disposals, Is.Zero, "An open account client still owns the resources.");
            Assert.That(disposal.IsCompleted, Is.False);
            resources.Account.ReleaseCatalog.TrySetResult();

            Assert.ThrowsAsync<ObjectDisposedException>(async () => await activeCheck.WaitAsync(Deadline));
            Assert.ThrowsAsync<ObjectDisposedException>(async () => await queuedCheck.WaitAsync(Deadline));
            await disposal.WaitAsync(Deadline);
            Assert.That(resources.AccountCreations, Is.EqualTo(1), "The queued refresh must not start another probe.");
            Assert.That(resources.Operations, Is.EqualTo(AccountCloseOrder));
            Assert.That(IsTerminal(provider), Is.True, "A late catalog must not restore availability.");
        }
        finally
        {
            resources.Account.ReleaseCatalog.TrySetResult();
            await disposal.WaitAsync(Deadline);
        }
    }

    [Test]
    public async Task DisposeWaitsForPendingCleanupBeforeClosingStorage()
    {
        var resources = new Resources();
        resources.Volatile.ReserveSession("owned-session");
        resources.Cleanup.WaitForDelete = true;
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, new Commands());
        var cleanup = provider.DeleteProviderSessionAsync("owned-session", CancellationToken.None);
        await resources.Cleanup.DeleteEntered.Task.WaitAsync(Deadline);
        var queuedCheck = provider.CheckAccountAndModelsAsync();
        var disposal = Task.Factory.StartNew(provider.Dispose, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            Assert.That(SpinWait.SpinUntil(() => IsTerminal(provider), Deadline), Is.True);
            Assert.That(resources.Disposals, Is.Zero);
            Assert.That(resources.Volatile.ContainsSession("owned-session"), Is.True);
            resources.Cleanup.ReleaseDelete.TrySetResult();
            await cleanup.WaitAsync(Deadline);
            Assert.ThrowsAsync<ObjectDisposedException>(async () => await queuedCheck.WaitAsync(Deadline));
            await disposal.WaitAsync(Deadline);
            Assert.That(resources.Volatile.ContainsSession("owned-session"), Is.False);
            Assert.That(resources.AccountCreations, Is.Zero);
            Assert.That(resources.Operations, Is.EqualTo(CleanupCloseOrder));
        }
        finally
        {
            resources.Cleanup.ReleaseDelete.TrySetResult();
            await disposal.WaitAsync(Deadline);
        }
    }

    [Test]
    public async Task DisposedProviderRejectsAllResourceAndAccountOperations()
    {
        var resources = new Resources();
        var commands = new Commands();
        using var provider = new CopilotSubscriptionAgentProvider(new NoTools(), resources, commands);
        await provider.CheckAccountAndModelsAsync();
        provider.Dispose();
        provider.Dispose();

        Assert.Throws<ObjectDisposedException>(() => provider.GetStatusAsync(CancellationToken.None));
        Assert.Throws<ObjectDisposedException>(() => provider.CreateSessionAsync(
            new AgentSessionOptions(provider.ProviderId, "synthetic-model") { PersistProviderSession = false },
            CancellationToken.None));
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await provider.CheckAccountAndModelsAsync());
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await provider.LoginAsync());
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await provider.LogoutAsync(true));
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await provider.DeleteProviderSessionAsync(
            "synthetic-session", CancellationToken.None));
        Assert.That(resources.Disposals, Is.EqualTo(1));
        Assert.That(resources.AccountCreations, Is.EqualTo(1));
        Assert.That(resources.SessionCreations, Is.Zero);
        Assert.That(resources.CleanupCreations, Is.Zero);
        Assert.That(commands.Actions, Is.Zero);
    }

    private static bool IsTerminal(CopilotSubscriptionAgentProvider provider)
    {
        try { _ = provider.GetStatusAsync(CancellationToken.None); return false; }
        catch (ObjectDisposedException) { return true; }
    }

    private sealed class Resources : ICopilotRuntimeResources
    {
        public ConcurrentQueue<string> Operations { get; } = new();
        public ControlledClient Account { get; }
        public ControlledClient Cleanup { get; }
        public MemoryCopilotSessionStorage Volatile { get; } = new(false);
        public ICopilotSessionFsStore VolatileStore => Volatile;
        public ICopilotSessionFsStore PersistentStore { get; } = new MemoryCopilotSessionStorage(true);
        public int Disposals;
        public int AccountCreations;
        public int SessionCreations;
        public int CleanupCreations;
        public Resources()
        {
            Account = new ControlledClient("account", Operations);
            Cleanup = new ControlledClient("cleanup", Operations);
        }
        public ICopilotRuntimeClient CreateAccountClient() { AccountCreations++; return Account; }
        public ICopilotRuntimeClient CreateSessionClient(string? workingDirectory, bool persistent)
        { SessionCreations++; return new MemoryCopilotRuntime(); }
        public ICopilotRuntimeClient CreateCleanupClient(bool volatileSession) { CleanupCreations++; return Cleanup; }
        public void Dispose() { Disposals++; Operations.Enqueue("owner-closed"); }
    }

    private sealed class ControlledClient(string name, ConcurrentQueue<string> operations) : ICopilotRuntimeClient
    {
        public bool WaitForCatalog;
        public bool WaitForDelete;
        public TaskCompletionSource CatalogEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCatalog { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DeleteEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseDelete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<CopilotRuntimeAuthentication> GetAuthStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new CopilotRuntimeAuthentication(true, "user"));
        public async Task<IReadOnlyList<string>> ListModelIdsAsync(CancellationToken cancellationToken)
        {
            CatalogEntered.TrySetResult();
            if (WaitForCatalog) await ReleaseCatalog.Task;
            return ["synthetic-model"];
        }
        public Task<bool> HasSessionAsync(string sessionId, CancellationToken cancellationToken) => Task.FromResult(true);
        public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            DeleteEntered.TrySetResult();
            if (WaitForDelete) await ReleaseDelete.Task;
        }
        public Task<ICopilotRuntimeSession> CreateSessionAsync(SessionConfig configuration, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<ICopilotRuntimeSession> ResumeSessionAsync(string sessionId, ResumeSessionConfig configuration,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() { operations.Enqueue(name + "-closed"); return ValueTask.CompletedTask; }
    }

    private sealed class Commands : ICopilotAccountCommands
    {
        public int Actions;
        public bool IsCliInstalled() => true;
        public Task<CopilotAccountCommandState> RunVisibleAsync(string action, CancellationToken cancellationToken)
        { Actions++; return Task.FromResult(CopilotAccountCommandState.Completed); }
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
