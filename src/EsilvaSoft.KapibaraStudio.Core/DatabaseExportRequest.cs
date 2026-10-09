namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Defines a bounded logical export of all collections in a database.</summary>
public sealed record DatabaseExportRequest(string Database, int DocumentsPerCollectionLimit = 100_000)
{
    public IProgress<DatabaseExportProgress>? Progress { get; init; }

    public DatabaseExportRequest Validate()
    {
        if (string.IsNullOrWhiteSpace(Database))
        {
            throw new ArgumentException("O banco de dados é obrigatório.", nameof(Database));
        }

        if (DocumentsPerCollectionLimit is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(DocumentsPerCollectionLimit), "O limite por coleção deve estar entre 1 e 1000000.");
        }

        return this;
    }
}

public enum DatabaseExportStage
{
    Exporting,
    Completed
}

/// <summary>Bounded status snapshot for logical export.</summary>
public sealed record DatabaseExportProgress(DatabaseExportStage Stage, int CompletedCollections,
    int TotalCollections, string? CurrentCollection, int CurrentCollectionDocuments, long TotalDocuments);
