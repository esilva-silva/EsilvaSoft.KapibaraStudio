namespace EsilvaSoft.SlopStudio.Core.Agents;

public enum AgentApprovalOutcome
{
    Granted,
    Denied,
}

/// <summary>A decision whose authenticity must be checked by the trusted approval service.</summary>
public sealed record AgentApprovalDecision(
    AgentSessionId SessionId,
    AgentTurnId TurnId,
    AgentApprovalId ApprovalId,
    AgentApprovalOutcome Outcome);
