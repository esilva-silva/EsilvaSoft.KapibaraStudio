using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using LiteDB;
using SystemTextJsonSerializer = System.Text.Json.JsonSerializer;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public sealed partial class LiteDbConnectionProfileRepository
{
    private const string ImportCheckpointCollectionName = "importCheckpointsV1";

    Task<IReadOnlyList<ImportCheckpoint>> IImportCheckpointRepository.GetPendingAsync(CancellationToken cancellationToken) =>
        RunAsync(() => (IReadOnlyList<ImportCheckpoint>)ReadImportCheckpoints().ToArray(), cancellationToken);

    Task IImportCheckpointRepository.CreateAsync(ImportCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        checkpoint.Validate();
        return RunAsync(() =>
        {
            var pending = ReadImportCheckpoints();
            if (pending.Count >= 100)
                throw new InvalidDataException("O limite de 100 checkpoints pendentes foi atingido; nenhuma alteração foi feita.");
            if (pending.Any(item => item.ProfileId == checkpoint.ProfileId
                && item.SourceGenerationId == checkpoint.SourceGenerationId
                && string.Equals(item.TargetDatabase, checkpoint.TargetDatabase, StringComparison.Ordinal)))
                throw new InvalidOperationException("Já existe checkpoint pendente para este banco de destino.");
            var collection = _database.GetCollection(ImportCheckpointCollectionName);
            if (collection.FindById(checkpoint.Id.ToString("N")) is not null)
                throw new InvalidOperationException("Checkpoint de importação duplicado.");
            collection.Insert(ToImportCheckpointDocument(checkpoint));
        }, cancellationToken);
    }

    Task IImportCheckpointRepository.UpdateAsync(ImportCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        checkpoint.Validate();
        return RunAsync(() =>
        {
            var collection = _database.GetCollection(ImportCheckpointCollectionName);
            var existing = ReadImportCheckpoint(collection.FindById(checkpoint.Id.ToString("N"))
                ?? throw new InvalidOperationException("Checkpoint de importação não encontrado."));
            if (existing with
                {
                    ProcessedDocuments = checkpoint.ProcessedDocuments,
                    InsertedDocuments = checkpoint.InsertedDocuments,
                    ModifiedDocuments = checkpoint.ModifiedDocuments,
                    IgnoredDocuments = checkpoint.IgnoredDocuments,
                    State = checkpoint.State,
                    UpdatedAtUtc = checkpoint.UpdatedAtUtc
                } != checkpoint
                || checkpoint.ProcessedDocuments < existing.ProcessedDocuments
                || checkpoint.InsertedDocuments < existing.InsertedDocuments
                || checkpoint.ModifiedDocuments < existing.ModifiedDocuments
                || checkpoint.IgnoredDocuments < existing.IgnoredDocuments
                || checkpoint.UpdatedAtUtc < existing.UpdatedAtUtc
                || existing.State == ImportCheckpointState.NeedsReview && checkpoint.State != ImportCheckpointState.NeedsReview
                || existing.State == ImportCheckpointState.Writing && checkpoint.State == ImportCheckpointState.Prepared)
                throw new InvalidOperationException("Atualização incompatível do checkpoint de importação.");
            collection.Update(ToImportCheckpointDocument(checkpoint));
        }, cancellationToken);
    }

    Task IImportCheckpointRepository.ResetForRestartAsync(ImportCheckpoint expected, ImportCheckpoint restarted,
        CancellationToken cancellationToken)
    {
        expected.Validate();
        restarted.Validate();
        if (restarted != (expected with
            {
                ProcessedDocuments = 0,
                InsertedDocuments = 0,
                ModifiedDocuments = 0,
                IgnoredDocuments = 0,
                State = ImportCheckpointState.Prepared,
                UpdatedAtUtc = restarted.UpdatedAtUtc
            }) || restarted.UpdatedAtUtc <= expected.UpdatedAtUtc)
            throw new InvalidOperationException("O reinício não corresponde ao checkpoint selecionado.");
        return RunAsync(() =>
        {
            var collection = _database.GetCollection(ImportCheckpointCollectionName);
            var current = ReadImportCheckpoint(collection.FindById(expected.Id.ToString("N"))
                ?? throw new InvalidOperationException("Checkpoint de importação não encontrado."));
            if (current != expected)
                throw new InvalidOperationException("O checkpoint mudou desde a verificação; confira novamente antes de reiniciar.");
            collection.Update(ToImportCheckpointDocument(restarted));
        }, cancellationToken);
    }

    Task IImportCheckpointRepository.CompleteAsync(Guid id, CancellationToken cancellationToken) =>
        RunAsync(() =>
        {
            var collection = _database.GetCollection(ImportCheckpointCollectionName);
            var document = collection.FindById(id.ToString("N"))
                ?? throw new InvalidOperationException("Checkpoint de importação não encontrado.");
            _ = ReadImportCheckpoint(document);
            collection.Delete(id.ToString("N"));
        }, cancellationToken);

    private List<ImportCheckpoint> ReadImportCheckpoints()
    {
        var documents = _database.GetCollection(ImportCheckpointCollectionName).FindAll().Take(101).ToArray();
        if (documents.Length > 100)
            throw new InvalidDataException("Há checkpoints de importação demais; nenhuma alteração foi feita.");
        return documents.Select(ReadImportCheckpoint).ToList();
    }

    private static ImportCheckpoint ReadImportCheckpoint(BsonDocument document)
    {
        try
        {
            return (SystemTextJsonSerializer.Deserialize<ImportCheckpoint>(document["json"].AsString)
                ?? throw new InvalidDataException("Checkpoint de importação vazio.")).Validate();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or KeyNotFoundException or InvalidCastException)
        {
            throw new InvalidDataException("Checkpoint de importação ilegível; ele foi preservado.", exception);
        }
    }

    private static BsonDocument ToImportCheckpointDocument(ImportCheckpoint checkpoint) => new()
    {
        ["_id"] = checkpoint.Id.ToString("N"),
        ["json"] = SystemTextJsonSerializer.Serialize(checkpoint.Validate())
    };
}
