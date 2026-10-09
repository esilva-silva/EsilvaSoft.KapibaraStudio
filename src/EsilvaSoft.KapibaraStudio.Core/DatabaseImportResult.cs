namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Describes a completed logical import using the caller's explicit duplicate policy.</summary>
public sealed record DatabaseImportResult(
    string SourceDirectory,
    string TargetDatabase,
    int CollectionCount,
    long DocumentCount)
{
    public DatabaseDefinitionRestoreReport? DefinitionRestoreReport { get; init; }
}
