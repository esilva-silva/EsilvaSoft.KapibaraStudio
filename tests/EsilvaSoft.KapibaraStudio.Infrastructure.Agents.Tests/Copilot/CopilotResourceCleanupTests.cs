using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture, Category("Unit")]
internal sealed class CopilotResourceCleanupTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [TestCase(false)]
    [TestCase(true)]
    public void OwnerAlwaysClosesBothStoresAndReportsEveryFailure(bool persistentFails)
    {
        var volatileFailure = new IOException("synthetic volatile failure");
        var persistentFailure = new IOException("synthetic persistent failure");
        var volatileStore = new MemoryStore(false) { Failure = volatileFailure };
        var persistentStore = new MemoryStore(true) { Failure = persistentFails ? persistentFailure : null };
        var owner = new LocalCopilotRuntimeResources(volatileStore, persistentStore);

        var failure = Assert.Throws<AggregateException>(owner.Dispose)!;

        Assert.That(volatileStore.Disposals, Is.EqualTo(1));
        Assert.That(persistentStore.Disposals, Is.EqualTo(1));
        Assert.That(failure.InnerExceptions, Has.Count.EqualTo(persistentFails ? 2 : 1));
        Assert.That(failure.InnerExceptions, Does.Contain(volatileFailure));
        if (persistentFails) Assert.That(failure.InnerExceptions, Does.Contain(persistentFailure));
        Assert.That(Assert.Throws<AggregateException>(owner.Dispose), Is.SameAs(failure));
        Assert.That(volatileStore.Disposals, Is.EqualTo(1));
        Assert.That(persistentStore.Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task CleanupGroupAttemptsAllSessionsAndFlattensFailuresWithoutLosingThem()
    {
        var sqliteFailure = new IOException("synthetic SQLite failure");
        var lockFailure = new IOException("synthetic ownership lock failure");
        var lastFailure = new IOException("synthetic last session failure");
        var first = new MemoryStore(true) { Failure = new AggregateException(sqliteFailure, lockFailure) };
        var middle = new MemoryStore(true);
        var last = new MemoryStore(true) { Failure = lastFailure };

        var failure = Assert.ThrowsAsync<AggregateException>(async () =>
            await CopilotResourceCleanup.DisposeAllAsync([first, middle, last]))!;

        Assert.That(first.Disposals, Is.EqualTo(1));
        Assert.That(middle.Disposals, Is.EqualTo(1));
        Assert.That(last.Disposals, Is.EqualTo(1));
        Assert.That(failure.InnerExceptions, Has.Count.EqualTo(3));
        Assert.That(failure.InnerExceptions, Does.Contain(sqliteFailure));
        Assert.That(failure.InnerExceptions, Does.Contain(lockFailure));
        Assert.That(failure.InnerExceptions, Does.Contain(lastFailure));
        await CopilotResourceCleanup.DisposeAllAsync([]);
    }

    [Test]
    public async Task ConcurrentOwnerDisposalSharesPendingCleanupAndDoesNotSkipTheSecondStore()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var volatileStore = new MemoryStore(false) { Wait = release.Task };
        var persistentStore = new MemoryStore(true);
        var owner = new LocalCopilotRuntimeResources(volatileStore, persistentStore);
        var first = Task.Factory.StartNew(owner.Dispose, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
        await volatileStore.Started.Task.WaitAsync(Deadline);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = Task.Factory.StartNew(() => { secondStarted.TrySetResult(); owner.Dispose(); },
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        try
        {
            await secondStarted.Task.WaitAsync(Deadline);
            Assert.That(first.IsCompleted, Is.False);
            Assert.That(second.IsCompleted, Is.False);
            Assert.That(persistentStore.Disposals, Is.Zero);
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(Deadline);
            Assert.That(volatileStore.Disposals, Is.EqualTo(1));
            Assert.That(persistentStore.Disposals, Is.EqualTo(1));
        }
        finally
        {
            release.TrySetResult();
            await Task.WhenAll(first, second).WaitAsync(Deadline);
        }
    }

    private sealed class MemoryStore(bool persistent) : ICopilotSessionFsStore
    {
        public int Disposals;
        public Exception? Failure { get; init; }
        public Task? Wait { get; init; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool IsPersistent => persistent;
        public void ReserveSession(string sessionId) => throw new NotSupportedException();
        public void ConfigureSession(SessionConfigBase configuration) => throw new NotSupportedException();
        public bool ContainsSession(string sessionId) => throw new NotSupportedException();
        public ValueTask<bool> DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask<bool> DeleteSessionAsync(string sessionId, Func<CancellationToken, Task>? deleteNativeSession,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async ValueTask DisposeAsync()
        {
            Disposals++;
            Started.TrySetResult();
            if (Wait is { } wait) await wait;
            if (Failure is { } failure) throw failure;
        }
    }
}
