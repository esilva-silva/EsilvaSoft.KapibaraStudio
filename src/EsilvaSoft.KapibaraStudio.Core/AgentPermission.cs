namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Canonical agent authorization capabilities. Every value is denied unless explicitly granted.</summary>
#pragma warning disable CA1711 // Permission is the normative domain term used by the Phase 7 authorization contract.
public enum AgentPermission
{
    ReadMetadata = 0,
    ReadSchema = 1,
    ReadDiagnostics = 2,
    ExecuteReadQueries = 3,
    ReadDocuments = 4,
    InsertDocuments = 5,
    UpdateDocuments = 6,
    DeleteDocuments = 7,
    CreateIndexes = 8,
    DropIndexes = 9,
    AdministrativeOperations = 10,
    /// <summary>Create-only workspace files, authorized by the captured file-writing opt-in and one-shot confirmation.</summary>
    CreateWorkspaceFiles = 11
}
#pragma warning restore CA1711
