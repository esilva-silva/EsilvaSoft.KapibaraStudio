using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Completion;

[TestFixture]
public sealed class TraditionalCompletionOrderingTests
{
    [Test]
    public async Task OlderAnalysisCannotReplaceOrCancelTheNewerRequest()
    {
        using var context = new WorkspaceTestContext();
        using var analysisRelease = new ManualResetEventSlim();
        var analysisEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var documentId = TextSnapshotVersion.NewDocumentId();
        var olderSnapshot = new BlockingSnapshot(
            "db.", new TextSnapshotVersion(documentId, 1), analysisEntered, analysisRelease);
        var newerSnapshot = new StringTextSnapshot("db.f", new TextSnapshotVersion(documentId, 2));
        var provider = new BarrierProvider(newerSnapshot.Version);
        var tab = new WorkspaceTabViewModel(context.Workspace) { TraditionalCompletion = provider };

        Task<TraditionalCompletionResult>? older = null;
        Task<TraditionalCompletionResult>? newer = null;
        try
        {
            older = tab.GetTraditionalCompletionsAsync(olderSnapshot, olderSnapshot.Length, CancellationToken.None);
            await analysisEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));

            newer = tab.GetTraditionalCompletionsAsync(newerSnapshot, newerSnapshot.Length, CancellationToken.None);
            await provider.NewerEntered.WaitAsync(TimeSpan.FromSeconds(3));

            // The older analysis resumes only after the newer request has acquired ownership and reached its provider.
            // It must observe its cancelled lease without calling Begin again or touching the newer token.
            analysisRelease.Set();
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await older.WaitAsync(TimeSpan.FromSeconds(3)));

            Assert.Multiple(() =>
            {
                Assert.That(provider.OlderProviderCalls, Is.Zero,
                    "A análise antiga não pode chegar ao provider depois que uma solicitação nova obteve a posse.");
                Assert.That(provider.NewerCancellationToken.IsCancellationRequested, Is.False,
                    "A solicitação antiga não pode cancelar o token da solicitação mais nova.");
            });

            provider.ReleaseNewer();
            var result = await newer.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Multiple(() =>
            {
                Assert.That(result.Items, Has.Count.EqualTo(1));
                Assert.That(result.Items[0].Label, Is.EqualTo("newer"));
            });
        }
        finally
        {
            analysisRelease.Set();
            provider.ReleaseNewer();
            if (older is not null) await ObserveAsync(older);
            if (newer is not null) await ObserveAsync(newer);
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (Exception) { }
    }

    private sealed class BlockingSnapshot(
        string text,
        TextSnapshotVersion version,
        TaskCompletionSource entered,
        ManualResetEventSlim release) : ITextSnapshot
    {
        public TextSnapshotVersion Version { get; } = version;
        public int Length => text.Length;
        public char this[int index] => text[index];

        public string GetText(int start, int length)
        {
            entered.TrySetResult();
            Assert.That(release.Wait(TimeSpan.FromSeconds(5)), Is.True, "A leitura de análise bloqueada deve ser liberada pelo teste.");
            return text.Substring(start, length);
        }

        public IReadOnlyList<TextChange>? GetChangesSince(ITextSnapshot previous) => null;
    }

    private sealed class BarrierProvider(TextSnapshotVersion newerVersion) : ICompletionProvider
    {
        private readonly TaskCompletionSource _newerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _releaseNewer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _olderProviderCalls;

        public CompletionProviderKind Kind => CompletionProviderKind.Traditional;
        public Task NewerEntered => _newerEntered.Task;
        public int OlderProviderCalls => Volatile.Read(ref _olderProviderCalls);
        public CancellationToken NewerCancellationToken { get; private set; }
        public void ReleaseNewer() => _releaseNewer.TrySetResult();

        public async ValueTask<CompletionResponse> CompleteAsync(
            CompletionRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.Context.Version == newerVersion)
            {
                NewerCancellationToken = cancellationToken;
                _newerEntered.TrySetResult();
                await _releaseNewer.Task.WaitAsync(cancellationToken);
                return Response(request, "newer");
            }

            Interlocked.Increment(ref _olderProviderCalls);
            return Response(request, "older");
        }

        private static CompletionResponse Response(CompletionRequest request, string label)
        {
            var item = new CompletionItem(label, label, "teste", CompletionItemKind.Text,
                new(request.Context.ReplaceSpan, request.Context.ReplaceSpan, label), label, 0, CompletionSource.Catalog);
            return new CompletionResponse(request, new(request.Context.Version, [item], false));
        }
    }
}
