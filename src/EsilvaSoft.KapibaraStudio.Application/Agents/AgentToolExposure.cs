namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Metadata and captured-output catalog. Historical stage values stay readable but never restore removed tools.
/// Composing the registry in DI does not grant permissions by itself.
/// </summary>
public enum AgentToolExposureStage
{
    /// <summary>No tool is discoverable or executable. Default for every registry instance.</summary>
    None = 0,

    /// <summary>
    /// Metadata tools, ordinary/Atlas indexes, cached schema, workspace context and captured execution outputs;
    /// mediated proposals, opt-in create-only workspace files and permission prompts. No document query or MongoDB mutation.
    /// </summary>
    Metadata = 1,

    /// <summary>Legacy value retained for compatibility; releases no additional tools.</summary>
    LiteralQueries = 2,

    /// <summary>Legacy value retained for compatibility; releases no additional tools.</summary>
    DerivedReads = 3,

    /// <summary>
    /// Legacy value retained for compatibility; even explicit write flags release no MongoDB tool.
    /// </summary>
    UnitaryWrites = 4
}

/// <summary>Historical flags retained for compatibility. They cannot release a removed tool.</summary>
[Flags]
public enum AgentWriteToolRelease
{
    None = 0,
    InsertOne = 1,
    UpdateOne = 2,
    DeleteOne = 4,
    CreateIndex = 8,
    DropIndex = 16
}

/// <summary>
/// Immutable, closed exposure gate for one registry. Unreleased tools are indistinguishable from unknown tools:
/// they have no descriptor or schema and are rejected before audit, policy, profile or MongoDB access.
/// </summary>
public sealed class AgentToolExposure
{
    private const AgentWriteToolRelease AllWriteTools = AgentWriteToolRelease.InsertOne |
        AgentWriteToolRelease.UpdateOne | AgentWriteToolRelease.DeleteOne | AgentWriteToolRelease.CreateIndex |
        AgentWriteToolRelease.DropIndex;

    private AgentToolExposure(AgentToolExposureStage stage, AgentWriteToolRelease writeTools)
    {
        Stage = stage;
        WriteTools = writeTools;
    }

    public static AgentToolExposure None { get; } = new(AgentToolExposureStage.None, AgentWriteToolRelease.None);

    public AgentToolExposureStage Stage { get; }

    /// <summary>Historical flags retained for configuration compatibility; no MongoDB write tool is exposed.</summary>
    public AgentWriteToolRelease WriteTools { get; }

    public static AgentToolExposure Through(AgentToolExposureStage stage) =>
        Enum.IsDefined(stage) ? stage == AgentToolExposureStage.None ? None : new(stage, AgentWriteToolRelease.None)
            : throw new ArgumentOutOfRangeException(nameof(stage));

    /// <summary>
    /// Preserves historical flags and their validation; removed tool names remain unavailable at every stage.
    /// </summary>
    public AgentToolExposure WithWriteTools(AgentWriteToolRelease tools)
    {
        if ((tools & ~AllWriteTools) != 0) throw new ArgumentOutOfRangeException(nameof(tools));
        if (tools != AgentWriteToolRelease.None && Stage < AgentToolExposureStage.UnitaryWrites)
            throw new InvalidOperationException("Escritas exigem o estágio UnitaryWrites.");
        return new(Stage, tools);
    }

    /// <summary>The stage that introduces a catalog tool, or <see langword="null"/> for unknown names.</summary>
    public static AgentToolExposureStage? StageOf(string? toolName) => toolName switch
    {
        // ADR-056: get_indexes is structured metadata (names, keys, flags, TTL, partial-filter field paths), no document.
        // Session tools release captured output or create a new workspace file with opt-in; none queries documents or writes MongoDB.
        AgentToolRegistry.ListConnectionsToolName or AgentToolRegistry.ListDatabasesToolName or
            AgentToolRegistry.ListCollectionsToolName or AgentToolRegistry.GetIndexesToolName or
            AgentToolRegistry.GetSearchIndexesToolName or AgentToolRegistry.GetQueryResultsToolName or
            AgentToolRegistry.GetQueryDiagnosticsToolName or
            AgentToolRegistry.GetCachedSchemaToolName or AgentToolRegistry.GetWorkspaceContextToolName or
            AgentToolRegistry.CreateWorkspaceFileToolName or AgentToolRegistry.ProposeFileEditToolName or AgentToolRegistry.ApproveToolName => AgentToolExposureStage.Metadata,
        _ => null
    };

    /// <summary>The individual release flag of a write tool, or <see cref="AgentWriteToolRelease.None"/>.</summary>
    public static AgentWriteToolRelease WriteReleaseOf(string? toolName) => toolName switch
    {
        AgentToolRegistry.InsertOneToolName => AgentWriteToolRelease.InsertOne,
        AgentToolRegistry.UpdateOneToolName => AgentWriteToolRelease.UpdateOne,
        AgentToolRegistry.DeleteOneToolName => AgentWriteToolRelease.DeleteOne,
        AgentToolRegistry.CreateIndexToolName => AgentWriteToolRelease.CreateIndex,
        AgentToolRegistry.DropIndexToolName => AgentWriteToolRelease.DropIndex,
        _ => AgentWriteToolRelease.None
    };

    public bool Exposes(string? toolName) =>
        Stage != AgentToolExposureStage.None && StageOf(toolName) is { } stage && stage <= Stage &&
        (stage != AgentToolExposureStage.UnitaryWrites || (WriteTools & WriteReleaseOf(toolName)) != 0);
}
