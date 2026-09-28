namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Describes a completed logical import that upserts documents by their identifiers.</summary>
public sealed record DatabaseImportResult(
    string SourceDirectory,
    string TargetDatabase,
    int CollectionCount,
    long DocumentCount);
