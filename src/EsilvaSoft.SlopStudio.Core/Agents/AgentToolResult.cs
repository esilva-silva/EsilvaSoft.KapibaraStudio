namespace EsilvaSoft.SlopStudio.Core.Agents;

public enum AgentToolResultStatus
{
    Succeeded,
    Failed,
    Denied,
    Cancelled,
    OutcomeUnknown,
}

/// <summary>A broker-produced result, bound to one pending call and turn.</summary>
public sealed record AgentToolResult(
    AgentSessionId SessionId,
    AgentTurnId TurnId,
    AgentToolCallId ToolCallId,
    AgentToolResultStatus Status,
    string? Data = null,
    string? ErrorCode = null,
    string? SafeMessage = null,
    bool Truncated = false);
