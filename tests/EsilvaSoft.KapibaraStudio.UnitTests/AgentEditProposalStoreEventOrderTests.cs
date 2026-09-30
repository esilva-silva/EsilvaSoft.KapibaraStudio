using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class AgentEditProposalStoreEventOrderTests
{
    [Test]
    public async Task ConcurrentMutationsPublishTheLatestSnapshotWhenUiQueueIsDrained()
    {
        const string original = "const value = 1;\n";
        const string proposed = "const value = 2;\n";
        var path = Path.DirectorySeparatorChar == '\\' ? @"C:\proposal-event-order.js" : "/proposal-event-order.js";
        Assert.That(LineDiff.TryCompute(original, proposed, out var hunks), Is.True);
        var proposal = new AgentEditProposal(Guid.NewGuid(), Guid.NewGuid(), path, null,
            AgentEditProposalStore.Sha256(original), original, proposed, hunks, DateTimeOffset.UtcNow);
        var queuedNotifications = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        var store = new AgentEditProposalStore(queuedNotifications.Enqueue, new OriginalTextReader(original));
        Assert.That(store.Submit(proposal).Status, Is.EqualTo(AgentEditProposalSubmissionStatus.Registered));
        // Submit queued ProposalAdded before the listener is attached. Remove that setup notification so the
        // queue below contains only the mutation callbacks under test.
        Assert.That(queuedNotifications.TryDequeue(out _), Is.True);
        var published = new List<AgentEditHunkState>();
        store.ProposalUpdated += (_, entry) => published.Add(entry.HunkStates[0]);

        const int mutationCount = 32;
        // A controllable queue keeps every event pending until all concurrent mutations have finished.
        var mutations = Enumerable.Range(0, mutationCount).Select(index => Task.Run(() =>
        {
            var next = index % 2 == 0 ? AgentEditHunkState.Applied : AgentEditHunkState.Pending;
            store.Mutate(proposal.Id, _ => (true, new[] { next }));
        })).ToArray();
        await Task.WhenAll(mutations);

        // Establish a deterministic final state while all callbacks remain queued. Every delayed callback
        // must resolve this snapshot, regardless of worker lock order or notification enqueue order.
        store.Mutate(proposal.Id, _ => (true, new[] { AgentEditHunkState.Applied }));
        Assert.That(store.TryGet(proposal.Id, out var final), Is.True);
        Assert.That(queuedNotifications, Has.Count.EqualTo(mutationCount + 1));
        while (queuedNotifications.TryDequeue(out var notification)) notification();

        Assert.Multiple(() =>
        {
            Assert.That(published, Has.Count.EqualTo(mutationCount + 1));
            Assert.That(published, Is.All.EqualTo(final.HunkStates[0]),
                "A delayed event must resolve to current state instead of publishing an older snapshot.");
        });
    }

    private sealed class OriginalTextReader(string text) : IAgentBoundedFileReader
    {
        public AgentFileReadResult Read(string fullPath, int maximumBytes) =>
            new(AgentFileReadState.Read, System.Text.Encoding.UTF8.GetBytes(text));
        public Task<AgentFileReadResult> ReadAsync(string fullPath, int maximumBytes, CancellationToken cancellationToken) =>
            Task.FromResult(Read(fullPath, maximumBytes));
    }
}
