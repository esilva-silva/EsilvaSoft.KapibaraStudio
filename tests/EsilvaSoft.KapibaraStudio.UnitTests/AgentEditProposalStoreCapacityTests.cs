using System.Collections.Concurrent;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Editing;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, Category("Unit")]
public sealed class AgentEditProposalStoreCapacityTests
{
    [TestCase(1)]
    [TestCase(2)]
    public async Task ConcurrentSubmissionsRespectRemainingCapacityAfterBothBaseReadsStart(int remainingCapacity)
    {
        const string original = "const value = 1;\n";
        const string proposed = "const value = 2;\n";
        var path = Path.DirectorySeparatorChar == '\\' ? @"C:\proposal-capacity.js" : "/proposal-capacity.js";
        Assert.That(LineDiff.TryCompute(original, proposed, out var hunks), Is.True);
        var conversation = Guid.NewGuid();
        AgentEditProposal Proposal() => new(Guid.NewGuid(), conversation, path, null,
            AgentEditProposalStore.Sha256(original), original, proposed, hunks, DateTimeOffset.UtcNow);
        using var reader = new GatedTextReader(original);
        var notifications = new ConcurrentQueue<Action>();
        var store = new AgentEditProposalStore(notifications.Enqueue, reader);
        var published = new List<Guid>();
        store.ProposalAdded += (_, entry) => published.Add(entry.Id);
        for (var index = 0; index < AgentEditProposalStore.MaximumProposals - remainingCapacity; index++)
            Assert.That(store.Submit(Proposal()).Status, Is.EqualTo(AgentEditProposalSubmissionStatus.Registered));

        var candidates = new[] { Proposal(), Proposal() };
        reader.GateReads = true;
        // Dedicated workers avoid depending on thread-pool expansion while synchronous base reads are gated.
        var submissions = candidates.Select(proposal => Task.Factory.StartNew(() => store.Submit(proposal),
            CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        try
        {
            Assert.That(reader.Entered.Wait(TimeSpan.FromSeconds(5)), Is.True,
                "Both submissions must pass the initial capacity check before either can register.");
        }
        finally
        {
            reader.Release.Set();
        }
        var results = await Task.WhenAll(submissions).WaitAsync(TimeSpan.FromSeconds(5));
        while (notifications.TryDequeue(out var notification)) notification();

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(result => result.Status == AgentEditProposalSubmissionStatus.Registered),
                Is.EqualTo(remainingCapacity));
            Assert.That(results.Count(result => result.Status == AgentEditProposalSubmissionStatus.Rejected),
                Is.EqualTo(2 - remainingCapacity));
            Assert.That(store.ForConversation(conversation), Has.Count.EqualTo(AgentEditProposalStore.MaximumProposals));
            Assert.That(published, Has.Count.EqualTo(AgentEditProposalStore.MaximumProposals));
            Assert.That(published.Distinct().Count(), Is.EqualTo(published.Count));
        });
        for (var index = 0; index < results.Length; index++)
        {
            var registered = results[index].Status == AgentEditProposalSubmissionStatus.Registered;
            Assert.Multiple(() =>
            {
                Assert.That(store.TryGet(candidates[index].Id, out _), Is.EqualTo(registered));
                Assert.That(published.Contains(candidates[index].Id), Is.EqualTo(registered));
                Assert.That(results[index].ErrorCode, Is.EqualTo(registered ? null : "ProposalLimitReached"));
            });
        }
    }

    private sealed class GatedTextReader(string text) : IAgentBoundedFileReader, IDisposable
    {
        public CountdownEvent Entered { get; } = new(2);
        public ManualResetEventSlim Release { get; } = new();
        public bool GateReads { get; set; }
        public AgentFileReadResult Read(string fullPath, int maximumBytes)
        {
            if (GateReads)
            {
                Entered.Signal();
                if (!Release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("Base read gate was not released.");
            }
            return new(AgentFileReadState.Read, Encoding.UTF8.GetBytes(text));
        }

        public Task<AgentFileReadResult> ReadAsync(string fullPath, int maximumBytes, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public void Dispose()
        {
            Entered.Dispose();
            Release.Dispose();
        }
    }
}
