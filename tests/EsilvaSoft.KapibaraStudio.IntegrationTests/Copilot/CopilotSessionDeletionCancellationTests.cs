using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

#pragma warning disable GHCP001 // Exercise the pinned experimental SessionFs contract with local stores only.

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

[TestFixture, Category("Integration")]
public sealed class CopilotSessionDeletionCancellationTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelledNativeDeletionPreservesLocalStateAndAllowsIdempotentRetry(bool persistent)
    {
        var root = Path.Combine(Path.GetTempPath(), "kapibara-copilot-cancel-delete-" + Guid.NewGuid().ToString("N"));
        const string id = "cancelled-delete-session";
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await using ICopilotSessionFsStore store = persistent
                ? new CopilotPersistentSessionFsStore(root) : new CopilotVolatileSessionFsStore();
            var handler = CreateHandler(store, id);
            await handler.WriteFileAsync(new SessionFsWriteFileRequest
            { SessionId = id, Path = "/state.txt", Content = "recoverable" }, CancellationToken.None);
            using var cancellation = new CancellationTokenSource();
            var nativeDeleted = false;
            var nativeCalls = 0;
            var deletion = store.DeleteSessionAsync(id, async token =>
            {
                Assert.That(token, Is.EqualTo(cancellation.Token));
                nativeCalls++;
                nativeDeleted = true;
                entered.TrySetResult();
                // A native delete can take effect and return normally after cancellation of its wait.
                await release.Task;
            }, cancellation.Token).AsTask();
            await entered.Task.WaitAsync(Deadline);
            cancellation.Cancel();
            release.TrySetResult();

            Assert.CatchAsync<OperationCanceledException>(async () => await deletion.WaitAsync(Deadline));
            Assert.That(nativeDeleted, Is.True, "Cancellation cannot roll back the native deletion.");
            Assert.That(store.ContainsSession(id), Is.True, "Keep local recovery state after cancellation.");
            var recovered = CreateHandler(store, id);
            var read = await recovered.ReadFileAsync(new SessionFsReadFileRequest
            { SessionId = id, Path = "/state.txt" }, CancellationToken.None);
            Assert.That(read.Error, Is.Null);
            Assert.That(read.Content, Is.EqualTo("recoverable"));

            Assert.That(await store.DeleteSessionAsync(id, _ =>
            {
                nativeCalls++;
                Assert.That(nativeDeleted, Is.True, "An already absent native session is a successful retry.");
                return Task.CompletedTask;
            }), Is.True);
            Assert.That(nativeCalls, Is.EqualTo(2));
            Assert.That(store.ContainsSession(id), Is.False);
            Assert.That(await store.DeleteSessionAsync(id), Is.False, "Repeated local deletion is idempotent.");
        }
        finally
        {
            release.TrySetResult();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static ISessionFsHandler CreateHandler(ICopilotSessionFsStore store, string id) => store switch
    {
        CopilotPersistentSessionFsStore persistent => (ISessionFsHandler)persistent.CreateProvider(id),
        CopilotVolatileSessionFsStore volatileStore => (ISessionFsHandler)volatileStore.CreateProvider(id),
        _ => throw new NotSupportedException(),
    };
}

#pragma warning restore GHCP001
