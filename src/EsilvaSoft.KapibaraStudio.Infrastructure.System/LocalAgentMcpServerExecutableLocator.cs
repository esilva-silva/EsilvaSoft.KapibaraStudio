using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Resolves the packaged MCP proxy beside the application executable.</summary>
public sealed class LocalAgentMcpServerExecutableLocator(IHostPlatformSnapshot hostPlatform)
    : IAgentMcpServerExecutableLocator
{
    public string GetExecutablePath()
    {
        ArgumentNullException.ThrowIfNull(hostPlatform);
        return Path.Combine(AppContext.BaseDirectory, "mcp", "EsilvaSoft.KapibaraStudio.McpServer" +
            (hostPlatform.IsWindows ? ".exe" : string.Empty));
    }
}
