using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ProfilerCaptureCoordinatorTests
{
    private const string Topology = """{"topologyVersion":{"processId":{"$oid":"507f1f77bcf86cd799439011"}},"isWritablePrimary":true}""";
    private const string Applied = """{"was":1,"slowms":50,"sampleRate":0.5}""";

    [Test]
    public async Task ListingPendingDoesNotContactMongo()
    {
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var repository = new MemoryCaptureRepository(Ticket(profile));
        var mongo = Proxy((method, _) => throw new AssertionException($"Unexpected Mongo call: {method}"));
        Assert.That(await new ProfilerCaptureCoordinator(repository, mongo).GetPendingAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RestoreCompletesOnlyAfterVerifiedResult()
    {
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var ticket = Ticket(profile);
        var repository = new MemoryCaptureRepository(ticket);
        var mongo = Proxy((method, _) => method switch
        {
            "GetTopologyAsync" => Task.FromResult(Topology),
            "GetProfilerStatusAsync" => Task.FromResult(Applied),
            "RestoreProfilerSettingsAsync" => Task.FromResult(new ProfilerConfigurationResult(0, 100, 1m, false)),
            _ => throw new AssertionException(method)
        });
        await new ProfilerCaptureCoordinator(repository, mongo).RestoreAsync(profile, ticket.Id, ticket.Database);
        Assert.That(repository.Items, Is.Empty);
    }

    [Test]
    public void ConcurrentChangeKeepsTicketAndDoesNotSendCommand()
    {
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var ticket = Ticket(profile);
        var repository = new MemoryCaptureRepository(ticket);
        var mongo = Proxy((method, _) => method switch
        {
            "GetTopologyAsync" => Task.FromResult(Topology),
            "GetProfilerStatusAsync" => Task.FromResult("""{"was":1,"slowms":25,"sampleRate":0.5}"""),
            _ => throw new AssertionException(method)
        });
        Assert.That(async () => await new ProfilerCaptureCoordinator(repository, mongo).RestoreAsync(profile, ticket.Id, ticket.Database), Throws.TypeOf<InvalidOperationException>());
        Assert.That(repository.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void CancellationAfterSubmissionLeavesUncertainTicket()
    {
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var ticket = Ticket(profile);
        var repository = new MemoryCaptureRepository(ticket);
        var mongo = Proxy((method, _) => method switch
        {
            "GetTopologyAsync" => Task.FromResult(Topology),
            "GetProfilerStatusAsync" => Task.FromResult(Applied),
            "RestoreProfilerSettingsAsync" => Task.FromException<ProfilerConfigurationResult>(new OperationCanceledException()),
            _ => throw new AssertionException(method)
        });
        Assert.That(async () => await new ProfilerCaptureCoordinator(repository, mongo).RestoreAsync(profile, ticket.Id, ticket.Database), Throws.TypeOf<OperationCanceledException>());
        Assert.That(repository.Items.Single().State, Is.EqualTo(ProfilerCaptureState.Uncertain));
    }

    [Test]
    public void UnconfirmedReadbackRetainsUncertainTicket()
    {
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var ticket = Ticket(profile);
        var repository = new MemoryCaptureRepository(ticket);
        var mongo = Proxy((method, _) => method switch
        {
            "GetTopologyAsync" => Task.FromResult(Topology),
            "GetProfilerStatusAsync" => Task.FromResult(Applied),
            "RestoreProfilerSettingsAsync" => Task.FromResult(new ProfilerConfigurationResult(1, 50, 0.5m, false)),
            _ => throw new AssertionException(method)
        });
        Assert.That(async () => await new ProfilerCaptureCoordinator(repository, mongo).RestoreAsync(profile, ticket.Id, ticket.Database), Throws.TypeOf<InvalidOperationException>());
        Assert.That(repository.Items.Single().State, Is.EqualTo(ProfilerCaptureState.Uncertain));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void WrongProfileOrConfirmationNeverContactsMongo(bool wrongProfile)
    {
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var ticket = Ticket(profile);
        var repository = new MemoryCaptureRepository(ticket);
        var mongo = Proxy((method, _) => throw new AssertionException($"Unexpected Mongo call: {method}"));
        var selected = wrongProfile ? ConnectionProfile.Create("other", "mongodb://localhost") : profile;
        var confirmation = wrongProfile ? ticket.Database : "other_database";
        Assert.That(async () => await new ProfilerCaptureCoordinator(repository, mongo).RestoreAsync(selected, ticket.Id, confirmation),
            wrongProfile ? Throws.TypeOf<InvalidOperationException>() : Throws.TypeOf<ArgumentException>());
        Assert.That(repository.Items.Single(), Is.EqualTo(ticket));
    }

    [Test]
    public void ChangedNodeNeverRestoresOldTicket()
    {
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var ticket = Ticket(profile);
        var repository = new MemoryCaptureRepository(ticket);
        var mongo = Proxy((method, _) => method == "GetTopologyAsync"
            ? Task.FromResult(Topology.Replace("507f1f77bcf86cd799439011", "507f1f77bcf86cd799439012", StringComparison.Ordinal))
            : throw new AssertionException(method));
        Assert.That(async () => await new ProfilerCaptureCoordinator(repository, mongo).RestoreAsync(profile, ticket.Id, ticket.Database), Throws.TypeOf<InvalidOperationException>());
        Assert.That(repository.Items.Single(), Is.EqualTo(ticket));
    }

    [Test]
    public void FailedCompletionKeepsRecoveryPossible()
    {
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var ticket = Ticket(profile);
        var repository = new MemoryCaptureRepository(ticket) { FailComplete = true };
        var mongo = Proxy((method, _) => method switch
        {
            "GetTopologyAsync" => Task.FromResult(Topology),
            "GetProfilerStatusAsync" => Task.FromResult(Applied),
            "RestoreProfilerSettingsAsync" => Task.FromResult(new ProfilerConfigurationResult(0, 100, 1m, false)),
            _ => throw new AssertionException(method)
        });
        Assert.That(async () => await new ProfilerCaptureCoordinator(repository, mongo).RestoreAsync(profile, ticket.Id, ticket.Database), Throws.TypeOf<IOException>());
        Assert.That(repository.Items.Single().State, Is.EqualTo(ProfilerCaptureState.Uncertain));
    }

    private static ProfilerCaptureTicket Ticket(ConnectionProfile profile)
    {
        var now = DateTimeOffset.UtcNow;
        return new(1, Guid.NewGuid(), profile.Id, "sample_mflix", ProfilerCaptureTopology.GetNodeIdentity(Topology), now, now.AddMinutes(10),
            new ProfilerSettings(0, 100, 1m, null), new ProfilerSettings(1, 50, 0.5m, null), ProfilerCaptureState.Active);
    }

    private static IMongoWorkspaceService Proxy(Func<string, object?[], object?> handler)
    {
        var mongo = DispatchProxy.Create<IMongoWorkspaceService, MongoTestProxy>();
        ((MongoTestProxy)mongo).Handler = handler;
        return mongo;
    }

    private sealed class MemoryCaptureRepository(ProfilerCaptureTicket ticket) : IProfilerCaptureRepository
    {
        public List<ProfilerCaptureTicket> Items { get; } = [ticket];
        public bool FailComplete { get; init; }
        public Task<IReadOnlyList<ProfilerCaptureTicket>> GetPendingAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ProfilerCaptureTicket>>(Items.ToArray());
        public Task CreateAsync(ProfilerCaptureTicket item, CancellationToken cancellationToken = default) { Items.Add(item); return Task.CompletedTask; }
        public Task UpdateAsync(ProfilerCaptureTicket item, CancellationToken cancellationToken = default) { Items[Items.FindIndex(x => x.Id == item.Id)] = item; return Task.CompletedTask; }
        public Task CompleteAsync(Guid id, CancellationToken cancellationToken = default) { if (FailComplete) throw new IOException("Synthetic persistence failure"); Items.RemoveAll(x => x.Id == id); return Task.CompletedTask; }
    }
}
