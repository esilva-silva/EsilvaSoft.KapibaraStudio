namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Describes the files produced by a bounded logical database export.</summary>
public sealed record DatabaseExportResult(
    string OutputDirectory,
    int CollectionCount,
    long DocumentCount,
    bool IsTruncated);
