using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Runtime-facing authority that recognizes approvals frozen by <see cref="AgentWriteApprovalCoordinator"/>. For
/// those approval IDs it validates only the identity of a human decision: the approval must be pending, unexpired and
/// belong to the same session and turn. Accepting a decision is not a grant by itself: the verdict reaches the
/// coordinator only through its UI port (the runtime bridge), and only the coordinator issues the single-use ticket.
/// Approval requests are never validated from coordinator IDs (see <see cref="ValidateApprovalRequestAsync"/>).
/// Premise: the coordinator given here is the one composed on the same <see cref="AgentRuntimeWriteApprovalBridge"/>
/// that the runtime is attached to. Tool requests/results and approvals unknown to the coordinator go to <c>inner</c> (the
/// production <see cref="FailClosedAgentInteractionAuthority"/>), so nothing else is widened.
/// </summary>
public sealed class AgentWriteApprovalInteractionAuthority : IAgentInteractionAuthority
{
    private readonly AgentWriteApprovalCoordinator _approvals;
    private readonly IAgentInteractionAuthority _inner;

    public AgentWriteApprovalInteractionAuthority(AgentWriteApprovalCoordinator approvals, IAgentInteractionAuthority inner)
    {
        _approvals = approvals ?? throw new ArgumentNullException(nameof(approvals));
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public Task<bool> ValidateToolRequestAsync(AgentSessionId sessionId, AgentTurnId turnId, AgentToolCallId toolCallId,
        CancellationToken cancellationToken) =>
        _inner.ValidateToolRequestAsync(sessionId, turnId, toolCallId, cancellationToken);

    public Task<bool> ValidateToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) =>
        _inner.ValidateToolResultAsync(result, cancellationToken);

    /// <summary>
    /// Only providers ask the runtime to validate an approval request; registry approvals are announced by the runtime
    /// itself through <see cref="AgentRuntimeWriteApprovalBridge"/>. A provider that echoes a pending coordinator ID
    /// therefore gains nothing: the request always goes to <c>inner</c> (fail-closed in production).
    /// </summary>
    public Task<bool> ValidateApprovalRequestAsync(AgentSessionId sessionId, AgentTurnId turnId,
        AgentApprovalId approvalId, CancellationToken cancellationToken) =>
        _inner.ValidateApprovalRequestAsync(sessionId, turnId, approvalId, cancellationToken);

    public Task<bool> ValidateApprovalDecisionAsync(AgentApprovalDecision decision, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return _approvals.IsPending(decision.SessionId, decision.TurnId, decision.ApprovalId)
            ? Task.FromResult(true)
            : _inner.ValidateApprovalDecisionAsync(decision, cancellationToken);
    }
}
