using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Restores a persisted profiler recovery ticket after explicit human confirmation.</summary>
public sealed class ProfilerCaptureCoordinator(
    IProfilerCaptureRepository repository,
    IMongoWorkspaceService mongo)
{
    public Task<IReadOnlyList<ProfilerCaptureTicket>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        repository.GetPendingAsync(cancellationToken);

    public async Task RestoreAsync(ConnectionProfile profile, Guid ticketId,
        string confirmationDatabase, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        var ticket = await FindTicketAsync(profile, ticketId, cancellationToken).ConfigureAwait(false);
        var confirmedDatabase = confirmationDatabase?.Trim();
        if (!string.Equals(ticket.Database, confirmedDatabase, StringComparison.Ordinal))
            throw new ArgumentException("Confirme o nome exato do banco para restaurar.", nameof(confirmationDatabase));
        var topology = await mongo.GetTopologyAsync(profile, cancellationToken).ConfigureAwait(false);
        if (ProfilerCaptureTopology.GetNodeIdentity(topology) != ticket.NodeIdentity)
            throw new InvalidOperationException("O processo MongoDB mudou; restauração automática recusada.");
        var status = await mongo.GetProfilerStatusAsync(profile, ticket.Database,
            cancellationToken).ConfigureAwait(false);
        var observed = ProfilerSettings.Parse(status);
        if (observed == ticket.Previous)
        {
            await repository.CompleteAsync(ticket.Id, cancellationToken).ConfigureAwait(false);
            return;
        }
        if (observed != ticket.Applied)
            throw new InvalidOperationException("A configuração foi alterada por outro ator; restauração recusada.");

        try
        {
            var result = await mongo.RestoreProfilerSettingsAsync(profile, ticket, confirmedDatabase!, status, topology, cancellationToken).ConfigureAwait(false);
            if (result.Level != ticket.Previous.Level || result.SlowMs != ticket.Previous.SlowMs
                || result.SampleRate != ticket.Previous.SampleRate || result.FilterConfigured)
                throw new InvalidOperationException("A restauração foi enviada, mas a releitura não confirmou o estado anterior.");
            await repository.CompleteAsync(ticket.Id, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            await MarkUncertainAsync(ticket).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<ProfilerCaptureTicket> FindTicketAsync(ConnectionProfile profile, Guid id,
        CancellationToken cancellationToken)
    {
        var pending = await repository.GetPendingAsync(cancellationToken).ConfigureAwait(false);
        return pending.SingleOrDefault(ticket => ticket.Id == id && ticket.ProfileId == profile.Id)
            ?? throw new InvalidOperationException("Registro de recuperação não encontrado para esta conexão.");
    }

    private async Task MarkUncertainAsync(ProfilerCaptureTicket ticket)
    {
        // Use an uncancelled token: a cancellation after dispatch still needs a durable warning.
        await repository.UpdateAsync(ticket with { State = ProfilerCaptureState.Uncertain },
            CancellationToken.None).ConfigureAwait(false);
    }
}
