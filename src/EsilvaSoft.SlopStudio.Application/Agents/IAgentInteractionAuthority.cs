using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Trusted broker/approver boundary. Implementations establish authenticated identity and policy outside
/// model output. Returning true for an unverified request or result is a security defect.
/// </summary>
public interface IAgentInteractionAuthority
{
    Task<bool> ValidateToolRequestAsync(
        AgentSessionId sessionId, AgentTurnId turnId, AgentToolCallId toolCallId,
        CancellationToken cancellationToken);

    Task<bool> ValidateToolResultAsync(AgentToolResult result, CancellationToken cancellationToken);

    Task<bool> ValidateApprovalRequestAsync(
        AgentSessionId sessionId, AgentTurnId turnId, AgentApprovalId approvalId,
        CancellationToken cancellationToken);

    Task<bool> ValidateApprovalDecisionAsync(AgentApprovalDecision decision, CancellationToken cancellationToken);
}
