using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class AgentEditProposalMutationConcurrencyTests
{
    [Test]
    public async Task TwoReviewsUsingTheSameOldSnapshotCannotApplyTheSameHunkTwiceOrLoseItsState()
    {
        const string original = "const value = 1;\n";
        const string proposed = "const value = 2;\n";
        Assert.That(LineDiff.TryCompute(original, proposed, out var hunks), Is.True);
        using var synthetic = new SyntheticDirectory();
        var path = Path.Combine(synthetic.Path, $"{Guid.NewGuid():N}.js");
        await File.WriteAllTextAsync(path, original);
        try
        {
        var proposal = new AgentEditProposal(Guid.NewGuid(), Guid.NewGuid(), path, null,
            AgentEditProposalStore.Sha256(original), original, proposed, hunks, DateTimeOffset.UtcNow);
        var store = new AgentEditProposalStore(new EsilvaSoft.KapibaraStudio.SystemAdapters.LocalAgentBoundedFileReader());
        Assert.That(store.Submit(proposal).Status, Is.EqualTo(AgentEditProposalSubmissionStatus.Registered));
        Assert.That(store.TryGet(proposal.Id, out var oldSnapshot), Is.True);
        var editor = new MemoryEditor(original);

        async Task<AgentEditApplyOutcome?> ApplyFromOldViewAsync() => await Task.Run(() =>
        {
            var mutation = store.Mutate(proposal.Id, current =>
            {
                var outcome = AgentEditProposalApplier.ApplyHunk(editor, current, 0);
                return (outcome, outcome.States);
            });
            return mutation?.Result;
        });

        var outcomes = await Task.WhenAll(ApplyFromOldViewAsync(), ApplyFromOldViewAsync());
        Assert.Multiple(() =>
        {
            Assert.That(oldSnapshot.HunkStates[0], Is.EqualTo(AgentEditHunkState.Pending), "Both review surfaces began with the same stale snapshot.");
            Assert.That(outcomes.Count(static outcome => outcome is { Succeeded: true, Changed: 1 }), Is.EqualTo(1));
            Assert.That(editor.Text, Is.EqualTo(proposed), "The edit must be applied exactly once.");
            Assert.That(store.TryGet(proposal.Id, out var current), Is.True);
            Assert.That(current.HunkStates[0], Is.EqualTo(AgentEditHunkState.Applied));
        });
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ClosedCapturedTabCannotBeReplacedByMatchingDiskSnapshot()
    {
        const string original = "const value = 1;\n";
        const string proposed = "const value = 2;\n";
        Assert.That(LineDiff.TryCompute(original, proposed, out var hunks), Is.True);
        using var synthetic = new SyntheticDirectory();
        var path = Path.Combine(synthetic.Path, $"{Guid.NewGuid():N}.js");
        await File.WriteAllTextAsync(path, original);
        try
        {
            var proposal = new AgentEditProposal(Guid.NewGuid(), Guid.NewGuid(), path, "closed-tab",
                AgentEditProposalStore.Sha256(original), original, proposed, hunks, DateTimeOffset.UtcNow);
            var store = new AgentEditProposalStore(new EsilvaSoft.KapibaraStudio.SystemAdapters.LocalAgentBoundedFileReader());
            using var resolver = store.AttachTextResolver((_, _) => null);
            Assert.That(store.Submit(proposal).Status, Is.EqualTo(AgentEditProposalSubmissionStatus.TargetUnavailable));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class MemoryEditor(string text) : IAgentBufferEditor
    {
        private readonly object _gate = new();
        private string _text = text;

        public string Text
        {
            get { lock (_gate) return _text; }
        }

        public bool TryApply(IReadOnlyList<LineDiffTextEdit> edits)
        {
            lock (_gate)
            {
                foreach (var edit in edits)
                {
                    if (edit.Offset < 0 || edit.Length < 0 || edit.Offset + edit.Length > _text.Length) return false;
                    _text = string.Concat(_text.AsSpan(0, edit.Offset), edit.Replacement, _text.AsSpan(edit.Offset + edit.Length));
                }

                return true;
            }
        }
    }
}
