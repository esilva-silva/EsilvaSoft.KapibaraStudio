using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

public interface IAgentRuntime
{
    Task<AgentSessionId> StartSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken);

    IAsyncEnumerable<AgentEvent> RunTurnAsync(
        AgentSessionId sessionId,
        AgentTurnRequest request,
        CancellationToken cancellationToken);

    Task SubmitToolResultAsync(AgentToolResult result, CancellationToken cancellationToken);

    Task DecideApprovalAsync(AgentApprovalDecision decision, CancellationToken cancellationToken);

    Task CancelTurnAsync(AgentSessionId sessionId, AgentTurnId turnId, CancellationToken cancellationToken);

    Task CloseSessionAsync(AgentSessionId sessionId, CancellationToken cancellationToken);

    /// <summary>Deletes provider-owned persisted state when the provider supports it; other providers are a no-op.</summary>
    Task DeleteProviderSessionAsync(string providerId, string providerSessionId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
