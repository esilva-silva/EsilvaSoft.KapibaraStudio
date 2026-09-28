namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// Adapter output. Native provider identifiers and exceptions stay inside the adapter. Every field is untrusted
/// model/provider data: it never authorizes a tool, grants an approval or closes a turn by itself.
/// <c>MessageId</c> is an adapter-scoped key used only to deduplicate message events inside one turn;
/// <c>ToolName</c> and <c>ArgumentsJson</c> are validated by the shared registry, never interpreted by the runtime.
/// </summary>
public sealed record AgentProviderEvent(
    AgentEventKind Kind,
    string? Text = null,
    AgentToolCallId? ToolCallId = null,
    AgentApprovalId? ApprovalId = null,
    AgentMessageId? MessageId = null,
    string? ToolName = null,
    string? ArgumentsJson = null);
