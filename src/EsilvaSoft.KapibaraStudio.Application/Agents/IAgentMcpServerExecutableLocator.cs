namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Resolves the packaged MCP server executable path at the host adapter boundary.</summary>
public interface IAgentMcpServerExecutableLocator
{
    string GetExecutablePath();
}
