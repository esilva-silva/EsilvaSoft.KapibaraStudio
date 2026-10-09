namespace EsilvaSoft.KapibaraStudio.Core.Agents;

/// <summary>Kind of a persisted conversation entry. Persisted by name (append-only).</summary>
public enum AgentConversationEntryKind
{
    UserMessage = 0,
    AssistantMessage = 1,

    /// <summary>Summarized tool card: name and outcome only, never arguments or results.</summary>
    ToolCall = 2,

    /// <summary>Visible product notice (e.g. resume fallback, cancellation), in pt-BR.</summary>
    Notice = 3,

    /// <summary>Reference to an edit proposal (<see cref="AgentConversationEntry.ProposalId"/>); texts are not kept.</summary>
    EditProposal = 4,

    /// <summary>Opt-in, expiring local model reasoning; never tool output, audit data, or a provider conversation.</summary>
    ReasoningText = 5,
}

/// <summary>
/// One persisted entry. Message text is stored after secret redaction by the writer. Attachments are descriptors
/// only (no content); tool entries keep only <see cref="ToolName"/> and <see cref="ToolOutcome"/>.
/// </summary>
public sealed record AgentConversationEntry(
    AgentConversationEntryKind Kind,
    string Text,
    DateTimeOffset Timestamp)
{
    public IReadOnlyList<AgentAttachmentDescriptor> Attachments { get; init; } = [];

    public string? ToolName { get; init; }

    public AgentToolResultStatus? ToolOutcome { get; init; }

    public Guid? ProposalId { get; init; }

    /// <summary>Expiry for opt-in reasoning only. Other entry kinds must leave this unset.</summary>
    public DateTimeOffset? ExpiresAtUtc { get; init; }

    /// <summary>Omits the text and attachments (user content) so logs never carry them.</summary>
    public override string ToString() =>
        $"{nameof(AgentConversationEntry)} {{ Kind = {Kind}, Timestamp = {Timestamp:O}, TextLength = {Text?.Length ?? 0}, ToolName = {ToolName} }}";
}

/// <summary>
/// A workspace-global conversation (not bound to a tab). Persisted with optimistic concurrency: a save succeeds only
/// when the stored <see cref="Revision"/> equals the expected one, and the stored copy gets the next revision.
/// <see cref="ProviderSessionId"/> is the provider's own session (e.g. Claude Code <c>--resume</c>); it is an opaque
/// identifier, never a credential. <see cref="FormatVersion"/> lets readers refuse a newer format instead of
/// overwriting it.
/// </summary>
public sealed record AgentConversation(
    Guid Id,
    string ProviderId,
    string Title,
    string? ModelId,
    AgentOperationMode Mode,
    string? ProviderSessionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Revision,
    IReadOnlyList<AgentConversationEntry> Entries)
{
    public const int CurrentFormatVersion = 2;

    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>Omits the title and entries (user content).</summary>
    public override string ToString() =>
        $"{nameof(AgentConversation)} {{ Id = {Id}, ProviderId = {ProviderId}, Revision = {Revision}, Entries = {Entries?.Count ?? 0} }}";
}

/// <summary>State of a stored conversation as seen by a listing.</summary>
public enum AgentConversationSummaryState
{
    Readable = 0,

    /// <summary>The document exists but cannot be read; it is shown as such and never overwritten.</summary>
    Unreadable = 1,

    /// <summary>Written by a newer format; read-only for this version.</summary>
    UnsupportedVersion = 2,
}

/// <summary>History list row. Fields other than <see cref="Id"/> may be null for unreadable documents.</summary>
public sealed record AgentConversationSummary(
    Guid Id,
    string? ProviderId,
    string? Title,
    DateTimeOffset? UpdatedAt,
    long Revision,
    AgentConversationSummaryState State = AgentConversationSummaryState.Readable);
