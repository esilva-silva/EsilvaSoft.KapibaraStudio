using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Infrastructure;

namespace EsilvaSoft.SlopStudio.UnitTests;

[TestFixture]
public sealed class SecretServicePromptTests
{
    [Test]
    public async Task ObservesBeforeShowingAndAcceptsSignalBeforeMethodReply()
    {
        Action<SecretServicePromptResult?, Exception?>? callback = null;
        var disposable = new Subscription();
        var result = await SecretServicePrompt.WaitAsync(handler =>
        {
            callback = handler;
            return Task.FromResult<IDisposable>(disposable);
        }, () =>
        {
            Assert.That(callback, Is.Not.Null);
            callback!(new SecretServicePromptResult(false, "/item", null), null);
            // Duplicate/late completion cannot replace the first outcome.
            callback(new SecretServicePromptResult(true, null, null), null);
            return Task.CompletedTask;
        }, CancellationToken.None);
        Assert.That(result.Item, Is.EqualTo("/item"));
        Assert.That(disposable.Disposed, Is.True);
    }

    [Test]
    public void DisconnectWhileWaitingFinishesAndRemovesSubscription()
    {
        Action<SecretServicePromptResult?, Exception?>? callback = null;
        var disposable = new Subscription();
        Assert.ThrowsAsync<SecretServiceException>(async () => await SecretServicePrompt.WaitAsync(handler =>
        {
            callback = handler;
            return Task.FromResult<IDisposable>(disposable);
        }, () =>
        {
            callback!(null, new SecretServiceException(SecretStoreFailureCode.Unavailable));
            return Task.CompletedTask;
        }, CancellationToken.None));
        Assert.That(disposable.Disposed, Is.True);
    }

    [Test]
    public async Task CallerCancellationWhileWaitingRemovesSubscription()
    {
        using var cts = new CancellationTokenSource();
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposable = new Subscription();
        var task = SecretServicePrompt.WaitAsync(_ => Task.FromResult<IDisposable>(disposable), () =>
        {
            shown.TrySetResult();
            return Task.CompletedTask;
        }, cts.Token);
        await shown.Task;
        cts.Cancel();
        Assert.ThrowsAsync<TaskCanceledException>(async () => await task);
        Assert.That(disposable.Disposed, Is.True);
    }

    private sealed class Subscription : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
}
