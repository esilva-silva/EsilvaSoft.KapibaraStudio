using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Concrete local adapters composed by the standalone MCP proxy.</summary>
public sealed record McpProxySystemAdapterSet(
    IAgentBrokerLocalTransport Transport,
    IClientTransportCredentialStore Credentials,
    IAgentMcpConsole Console);

public static class McpProxySystemAdapters
{
    public static McpProxySystemAdapterSet Create(IHostPlatformSnapshot? platform = null,
        IAgentBrokerLocalTransport? transport = null, IClientTransportCredentialStore? credentials = null,
        IAgentMcpConsole? console = null)
    {
        platform ??= new LocalHostPlatformSnapshot();
        return new McpProxySystemAdapterSet(
            transport ?? new BrokerLocalTransport(),
            credentials ?? (platform.IsWindows
                ? new WindowsClientTransportCredentialStore()
                : new UnavailableClientTransportCredentialStore()),
            console ?? new LocalAgentMcpConsole());
    }
}
