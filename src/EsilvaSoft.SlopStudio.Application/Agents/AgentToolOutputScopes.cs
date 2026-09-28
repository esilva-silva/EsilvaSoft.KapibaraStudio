using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Trusted classification of the data each catalog tool releases, shared by every ingress (native runtime and MCP
/// broker) so the same tool always asks the registry for the same output grant. It is chosen by trusted composition,
/// never by a client or model, and the registry still requires a grant for exactly this scope and destination.
/// Unknown names return <see langword="null"/>, which denies.
/// </summary>
public static class AgentToolOutputScopes
{
    public static AgentOutputDataScope? For(string? toolName) => toolName switch
    {
        AgentToolRegistry.ListConnectionsToolName or AgentToolRegistry.ListDatabasesToolName or
            AgentToolRegistry.ListCollectionsToolName or AgentToolRegistry.GetIndexesToolName =>
            AgentOutputDataScope.Metadata,
        AgentToolRegistry.GetCollectionSchemaToolName or AgentToolRegistry.GetCachedSchemaToolName =>
            AgentOutputDataScope.Schema,
        // Per-session tools without MongoDB data: workspace names, a proposal receipt, a confirmation answer.
        AgentToolRegistry.GetWorkspaceContextToolName or AgentToolRegistry.ProposeFileEditToolName or
            AgentToolRegistry.ApproveToolName => AgentOutputDataScope.Metadata,
        AgentToolRegistry.MongoExplainToolName or AgentToolRegistry.MongoFindToolName or AgentToolRegistry.MongoCountToolName or
            AgentToolRegistry.SampleDocumentsToolName or AgentToolRegistry.MongoFindOneToolName or
            AgentToolRegistry.GetDocumentToolName or AgentToolRegistry.MongoDistinctToolName =>
            AgentOutputDataScope.DocumentValues,
        // Lote 10: document writes return the document identifier (a value); index writes return only metadata.
        AgentToolRegistry.InsertOneToolName or AgentToolRegistry.UpdateOneToolName or
            AgentToolRegistry.DeleteOneToolName => AgentOutputDataScope.DocumentValues,
        AgentToolRegistry.CreateIndexToolName or AgentToolRegistry.DropIndexToolName => AgentOutputDataScope.Metadata,
        _ => null
    };
}
