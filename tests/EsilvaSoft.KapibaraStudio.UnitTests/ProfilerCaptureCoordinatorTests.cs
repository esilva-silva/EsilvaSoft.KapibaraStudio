using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class ProfilerCaptureCoordinatorTests
{
    private const string Initial = """{"was":0,"slowms":100,"sampleRate":1}""";
    private const string Enabled = """{"was":1,"slowms":50,"sampleRate":0.5}""";
    private const string Topology = """{"topologyVersion":{"processId":{"$oid":"507f1f77bcf86cd799439011"}},"isWritablePrimary":true}""";
    private static readonly string[] StatusReadOnlyCall = ["GetProfilerStatusAsync"];

    [Test]
    public void PersistenceFailurePreventsProfilerCommand()
    {
        var repository = new MemoryCaptureRepository { FailCreate = true };
        var commands = 0;
        var mongo = Proxy((method, _) => method switch
        {
            "GetProfilerStatusAsync" => Task.FromResult(Initial),
            "GetTopologyAsync" => Task.FromResult(Topology),
            "ConfigureProfilerAsync" => CountCommand(),
            _ => throw new InvalidOperationException(method)
        });
        Task<ProfilerConfigurationResult> CountCommand()
        {
            commands++;
            return Task.FromResult(new ProfilerConfigurationResult(1, 50, 0.5m, false));
        }
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var coordinator = new ProfilerCaptureCoordinator(repository, mongo);

        Assert.That(async () => await coordinator.StartAsync(profile, Request(), TimeSpan.FromMinutes(10)),
            Throws.TypeOf<IOException>());
        Assert.That(commands, Is.Zero);
    }

    [Test]
    public void FilteredBaselineIsRefusedBeforePersistenceOrCommand()
    {
        var repository = new MemoryCaptureRepository();
        var calls = new List<string>();
        var mongo = Proxy((method, _) =>
        {
            calls.Add(method);
            return method switch
            {
                "GetProfilerStatusAsync" => Task.FromResult("""{"was":0,"slowms":100,"sampleRate":1,"filter":{"op":"query"}}"""),
                _ => throw new InvalidOperationException(method)
            };
        });

        Assert.That(async () => await new ProfilerCaptureCoordinator(repository, mongo)
            .StartAsync(ConnectionProfile.Create("local", "mongodb://localhost"),
                Request(), TimeSpan.FromMinutes(10)), Throws.TypeOf<InvalidOperationException>());
        Assert.Multiple(() =>
        {
            Assert.That(repository.Items, Is.Empty);
            Assert.That(calls, Is.EqualTo(StatusReadOnlyCall));
        });
    }

    [Test]
    public async Task StartCollectRestoreKeepsReceiptUntilVerified()
    {
        var repository = new MemoryCaptureRepository();
        var status = Initial;
        var commands = 0;
        var mongo = Proxy((method, args) => method switch
        {
            "GetProfilerStatusAsync" => Task.FromResult(status),
            "GetTopologyAsync" => Task.FromResult(Topology),
            "ConfigureProfilerAsync" => Configure((ProfilerConfigurationRequest)args[1]!),
            "ReadProfilerCaptureAsync" => Task.FromResult(new ProfilerCapturePage(
                [new ProfilerCaptureEntry(DateTimeOffset.UtcNow, "query", "sample_mflix.movies", 7, "COLLSCAN")],
                false, (DateTimeOffset)args[3]!)),
            _ => throw new InvalidOperationException(method)
        });
        Task<ProfilerConfigurationResult> Configure(ProfilerConfigurationRequest request)
        {
            Assert.That(repository.Items, Has.Count.EqualTo(1), "A recuperação deve existir antes do comando.");
            commands++;
            status = request.Level == 0 ? Initial : Enabled;
            return Task.FromResult(new ProfilerConfigurationResult(request.Level,
                request.SlowMs, request.SampleRate, false));
        }
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var coordinator = new ProfilerCaptureCoordinator(repository, mongo);

        var ticket = await coordinator.StartAsync(profile, Request(), TimeSpan.FromMinutes(10));
        var pendingAfterRestart = await new ProfilerCaptureCoordinator(repository, mongo).GetPendingAsync();
        var page = await coordinator.CollectAsync(profile, ticket.Id);
        await coordinator.RestoreAsync(profile, ticket.Id, "sample_mflix");

        Assert.Multiple(() =>
        {
            Assert.That(ticket.State, Is.EqualTo(ProfilerCaptureState.Active));
            Assert.That(pendingAfterRestart, Has.Count.EqualTo(1));
            Assert.That(page.Entries, Has.Count.EqualTo(1));
            Assert.That(page.Entries[0].PlanSummary, Is.EqualTo("COLLSCAN"));
            Assert.That(commands, Is.EqualTo(2));
            Assert.That(repository.Items, Is.Empty);
        });
    }

    [Test]
    public async Task ConcurrentChangeBlocksRestoreAndRetainsReceipt()
    {
        var repository = new MemoryCaptureRepository();
        var status = Initial;
        var commands = 0;
        var mongo = Proxy((method, _) => method switch
        {
            "GetProfilerStatusAsync" => Task.FromResult(status),
            "GetTopologyAsync" => Task.FromResult(Topology),
            "ConfigureProfilerAsync" => Configure(),
            _ => throw new InvalidOperationException(method)
        });
        Task<ProfilerConfigurationResult> Configure()
        {
            commands++;
            status = Enabled;
            return Task.FromResult(new ProfilerConfigurationResult(1, 50, 0.5m, false));
        }
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");
        var coordinator = new ProfilerCaptureCoordinator(repository, mongo);
        var ticket = await coordinator.StartAsync(profile, Request(), TimeSpan.FromMinutes(10));
        status = """{"was":1,"slowms":25,"sampleRate":0.5}""";

        Assert.That(async () => await coordinator.RestoreAsync(profile, ticket.Id, "sample_mflix"),
            Throws.TypeOf<InvalidOperationException>());
        Assert.Multiple(() =>
        {
            Assert.That(commands, Is.EqualTo(1));
            Assert.That(repository.Items, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void CancellationAfterSubmissionLeavesDurableUncertainReceipt()
    {
        var repository = new MemoryCaptureRepository();
        var mongo = Proxy((method, _) => method switch
        {
            "GetProfilerStatusAsync" => Task.FromResult(Initial),
            "GetTopologyAsync" => Task.FromResult(Topology),
            "ConfigureProfilerAsync" => Task.FromException<ProfilerConfigurationResult>(
                new OperationCanceledException("server outcome unknown")),
            _ => throw new InvalidOperationException(method)
        });
        var profile = ConnectionProfile.Create("local", "mongodb://localhost");

        Assert.That(async () => await new ProfilerCaptureCoordinator(repository, mongo)
            .StartAsync(profile, Request(), TimeSpan.FromMinutes(10)),
            Throws.TypeOf<OperationCanceledException>());
        Assert.That(repository.Items.Single().State, Is.EqualTo(ProfilerCaptureState.Uncertain));
    }

    private static ProfilerConfigurationRequest Request() => new(
        "sample_mflix", 1, 50, 0.5m, ProfilerFilterMode.Unset,
        null, "sample_mflix", Initial, Topology);

    private static IMongoWorkspaceService Proxy(Func<string, object?[], object?> handler)
    {
        var mongo = DispatchProxy.Create<IMongoWorkspaceService, MongoTestProxy>();
        ((MongoTestProxy)mongo).Handler = handler;
        return mongo;
    }

    private sealed class MemoryCaptureRepository : IProfilerCaptureRepository
    {
        public List<ProfilerCaptureTicket> Items { get; } = [];
        public bool FailCreate { get; init; }
        public Task<IReadOnlyList<ProfilerCaptureTicket>> GetPendingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ProfilerCaptureTicket>>(Items.ToArray());
        public Task CreateAsync(ProfilerCaptureTicket ticket, CancellationToken cancellationToken = default)
        {
            if (FailCreate) throw new IOException("disk failure");
            Items.Add(ticket);
            return Task.CompletedTask;
        }
        public Task UpdateAsync(ProfilerCaptureTicket ticket, CancellationToken cancellationToken = default)
        {
            Items[Items.FindIndex(item => item.Id == ticket.Id)] = ticket;
            return Task.CompletedTask;
        }
        public Task CompleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            Items.RemoveAll(item => item.Id == id);
            return Task.CompletedTask;
        }
    }
}
