using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Query port of the trusted approval authority (registry/broker that froze the proposal). It describes an approval
/// already announced by the runtime (<see cref="AgentEventKind.ApprovalRequested"/>) and bound to the same session,
/// turn and approval IDs. Implementations never render model text, arguments outside the frozen proposal, secrets or
/// query results. Returning null (unknown, decided, expired or revoked) keeps approval disabled in the UI.
/// No production implementation exists until approval-bound writes (lote 10); the UI then fails closed.
/// </summary>
public interface IAgentApprovalDetailsSource
{
    Task<AgentApprovalDetails?> DescribeAsync(
        AgentSessionId sessionId, AgentTurnId turnId, AgentApprovalId approvalId, CancellationToken cancellationToken);
}
