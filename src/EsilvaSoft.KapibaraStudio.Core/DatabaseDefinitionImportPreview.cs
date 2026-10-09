namespace EsilvaSoft.KapibaraStudio.Core;

public enum DatabaseDefinitionPlannedStatus
{
    Attempt,
    Omitted,
    Blocked
}

/// <summary>A read-only plan. Actual import revalidates source and destination before writing.</summary>
public sealed record DatabaseDefinitionImportPreviewItem(
    DatabaseDefinitionKind Kind,
    string Target,
    string? Name,
    IReadOnlyList<string> DependsOn,
    DatabaseDefinitionCollision Collision,
    DatabaseDefinitionPlannedStatus Status);

public sealed record DatabaseDefinitionImportPreview(IReadOnlyList<DatabaseDefinitionImportPreviewItem> Items);
