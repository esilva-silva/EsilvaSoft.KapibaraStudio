using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Human answer to one tool confirmation. There is deliberately no "always" value: every approval covers exactly one
/// call with exactly the input shown.
/// </summary>
public enum AgentToolConfirmationDecision
{
    Rejected = 0,
    ApprovedOnce = 1,
}

/// <summary>
/// One confirmation shown to the user. <see cref="ToolName"/> is the name the CLI asked about (native <c>Read</c> or
/// <c>mcp__slopstudio__list_connections</c>); <see cref="InputJson"/> is the exact input that will run if approved
/// (display only: it is data, never an instruction). <see cref="ConversationId"/> is the conversation that owns the
/// channel, so the card is shown only there.
/// </summary>
public sealed record AgentToolConfirmationRequest(
    Guid ConversationId,
    string ProviderId,
    string ToolName,
    AgentConfirmationCategories Category,
    string InputJson,
    string? ToolUseId)
{
    public override string ToString() =>
        $"{nameof(AgentToolConfirmationRequest)} {{ ConversationId = {ConversationId}, ToolName = {ToolName} }}";
}

/// <summary>
/// Implemented by the Desktop as the inline card "Aprovar uma vez / Rejeitar" of the originating conversation. The
/// registry applies the deadline (the runtime's <c>ApprovalTimeout</c>) and cancels the token when it expires; an
/// expired, cancelled or failed prompt is a rejection. Missing implementation means every confirmation is denied.
/// </summary>
public interface IAgentToolConfirmationPrompt
{
    Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request, CancellationToken cancellationToken);
}
