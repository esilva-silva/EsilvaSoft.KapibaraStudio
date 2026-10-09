using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Human-triggered profiler window; persisted recovery is established before changing the server.</summary>
public sealed class ProfilerCaptureCoordinator(
    IProfilerCaptureRepository repository,
    IMongoWorkspaceService mongo,
    TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public Task<IReadOnlyList<ProfilerCaptureTicket>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        repository.GetPendingAsync(cancellationToken);

    public async Task<ProfilerCaptureTicket> StartAsync(ConnectionProfile profile,
        ProfilerConfigurationRequest request, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        profile.EnsureWriteAllowed();
        request.Validate();
        if (duration < TimeSpan.FromMinutes(1) || duration > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(duration), "A coleta deve durar entre 1 e 60 minutos.");
        if (request.Level == 0 || request.FilterMode == ProfilerFilterMode.Set)
            throw new NotSupportedException("A coleta requer nível 1 ou 2 sem filtro novo.");
        var before = ProfilerSettings.Parse(await mongo.GetProfilerStatusAsync(profile, request.Database,
            cancellationToken).ConfigureAwait(false));
        var expected = ProfilerSettings.Parse(request.ExpectedStatusJson);
        if (before != expected || before.FilterDigest is not null)
            throw new InvalidOperationException("O profiler mudou ou já possui filtro; esta coleta não pode restaurá-lo com segurança.");
        var topology = await mongo.GetTopologyAsync(profile, cancellationToken).ConfigureAwait(false);
        var node = ProfilerCaptureTopology.GetNodeIdentity(topology);
        if (node != ProfilerCaptureTopology.GetNodeIdentity(request.ExpectedTopologyJson))
            throw new InvalidOperationException("O processo MongoDB mudou após a prévia.");
        if (before.Level == 2 || before.SlowMs is null || before.SampleRate is null)
            throw new NotSupportedException("A configuração anterior não pode ser restaurada integralmente neste fluxo.");
        var now = _clock.GetUtcNow();
        var applied = new ProfilerSettings(request.Level, request.SlowMs ?? before.SlowMs,
            request.SampleRate ?? before.SampleRate, null);
        var ticket = new ProfilerCaptureTicket(ProfilerCaptureTicket.CurrentVersion,
            Guid.NewGuid(), profile.Id, request.Database, node, now, now + duration,
            before, applied, ProfilerCaptureState.Prepared).Validate();

        // Local persistence is the write barrier. A failed save must never send profile: 1/2.
        await repository.CreateAsync(ticket, cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await mongo.ConfigureProfilerAsync(profile, request, cancellationToken).ConfigureAwait(false);
            if (result.Level != applied.Level || result.SlowMs != applied.SlowMs
                || result.SampleRate != applied.SampleRate || result.FilterConfigured)
                throw new InvalidOperationException("O profiler foi alterado, mas o estado observado difere da coleta planejada.");
            ticket = ticket with { State = ProfilerCaptureState.Active };
            await repository.UpdateAsync(ticket, CancellationToken.None).ConfigureAwait(false);
            return ticket;
        }
        catch
        {
            await MarkUncertainAsync(ticket).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<ProfilerCapturePage> CollectAsync(ConnectionProfile profile, Guid ticketId,
        CancellationToken cancellationToken = default)
    {
        var ticket = await FindTicketAsync(profile, ticketId, cancellationToken).ConfigureAwait(false);
        var topology = await mongo.GetTopologyAsync(profile, cancellationToken).ConfigureAwait(false);
        if (ProfilerCaptureTopology.GetNodeIdentity(topology) != ticket.NodeIdentity)
            throw new InvalidOperationException("O processo MongoDB mudou; a coleta foi recusada.");
        var through = _clock.GetUtcNow();
        if (through > ticket.EndsAtUtc)
            through = ticket.EndsAtUtc;
        if (through < ticket.StartedAtUtc)
            throw new InvalidOperationException("O relógio está anterior ao início da coleta.");
        return await mongo.ReadProfilerCaptureAsync(profile, ticket.Database,
            ticket.StartedAtUtc, through, cancellationToken).ConfigureAwait(false);
    }

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

        var restore = new ProfilerConfigurationRequest(ticket.Database, ticket.Previous.Level,
            ticket.Previous.SlowMs, ticket.Previous.SampleRate, ProfilerFilterMode.Unset,
            null, confirmedDatabase!, status, topology).Validate();
        try
        {
            var result = await mongo.ConfigureProfilerAsync(profile, restore, cancellationToken).ConfigureAwait(false);
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
