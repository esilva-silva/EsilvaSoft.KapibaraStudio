namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Versioned prompt/catalog descriptions. Unknown extension tools retain their name.</summary>
public static class AgentToolDescriptions
{
    public const string Version = "kapibara-tool-descriptions-v1";

    public static string Get(string name) => name switch
    {
        "list_connections" => "List authorized connection profiles without credentials.",
        "list_databases" => "List database names on an authorized connection.",
        "list_collections" => "List collection names in an authorized database without querying documents.",
        "get_indexes" => "Read ordinary index metadata for an authorized collection.",
        "get_search_indexes" => "Read Atlas Search index metadata for an authorized collection.",
        "get_cached_schema" => "Read field metadata already cached by the IDE; never sample documents.",
        "get_workspace_context" => "Read authorized context from the originating editor tab and workspace.",
        "get_query_results" => "Read authorized results already captured from a human query; never execute a query.",
        "get_query_diagnostics" => "Read authorized errors and logs already captured from human executions.",
        "propose_file_edit" => "Propose an edit to an authorized existing file for review through the IDE.",
        "create_workspace_file" => "Create a new workspace file only with write opt-in and explicit confirmation; never overwrite.",
        "approve" => "Request the confirmation required by the active turn plan; does not bypass permissions.",
        _ => name,
    };
}
