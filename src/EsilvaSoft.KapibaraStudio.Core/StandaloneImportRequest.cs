namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Imports a standalone NDJSON or mapped CSV file into one new collection.</summary>
public sealed record StandaloneImportRequest(
    string SourceFile,
    string TargetDatabase,
    string TargetCollection,
    TransferImportSchema Schema,
    DatabaseImportDuplicatePolicy DuplicatePolicy,
    string ConfirmationDatabase)
{
    public IProgress<DatabaseImportProgress>? Progress { get; init; }
    public Guid? RestartCheckpointId { get; init; }

    public StandaloneImportRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(SourceFile)) throw new ArgumentException("O arquivo de origem é obrigatório.", nameof(SourceFile));
        if (string.IsNullOrWhiteSpace(TargetDatabase)) throw new ArgumentException("O banco de destino é obrigatório.", nameof(TargetDatabase));
        if (string.IsNullOrWhiteSpace(TargetCollection)
            || TargetCollection.Contains('\0')
            || TargetCollection.Contains('$')
            || TargetCollection.Contains('.')
            || TargetCollection.StartsWith("system.", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A coleção de destino é inválida ou protegida.", nameof(TargetCollection));
        if (!Enum.IsDefined(DuplicatePolicy)) throw new ArgumentOutOfRangeException(nameof(DuplicatePolicy));
        if (RestartCheckpointId == Guid.Empty)
            throw new ArgumentException("O checkpoint selecionado é inválido.", nameof(RestartCheckpointId));
        if (!string.Equals(TargetDatabase, ConfirmationDatabase, StringComparison.Ordinal))
            throw new ArgumentException("Confirme o nome exato do banco de destino.", nameof(ConfirmationDatabase));
        Schema.Validate();
        return this;
    }
}

public sealed record StandaloneImportResult(string SourceFile, string TargetDatabase, string TargetCollection, long DocumentCount);

public sealed record TransferPreviewField(string Name, string Type, string? SourceColumn = null);
public sealed record TransferDocumentPreview(IReadOnlyList<TransferPreviewField> Fields, int SampledRows, bool HasMoreRows);
