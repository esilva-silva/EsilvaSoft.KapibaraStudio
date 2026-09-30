namespace EsilvaSoft.KapibaraStudio.Application.Agents.Broker;

/// <summary>Local OS transport, endpoint identity and private socket directory used by the broker.</summary>
public interface IAgentBrokerLocalTransport
{
    AgentBrokerEndpoint GetEndpoint(Guid workspaceId);
    void PrepareServerEndpoint(AgentBrokerEndpoint endpoint);
    bool HasPrivateDirectory(AgentBrokerEndpoint endpoint);
    IAgentBrokerServerInstance CreateServerInstance(AgentBrokerEndpoint endpoint, int maximumConnections, bool firstInstance);
    Task<Stream> ConnectAsync(AgentBrokerEndpoint endpoint, TimeSpan timeout, CancellationToken cancellationToken);
    void RemoveServerEndpoint(AgentBrokerEndpoint endpoint);
}

/// <summary>A listening transport instance whose connected stream is owned by its broker connection.</summary>
public interface IAgentBrokerServerInstance : IAsyncDisposable
{
    Stream Stream { get; }
    Task WaitForConnectionAsync(CancellationToken cancellationToken);
}
