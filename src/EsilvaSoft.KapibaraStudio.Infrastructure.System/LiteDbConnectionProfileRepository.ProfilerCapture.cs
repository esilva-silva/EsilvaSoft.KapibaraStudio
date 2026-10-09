using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using LiteDB;
using SystemTextJsonSerializer = System.Text.Json.JsonSerializer;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string ProfilerCaptureCollectionName = "profilerCaptureRecoveryV1";

    public Task<IReadOnlyList<ProfilerCaptureTicket>> GetPendingAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => (IReadOnlyList<ProfilerCaptureTicket>)ReadProfilerTickets().ToArray(), cancellationToken);

    public Task CreateAsync(ProfilerCaptureTicket ticket, CancellationToken cancellationToken = default)
    {
        ticket.Validate();
        return RunAsync(() =>
        {
            var pending = ReadProfilerTickets();
            if (pending.Any(item => item.ProfileId == ticket.ProfileId
                && string.Equals(item.Database, ticket.Database, StringComparison.Ordinal)))
                throw new InvalidOperationException("Já existe recuperação pendente do profiler para esta conexão e banco.");
            var collection = _database.GetCollection(ProfilerCaptureCollectionName);
            if (collection.FindById(ticket.Id.ToString("N")) is not null)
                throw new InvalidOperationException("Registro de recuperação duplicado.");
            collection.Insert(ToProfilerDocument(ticket));
        }, cancellationToken);
    }

    public Task UpdateAsync(ProfilerCaptureTicket ticket, CancellationToken cancellationToken = default)
    {
        ticket.Validate();
        return RunAsync(() =>
        {
            var collection = _database.GetCollection(ProfilerCaptureCollectionName);
            var existing = ReadProfilerTicket(collection.FindById(ticket.Id.ToString("N"))
                ?? throw new InvalidOperationException("Registro de recuperação não encontrado."));
            if (existing with { State = ticket.State } != ticket
                || existing.State == ProfilerCaptureState.Uncertain && ticket.State != ProfilerCaptureState.Uncertain
                || existing.State == ProfilerCaptureState.Active && ticket.State == ProfilerCaptureState.Prepared)
                throw new InvalidOperationException("Atualização incompatível do registro de recuperação.");
            collection.Update(ToProfilerDocument(ticket));
        }, cancellationToken);
    }

    public Task CompleteAsync(Guid id, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        var collection = _database.GetCollection(ProfilerCaptureCollectionName);
        var document = collection.FindById(id.ToString("N"))
            ?? throw new InvalidOperationException("Registro de recuperação não encontrado.");
        _ = ReadProfilerTicket(document);
        collection.Delete(id.ToString("N"));
    }, cancellationToken);

    private List<ProfilerCaptureTicket> ReadProfilerTickets()
    {
        var documents = _database.GetCollection(ProfilerCaptureCollectionName).FindAll().Take(101).ToArray();
        if (documents.Length > 100)
            throw new InvalidDataException("Há registros de recuperação demais; nenhuma alteração será feita.");
        return documents.Select(ReadProfilerTicket).ToList();
    }

    private static ProfilerCaptureTicket ReadProfilerTicket(BsonDocument document)
    {
        try
        {
            return (SystemTextJsonSerializer.Deserialize<ProfilerCaptureTicket>(document["json"].AsString)
                ?? throw new InvalidDataException("Registro de recuperação vazio.")).Validate();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or KeyNotFoundException)
        {
            throw new InvalidDataException("Registro de recuperação do profiler ilegível; ele foi preservado.", exception);
        }
    }

    private static BsonDocument ToProfilerDocument(ProfilerCaptureTicket ticket) => new()
    {
        ["_id"] = ticket.Id.ToString("N"),
        ["json"] = SystemTextJsonSerializer.Serialize(ticket.Validate())
    };
}
