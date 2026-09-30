using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Dependencies of the per-session product tools (<c>get_cached_schema</c>, <c>get_workspace_context</c>,
/// <c>propose_file_edit</c>, <c>approve</c>). They exist only for principals of a per-session channel
/// (<see cref="SessionScopes"/>); every missing port makes its tool unavailable (unknown), and a missing
/// <see cref="ConfirmationPrompt"/> makes every confirmation a denial.
/// </summary>
public sealed record AgentSessionToolPorts(IAgentMcpSessionScopes SessionScopes)
{
    /// <summary>Bounded file reads for proposals; absence refuses disk targets without native fallback.</summary>
    public IAgentBoundedFileReader? FileReader { get; init; }
    public IAgentWorkspacePathProbe? PathProbe { get; init; }
    /// <summary>Native chat scopes are keyed by the exact runtime session and turn, not by principal.</summary>
    public IAgentNativeChatTurnScopes? NativeChatTurnScopes { get; init; }
    /// <summary>Autocomplete schema cache; read with <see cref="MetadataAccess.Peek"/> only (never samples).</summary>
    public IMetadataCache? MetadataCache { get; init; }

    /// <summary>Learned schema store; read only.</summary>
    public ILearnedSchemaRepository? LearnedSchemas { get; init; }

    /// <summary>Desktop snapshot of the workspace folder and active tab.</summary>
    public IAgentWorkspaceContextSource? WorkspaceContext { get; init; }

    /// <summary>Desktop sink of edit proposals; nothing is ever written to disk.</summary>
    public IAgentEditProposalSink? ProposalSink { get; init; }

    /// <summary>Desktop inline confirmation card.</summary>
    public IAgentToolConfirmationPrompt? ConfirmationPrompt { get; init; }

    /// <summary>Deadline of one confirmation (the runtime's <c>ApprovalTimeout</c>); capped at 120 s.</summary>
    public TimeSpan ApprovalTimeout { get; init; } = AgentWriteApprovalCoordinator.DefaultApprovalTimeout;
}
