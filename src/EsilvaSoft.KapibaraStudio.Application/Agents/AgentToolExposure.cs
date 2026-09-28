namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Release stages of the catalog, in the order of the phase 7 plan (lotes 2 and 10). A later stage always includes the
/// earlier ones. Each stage may be enabled only after its security gate has evidence; composing the registry in DI
/// does not release anything by itself.
/// </summary>
public enum AgentToolExposureStage
{
    /// <summary>No tool is discoverable or executable. Default for every registry instance.</summary>
    None = 0,

    /// <summary>
    /// <c>list_connections</c>, <c>list_databases</c>, <c>list_collections</c>, <c>get_indexes</c> and the per-session
    /// tools <c>get_cached_schema</c>, <c>get_workspace_context</c>, <c>propose_file_edit</c> and <c>approve</c> (ADR-056).
    /// </summary>
    Metadata = 1,

    /// <summary>Adds the literal query codec tools <c>mongo_find</c> and <c>mongo_count</c>.</summary>
    LiteralQueries = 2,

    /// <summary>Adds schema sampling, sample, distinct, explain, find-one and get-document.</summary>
    DerivedReads = 3,

    /// <summary>
    /// Lote 10: makes unitary writes and index tools eligible. Reaching this stage releases no write by itself: each
    /// write tool must also be named in <see cref="AgentToolExposure.WriteTools"/>, and writes exist only for the
    /// internal chat with human approval (never for the MCP ingress).
    /// </summary>
    UnitaryWrites = 4
}

/// <summary>Write tools released one by one inside <see cref="AgentToolExposureStage.UnitaryWrites"/>.</summary>
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

    /// <summary>Write tools individually released; effective only at <see cref="AgentToolExposureStage.UnitaryWrites"/>.</summary>
    public AgentWriteToolRelease WriteTools { get; }

    public static AgentToolExposure Through(AgentToolExposureStage stage) =>
        Enum.IsDefined(stage) ? stage == AgentToolExposureStage.None ? None : new(stage, AgentWriteToolRelease.None)
            : throw new ArgumentOutOfRangeException(nameof(stage));

    /// <summary>
    /// Releases exactly the named write tools. Requires <see cref="AgentToolExposureStage.UnitaryWrites"/>, so a
    /// read-stage composition can never expose a write by adding a flag.
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
        // The per-session tools also belong here: they release no document and never write MongoDB or disk.
        AgentToolRegistry.ListConnectionsToolName or AgentToolRegistry.ListDatabasesToolName or
            AgentToolRegistry.ListCollectionsToolName or AgentToolRegistry.GetIndexesToolName or
            AgentToolRegistry.GetCachedSchemaToolName or AgentToolRegistry.GetWorkspaceContextToolName or
            AgentToolRegistry.ProposeFileEditToolName or AgentToolRegistry.ApproveToolName => AgentToolExposureStage.Metadata,
        AgentToolRegistry.MongoFindToolName or AgentToolRegistry.MongoCountToolName =>
            AgentToolExposureStage.LiteralQueries,
        AgentToolRegistry.GetCollectionSchemaToolName or AgentToolRegistry.SampleDocumentsToolName or
            AgentToolRegistry.MongoDistinctToolName or AgentToolRegistry.MongoExplainToolName or
            AgentToolRegistry.MongoFindOneToolName or
            AgentToolRegistry.GetDocumentToolName => AgentToolExposureStage.DerivedReads,
        _ when WriteReleaseOf(toolName) is not AgentWriteToolRelease.None => AgentToolExposureStage.UnitaryWrites,
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
