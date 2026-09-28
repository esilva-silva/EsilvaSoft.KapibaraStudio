namespace EsilvaSoft.SlopStudio.Core.Agents;

/// <summary>How the edit proposals of a turn are handled. Nothing is ever written to disk by an agent.</summary>
public enum AgentProposalHandling
{
    /// <summary><c>propose_file_edit</c> is not exposed.</summary>
    Disabled = 0,

    /// <summary>The proposal is registered and waits for the user to review it hunk by hunk.</summary>
    ReviewRequired = 1,

    /// <summary>The proposal is applied to the editor buffer (undoable per hunk) and never saved to disk.</summary>
    AutoApplyToBuffer = 2,
}

/// <summary>
/// Categories of tool operations governed by the provider permission prompt.
/// </summary>
[Flags]
public enum AgentConfirmationCategories
{
    None = 0,

    /// <summary><c>list_connections</c>, <c>list_databases</c>, <c>list_collections</c>, <c>get_indexes</c>, <c>get_cached_schema</c>.</summary>
    MongoMetadataRead = 1,

    /// <summary><c>get_workspace_context</c>.</summary>
    WorkspaceContextRead = 2,

    /// <summary>Provider-native file reads (Claude Code Read/Glob/Grep).</summary>
    NativeFileRead = 4,

    /// <summary><c>propose_file_edit</c>.</summary>
    EditProposal = 8,

    /// <summary>Native shell or command execution.</summary>
    NativeCommand = 16,

    /// <summary>Native file editing/writing.</summary>
    NativeFileWrite = 32,

    /// <summary>Native network tools.</summary>
    NativeNetwork = 64,

    All = MongoMetadataRead | WorkspaceContextRead | NativeFileRead | EditProposal | NativeCommand | NativeFileWrite | NativeNetwork,
}

/// <summary>Why a turn cannot be sent at all.</summary>
public enum AgentTurnBlockReason
{
    None = 0,

    /// <summary>No persisted consent for sending data to the provider's external destination.</summary>
    ConsentMissing = 1,

    /// <summary>
    /// The permissions are malformed (a null section, an undefined enum value, a newer format version...). Nothing is
    /// sent until they are fixed: a malformed value is never read as "allowed".
    /// </summary>
    InvalidPermissions = 2,
}

/// <summary>
/// Effective, immutable decision for one turn, produced only by the Application <c>AgentModePolicy</c>. Adapters apply
/// it literally (e.g. Claude Code <c>--tools</c>, <c>ask</c>/<c>deny</c> rules, <c>--mcp-config</c>,
/// <c>--permission-prompt-tool</c>); they never widen it. The CLI permission mode stays <c>default</c> in every mode.
/// <see cref="ProductTools"/> are registry names without the MCP server prefix. MongoDB write tools never appear.
/// </summary>
public sealed record AgentTurnPlan(
    AgentOperationMode Mode,
    IReadOnlyList<string> NativeTools,
    IReadOnlyList<string> NativeAskRules,
    IReadOnlyList<string> NativeDenyRules,
    IReadOnlyList<string> ProductTools,
    AgentProposalHandling ProposalHandling,
    bool RequiresPermissionPromptTool,
    AgentConfirmationCategories ConfirmationCategories,
    AgentTurnBlockReason BlockReason = AgentTurnBlockReason.None)
{
    /// <summary>Connection IDs the product tools may touch; null means every registered connection.</summary>
    public IReadOnlyList<Guid>? AllowedConnectionIds { get; init; }

    /// <summary>
    /// Safe, stable codes explaining capabilities the plan had to drop (e.g. <c>ProductToolsUnavailableOnPlatform</c>),
    /// for a visible state in the UI. Never contains paths or user text.
    /// </summary>
    public IReadOnlyList<string> Notices { get; init; } = [];

    public bool IsBlocked => BlockReason != AgentTurnBlockReason.None;

    /// <summary>A plan that sends nothing and exposes nothing.</summary>
    public static AgentTurnPlan Blocked(AgentOperationMode mode, AgentTurnBlockReason reason) =>
        new(mode, [], [], [], [], AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None, reason);
}
