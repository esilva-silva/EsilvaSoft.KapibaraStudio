using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture, NonParallelizable]
public sealed class ProfilerRecoveryViewModelTests
{
    [Test]
    public void OpeningListsRecoveryWithoutContactingMongo()
    {
        using var repository = new MemoryWorkspaceRepository();
        var tickets = new RecoveryRepository();
        var viewModel = Create(repository, tickets);
        Assert.That(viewModel.PendingProfilerCaptures.Single(), Is.EqualTo(tickets.Ticket));
        Assert.That(viewModel.ProfilerCaptureWarning, Is.Not.Empty);
    }

    [Test]
    public void UnreadableRecoveryIsVisibleAndDoesNotDeleteData()
    {
        using var repository = new MemoryWorkspaceRepository();
        var tickets = new RecoveryRepository { FailRead = true };
        var viewModel = Create(repository, tickets);
        Assert.That(viewModel.ProfilerCaptureWarning, Does.Contain("synthetic-corruption"));
        Assert.That(tickets.MutationCount, Is.Zero);
    }

    [Test]
    public async Task WrongConfirmationDoesNotRestoreOrDiscardTicket()
    {
        using var repository = new MemoryWorkspaceRepository();
        var tickets = new RecoveryRepository();
        var viewModel = Create(repository, tickets);
        viewModel.SelectedProfile = new(tickets.Ticket.ProfileId, "Original", "mongodb://localhost", "fixture");
        viewModel.ProfilerConfirmation = "another_database";
        await viewModel.RestoreProfilerCaptureCommand.ExecuteAsync(null);
        Assert.That(viewModel.PendingProfilerCaptures.Single(), Is.EqualTo(tickets.Ticket));
        Assert.That(tickets.MutationCount, Is.Zero);
    }

    private static MainWindowViewModel Create(MemoryWorkspaceRepository repository, RecoveryRepository tickets)
    {
        var mongo = DispatchProxy.Create<IMongoWorkspaceService, MongoTestProxy>();
        ((MongoTestProxy)mongo).Handler = (method, _) => throw new AssertionException($"Unexpected Mongo call: {method}");
        var workspace = new WorkspaceService(repository, repository, repository, repository, repository,
            mongo, new MemoryTextFiles(), new SessionConnectionSecretStore(), profilerCaptures: tickets);
        return new(workspace, autoLoadCollections: false);
    }

    private sealed class RecoveryRepository : IProfilerCaptureRepository
    {
        public ProfilerCaptureTicket Ticket { get; } = new(1, Guid.NewGuid(), Guid.NewGuid(), "fixture",
            new string('A', 64), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1),
            new(0, 100, 1m, null), new(1, 50, 0.5m, null), ProfilerCaptureState.Uncertain);
        public bool FailRead { get; init; }
        public int MutationCount { get; private set; }
        public Task<IReadOnlyList<ProfilerCaptureTicket>> GetPendingAsync(CancellationToken cancellationToken = default) =>
            FailRead ? Task.FromException<IReadOnlyList<ProfilerCaptureTicket>>(new InvalidDataException("synthetic-corruption"))
                : Task.FromResult<IReadOnlyList<ProfilerCaptureTicket>>([Ticket]);
        public Task CreateAsync(ProfilerCaptureTicket ticket, CancellationToken cancellationToken = default) { MutationCount++; throw new AssertionException("Unexpected create"); }
        public Task UpdateAsync(ProfilerCaptureTicket ticket, CancellationToken cancellationToken = default) { MutationCount++; throw new AssertionException("Unexpected update"); }
        public Task CompleteAsync(Guid id, CancellationToken cancellationToken = default) { MutationCount++; throw new AssertionException("Unexpected complete"); }
    }
}
