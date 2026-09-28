using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class AgentEditProposalStoreEventOrderTests
{
    [Test]
    public async Task ConcurrentMutationsPublishTheLatestSnapshotWhenUiQueueIsDrained()
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        await session.Dispatch<bool>(async () =>
        {
            const string original = "const value = 1;\n";
            const string proposed = "const value = 2;\n";
            Assert.That(LineDiff.TryCompute(original, proposed, out var hunks), Is.True);
            var path = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"{Guid.NewGuid():N}-proposal-event-order.js");
            await File.WriteAllTextAsync(path, original);
            try
            {
                var proposal = new AgentEditProposal(Guid.NewGuid(), Guid.NewGuid(), path, null,
                    AgentEditProposalStore.Sha256(original), original, proposed, hunks, DateTimeOffset.UtcNow);
                var store = new AgentEditProposalStore();
                Assert.That(store.Submit(proposal).Status, Is.EqualTo(AgentEditProposalSubmissionStatus.Registered));
                var published = new List<AgentEditHunkState>();
                store.ProposalUpdated += (_, entry) => published.Add(entry.HunkStates[0]);

                const int mutationCount = 32;
                var mutations = Enumerable.Range(0, mutationCount).Select(index => Task.Run(() =>
                {
                    var next = index % 2 == 0 ? AgentEditHunkState.Applied : AgentEditHunkState.Pending;
                    store.Mutate(proposal.Id, _ => (true, new[] { next }));
                })).ToArray();

                // Keep the UI dispatcher from draining posted snapshots until all concurrent mutations finish.
                Task.WaitAll(mutations);
                Assert.That(store.TryGet(proposal.Id, out var final), Is.True);
                Dispatcher.UIThread.RunJobs();

                Assert.Multiple(() =>
                {
                    Assert.That(published, Has.Count.EqualTo(mutationCount));
                    Assert.That(published, Is.All.EqualTo(final.HunkStates[0]),
                        "A delayed event must resolve to current state instead of publishing an older snapshot.");
                });
                return true;
            }
            finally
            {
                File.Delete(path);
            }
        }, CancellationToken.None);
    }
}
