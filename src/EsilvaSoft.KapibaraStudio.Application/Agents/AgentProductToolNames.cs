using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Registry names of the product tools that an integrated agent may see through the local MCP proxy (without the MCP
/// server prefix), and the provider-native file read tools. Reads, mediated proposals and opt-in create-only files; MongoDB write
/// tools are deliberately absent. The registry lote owns the implementations of the new names.
/// </summary>
public static class AgentProductToolNames
{
    public const string ListConnections = AgentToolRegistry.ListConnectionsToolName;
    public const string ListDatabases = AgentToolRegistry.ListDatabasesToolName;
    public const string ListCollections = AgentToolRegistry.ListCollectionsToolName;
    public const string GetIndexes = AgentToolRegistry.GetIndexesToolName;
    public const string GetSearchIndexes = AgentToolRegistry.GetSearchIndexesToolName;
    public const string GetQueryResults = AgentToolRegistry.GetQueryResultsToolName;
    public const string GetQueryDiagnostics = AgentToolRegistry.GetQueryDiagnosticsToolName;
    public const string GetCollectionSchema = AgentToolRegistry.GetCollectionSchemaToolName;
    public const string MongoFind = AgentToolRegistry.MongoFindToolName;
    public const string MongoCount = AgentToolRegistry.MongoCountToolName;
    public const string SampleDocuments = AgentToolRegistry.SampleDocumentsToolName;
    public const string MongoFindOne = AgentToolRegistry.MongoFindOneToolName;
    public const string GetDocument = AgentToolRegistry.GetDocumentToolName;
    public const string MongoDistinct = AgentToolRegistry.MongoDistinctToolName;
    public const string MongoExplain = AgentToolRegistry.MongoExplainToolName;

    /// <summary>Reads the autocomplete schema cache only; never samples the database.</summary>
    public const string GetCachedSchema = "get_cached_schema";

    /// <summary>Workspace folder, active file and the tab's connection › database › collection.</summary>
    public const string GetWorkspaceContext = "get_workspace_context";

    /// <summary>Registers an edit proposal for review; never writes to disk.</summary>
    public const string CreateWorkspaceFile = AgentToolRegistry.CreateWorkspaceFileToolName;
    public const string ProposeFileEdit = "propose_file_edit";

    public const string NativeRead = "Read";
    public const string NativeGlob = "Glob";
    public const string NativeGrep = "Grep";
    public const string NativeBash = "Bash";
    public const string NativeEdit = "Edit";
    public const string NativeWrite = "Write";
    public const string NativeWebSearch = "WebSearch";
    public const string NativeWebFetch = "WebFetch";

    /// <summary>Product read tools in a stable order.</summary>
    public static IReadOnlyList<string> ReadTools { get; } =
        [ListConnections, ListDatabases, ListCollections, GetIndexes, GetSearchIndexes, GetCachedSchema, GetWorkspaceContext,
            GetQueryResults, GetQueryDiagnostics];

    /// <summary>Captured outputs can contain document values and require separate data-sending consent.</summary>
    public static bool IsMongoDocumentRead(string toolName) => toolName is
        GetQueryResults or GetQueryDiagnostics;

    /// <summary>Compatibility name retained for existing Copilot policy and regression tests.</summary>
    public static bool IsCopilotDocumentRead(string toolName) => IsMongoDocumentRead(toolName);

    /// <summary>Provider-native file read tools (Claude Code), in a stable order.</summary>
    public static IReadOnlyList<string> NativeFileReadTools { get; } = [NativeRead, NativeGlob, NativeGrep];

    public static IReadOnlyList<string> NativeCommandTools { get; } = [NativeBash];
    public static IReadOnlyList<string> NativeFileWriteTools { get; } = [NativeEdit, NativeWrite];
    public static IReadOnlyList<string> NativeNetworkTools { get; } = [NativeWebSearch, NativeWebFetch];

    /// <summary>Confirmation category of a product tool; <see cref="AgentConfirmationCategories.None"/> for unknown names.</summary>
    public static AgentConfirmationCategories CategoryOf(string toolName) => toolName switch
    {
        var name when IsMongoDocumentRead(name) => AgentConfirmationCategories.MongoDocumentRead,
        ListConnections or ListDatabases or ListCollections or GetIndexes or GetSearchIndexes or GetCachedSchema =>
            AgentConfirmationCategories.MongoMetadataRead,
        GetWorkspaceContext => AgentConfirmationCategories.WorkspaceContextRead,
        CreateWorkspaceFile => AgentConfirmationCategories.NativeFileWrite,
        ProposeFileEdit => AgentConfirmationCategories.EditProposal,
        NativeRead or NativeGlob or NativeGrep => AgentConfirmationCategories.NativeFileRead,
        NativeBash => AgentConfirmationCategories.NativeCommand,
        NativeEdit or NativeWrite => AgentConfirmationCategories.NativeFileWrite,
        NativeWebSearch or NativeWebFetch => AgentConfirmationCategories.NativeNetwork,
        _ => AgentConfirmationCategories.None,
    };
}
