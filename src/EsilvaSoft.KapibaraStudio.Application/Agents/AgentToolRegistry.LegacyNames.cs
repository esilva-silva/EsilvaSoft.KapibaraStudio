namespace EsilvaSoft.KapibaraStudio.Application.Agents;

public sealed partial class AgentToolRegistry
{
    // Historical identifiers only. No descriptor, schema, handler or release flag can execute them.
    public const string InsertOneToolName = "insert_one";
    public const string UpdateOneToolName = "update_one";
    public const string DeleteOneToolName = "delete_one";
    public const string CreateIndexToolName = "create_index";
    public const string DropIndexToolName = "drop_index";
}
