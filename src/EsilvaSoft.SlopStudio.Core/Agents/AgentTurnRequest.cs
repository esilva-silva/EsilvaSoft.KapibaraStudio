namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>
/// A fixed request; callers capture tab and document context before invoking the runtime.
/// The init-only members below are additive (P7-CLP-1): they default to "absent", so providers that ignore them keep
/// their previous behavior. A provider that honors <see cref="Plan"/> must never widen it. Only providers declaring
/// <see cref="AgentProviderCapabilities.TurnPlan"/> consume <see cref="Plan"/>, <see cref="SystemPrompt"/> and
/// <see cref="Attachments"/>; callers must not send a plan or chips to any other provider (refuse the send instead,
/// since those would be silently ignored).
/// </summary>
public sealed record AgentTurnRequest(
    AgentTurnId TurnId,
    string UserMessage,
    string TabId,
    long DocumentVersion,
    string? AuthorizedContext = null)
{
    /// <summary>Effective plan from <c>AgentModePolicy</c> (mode, tools, rules, proposal handling); null = legacy behavior.</summary>
    public AgentTurnPlan? Plan { get; init; }

    /// <summary>Operation mode of this turn: the single source is <see cref="AgentTurnPlan.Mode"/> (null without a plan).</summary>
    public AgentOperationMode? Mode => Plan?.Mode;

    /// <summary>Short per-turn system prompt (appended to the provider's own), rebuilt every turn so the mode applies on resume.</summary>
    public string? SystemPrompt { get; init; }

    /// <summary>Resolved, redacted attachments of this turn (runtime only; never persisted with content).</summary>
    public IReadOnlyList<AgentContextAttachment> Attachments { get; init; } = [];

    /// <summary>Conversation that originated the turn; results are routed only to it.</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>
    /// The same persisted permissions snapshot <see cref="Plan"/> was built from (P7-CLP-4, additive). A provider that
    /// exposes product tools through a per-session channel binds it with the plan (workspace, proposals and data scopes)
    /// and refuses the turn when the plan has product tools and this is null. Never widens the plan.
    /// </summary>
    public AgentProviderPermissions? Permissions { get; init; }

    /// <summary>Immutable workspace snapshot captured when this turn was sent; never recaptured from the active tab.</summary>
    public AgentWorkspaceContext? WorkspaceContext { get; init; }
}
