using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

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
            AgentToolRegistry.ListCollectionsToolName or AgentToolRegistry.GetIndexesToolName or AgentToolRegistry.GetSearchIndexesToolName =>
            AgentOutputDataScope.Metadata,
        AgentToolRegistry.GetCachedSchemaToolName =>
            AgentOutputDataScope.Schema,
        // Per-session tools without MongoDB data: workspace names, a proposal receipt, a confirmation answer.
        AgentToolRegistry.GetWorkspaceContextToolName or AgentToolRegistry.ProposeFileEditToolName or
            AgentToolRegistry.ApproveToolName => AgentOutputDataScope.Metadata,
        AgentToolRegistry.GetQueryResultsToolName or AgentToolRegistry.GetQueryDiagnosticsToolName =>
            AgentOutputDataScope.DocumentValues,
        _ => null
    };
}
