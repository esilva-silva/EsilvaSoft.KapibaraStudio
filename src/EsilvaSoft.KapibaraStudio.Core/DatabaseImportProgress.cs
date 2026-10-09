namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Reports bounded, secret-free progress for one logical database import.</summary>
public enum DatabaseImportStage { Validating, CreatingCollections, ImportingDocuments, CreatingViews, CleaningUp, Completed, Failed }

public sealed record DatabaseImportProgress(
    DatabaseImportStage Stage,
    string? CurrentCollection,
    int CompletedCollections,
    int TotalCollections,
    long ProcessedDocuments,
    long TotalDocuments,
    long InsertedDocuments,
    long ModifiedDocuments,
    long IgnoredDocuments,
    long FailedDocuments,
    bool OutcomeMayBePartial = false);
