namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Canonical mapping that prevents a descriptor from understating an operation's risk.</summary>
public static class AgentPermissionRiskCompatibility
{
    public static bool IsCompatible(AgentPermission permission, AgentToolRisk risk) => permission switch
    {
        AgentPermission.ReadMetadata or AgentPermission.ReadSchema or AgentPermission.ReadDiagnostics or
            AgentPermission.ExecuteReadQueries or AgentPermission.ReadDocuments => risk == AgentToolRisk.ReadOnly,
        AgentPermission.InsertDocuments or AgentPermission.UpdateDocuments or AgentPermission.CreateIndexes => risk == AgentToolRisk.Write,
        AgentPermission.DeleteDocuments or AgentPermission.DropIndexes => risk == AgentToolRisk.Destructive,
        AgentPermission.AdministrativeOperations => risk == AgentToolRisk.Administrative,
        _ => false
    };
}
