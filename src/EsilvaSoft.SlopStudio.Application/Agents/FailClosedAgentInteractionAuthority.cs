using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Production authority while no trusted human approval channel or broker-answered tool path exists (lote 10). Every
/// request, result and decision is rejected, so approvals and externally answered tool calls fail closed. Read tools
/// dispatched by the runtime itself do not pass through this authority: they stay gated by the shared registry.
/// </summary>
public sealed class FailClosedAgentInteractionAuthority : IAgentInteractionAuthority
{
    public Task<bool> ValidateToolRequestAsync(
        AgentSessionId sessionId, AgentTurnId turnId, AgentToolCallId toolCallId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<bool> ValidateToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<bool> ValidateApprovalRequestAsync(
        AgentSessionId sessionId, AgentTurnId turnId, AgentApprovalId approvalId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<bool> ValidateApprovalDecisionAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
        Task.FromResult(false);
}
