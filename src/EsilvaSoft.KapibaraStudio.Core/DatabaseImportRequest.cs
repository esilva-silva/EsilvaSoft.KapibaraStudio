namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Defines an import of a KapibaraStudio logical export into a target database.</summary>
public enum DatabaseImportDuplicatePolicy { Reject, Upsert }

public sealed record DatabaseImportRequest(
    string SourceDirectory,
    string TargetDatabase,
    DatabaseImportDuplicatePolicy DuplicatePolicy = DatabaseImportDuplicatePolicy.Reject,
    string? ConfirmationDatabase = null,
    bool RestoreDefinitions = false)
{
    /// <summary>Optional runtime observer; it is never serialized into an export package.</summary>
    public IProgress<DatabaseImportProgress>? Progress { get; init; }
    public Guid? RestartCheckpointId { get; init; }
    /// <summary>Opaque fingerprint from the explicit destination preview; revalidated before any write.</summary>
    public string? PreviewFingerprint { get; init; }

    public DatabaseImportRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceDirectory))
        {
            throw new ArgumentException("A pasta de origem é obrigatória.", nameof(SourceDirectory));
        }

        if (string.IsNullOrWhiteSpace(TargetDatabase))
        {
            throw new ArgumentException("O banco de destino é obrigatório.", nameof(TargetDatabase));
        }

        if (!Enum.IsDefined(DuplicatePolicy))
            throw new ArgumentOutOfRangeException(nameof(DuplicatePolicy));

        if (RestartCheckpointId == Guid.Empty)
            throw new ArgumentException("O checkpoint selecionado é inválido.", nameof(RestartCheckpointId));

        if ((DuplicatePolicy == DatabaseImportDuplicatePolicy.Upsert || RestoreDefinitions || RestartCheckpointId is not null)
            && !string.Equals(TargetDatabase, ConfirmationDatabase, StringComparison.Ordinal))
            throw new ArgumentException("Confirme o nome exato do banco para aplicar políticas avançadas de importação.", nameof(ConfirmationDatabase));

        return this;
    }
}
