namespace EsilvaSoft.KapibaraStudio.Core;

public enum DatabaseImportObjectKind { Collection, View }

/// <summary>One manifest namespace and its read-only collision state at preview time.</summary>
public sealed record DatabaseImportPreviewItem(
    DatabaseImportObjectKind Kind,
    string Namespace,
    long DocumentCount,
    bool DestinationNamespaceExists);

/// <summary>
/// Read-only logical package plan. The opaque fingerprint binds the profile generation, package content,
/// destination database and policy to the destination namespace set observed during preview.
/// </summary>
public sealed record DatabaseImportPreview(
    string TargetDatabase,
    DatabaseImportDuplicatePolicy DuplicatePolicy,
    bool RestoreDefinitions,
    IReadOnlyList<DatabaseImportPreviewItem> Items,
    IReadOnlyList<string> DestinationNamespaces,
    bool DestinationIsEmpty,
    long TotalDocuments,
    string Fingerprint,
    bool DestinationCanBeUpsertedInto = false)
{
    public bool CanImport => DestinationIsEmpty
        || (DuplicatePolicy == DatabaseImportDuplicatePolicy.Upsert
            && !RestoreDefinitions
            && DestinationCanBeUpsertedInto);
}
