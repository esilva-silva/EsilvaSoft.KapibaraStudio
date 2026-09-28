using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AgentEditProposalHunkReviewTests
{
    private sealed class Buffer(string text) : IAgentBufferEditor
    {
        public string Text { get; private set; } = text;

        public bool TryApply(IReadOnlyList<LineDiffTextEdit> edits)
        {
            var simulated = Text;
            foreach (var edit in edits)
            {
                if (edit.Offset < 0 || edit.Length < 0 || edit.Offset + edit.Length > simulated.Length) return false;
                simulated = simulated.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Replacement);
            }

            Text = simulated;
            return true;
        }
    }

    private static AgentEditProposalEntry Create(string original, string proposed)
    {
        Assert.That(LineDiff.TryCompute(original, proposed, out var hunks), Is.True);
        var proposal = new AgentEditProposal(Guid.NewGuid(), Guid.NewGuid(), "C:\\workspace\\query.js", "tab-1",
            AgentEditProposalStore.Sha256(original), original, proposed, hunks, DateTimeOffset.UtcNow);
        return new AgentEditProposalEntry(proposal, [.. hunks.Select(static hunk => hunk.State)]);
    }

    [Test]
    public void SelectedHunkCanBeAppliedAndRevertedWithoutChangingOtherText()
    {
        const string original = "one\ntwo\nthree\nfour\nfive\nsix\nseven\neight\nnine\n";
        const string proposed = "one\nTWO\nthree\nfour\nfive\nsix\nseven\nEIGHT\nnine\n";
        var entry = Create(original, proposed);
        Assert.That(entry.Proposal.Hunks.Count, Is.EqualTo(2));
        var buffer = new Buffer(original);

        var first = AgentEditProposalApplier.ApplyHunk(buffer, entry, 0);
        Assert.Multiple(() =>
        {
            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.Changed, Is.EqualTo(1));
            Assert.That(buffer.Text, Is.EqualTo("one\nTWO\nthree\nfour\nfive\nsix\nseven\neight\nnine\n"));
        });

        entry = entry with { HunkStates = first.States };
        var second = AgentEditProposalApplier.ApplyHunk(buffer, entry, 1);
        Assert.That(buffer.Text, Is.EqualTo(proposed));
        entry = entry with { HunkStates = second.States };

        var reverted = AgentEditProposalApplier.RevertHunk(buffer, entry, 0);
        Assert.That(reverted.Succeeded, Is.True);
        Assert.That(buffer.Text, Is.EqualTo("one\ntwo\nthree\nfour\nfive\nsix\nseven\nEIGHT\nnine\n"));
    }

    [Test]
    public void ChangedHunkIsMarkedStaleAndTheBufferIsNotForced()
    {
        const string original = "before\noriginal\nafter\n";
        const string proposed = "before\nproposed\nafter\n";
        var entry = Create(original, proposed);
        var buffer = new Buffer("before\nuser edit\nafter\n");

        var result = AgentEditProposalApplier.ApplyHunk(buffer, entry, 0);

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.Stale, Is.EqualTo(1));
            Assert.That(result.States[0], Is.EqualTo(AgentEditHunkState.Stale));
            Assert.That(buffer.Text, Is.EqualTo("before\nuser edit\nafter\n"));
        });
    }
}
