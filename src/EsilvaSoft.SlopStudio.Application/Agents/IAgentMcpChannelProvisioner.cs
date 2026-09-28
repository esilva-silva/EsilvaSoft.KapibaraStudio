using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Why a per-session MCP channel could not be provided. Codes are safe to show and never carry paths.</summary>
public enum AgentMcpChannelStatus
{
    Ready = 0,

    /// <summary>The platform has no transport proof store for the proxy (today: Linux). A platform fact, not a failure.</summary>
    UnavailableOnPlatform = 1,

    /// <summary>The registry composition releases no product tool (stage <c>None</c>).</summary>
    ToolsNotReleased = 2,

    /// <summary>The MCP proxy executable is not next to the application.</summary>
    ServerExecutableMissing = 3,

    /// <summary>The local broker could not be started (endpoint busy, IO failure).</summary>
    BrokerUnavailable = 4,

    /// <summary>Channel enrollment or its proof in the OS store failed.</summary>
    EnrollmentFailed = 5,

    /// <summary>The channel policy could not be written; the channel was revoked.</summary>
    PolicyUnavailable = 6,

    /// <summary>Unknown or already closed session handle.</summary>
    UnknownSession = 7,

    /// <summary>A tool required by this turn is not published by the registry/broker; no process should start.</summary>
    RequiredToolUnavailable = 8,
}

/// <summary>
/// Process launch of the STDIO MCP proxy for the adapter's <c>--mcp-config</c>. Every argument is a non-secret
/// identifier: the channel proof stays in the OS store and is read by the proxy itself.
/// </summary>
public sealed record McpServerLaunchSpec(string ServerName, string Command, IReadOnlyList<string> Args)
{
    /// <summary>MCP server name; the CLI prefixes tools as <c>mcp__slopstudio__&lt;tool&gt;</c>.</summary>
    public const string DefaultServerName = "slopstudio";

    /// <summary>MCP name of a registry tool as the Claude Code CLI reports it.</summary>
    public static string ToolName(string registryToolName) =>
        "mcp__" + DefaultServerName + "__" + (registryToolName ?? throw new ArgumentNullException(nameof(registryToolName)));
}

/// <summary>Opaque reference to one open session channel. Knowing it grants nothing; the provisioner validates it.</summary>
public sealed record AgentMcpChannelHandle(Guid ChannelId, string ProviderId, Guid ConversationId);

public sealed record AgentMcpChannelProvisioning(
    AgentMcpChannelStatus Status,
    AgentMcpChannelHandle? Handle = null,
    McpServerLaunchSpec? LaunchSpec = null)
{
    public bool IsReady => Status == AgentMcpChannelStatus.Ready && Handle is not null && LaunchSpec is not null;
}

/// <summary>
/// Provides the per-session local MCP channel of an integrated provider (today <c>claude-code</c>): starts the broker on
/// demand, enrolls a channel/principal for the session, keeps its grants equal to the current turn plan and revokes it
/// when the session closes. It is independent of the user's opt-in for external MCP clients: a session channel is an
/// internal channel of the IDE and is accepted by the broker even when external clients are disabled.
/// </summary>
public interface IAgentMcpChannelProvisioner
{
    /// <summary>Platform fact for <see cref="AgentPlatformFacts.ProductToolsAvailable"/>.</summary>
    bool ProductToolsAvailable { get; }

    /// <summary>Opens the channel of one provider session. Nothing is exposed until a turn plan is bound.</summary>
    Task<AgentMcpChannelProvisioning> OpenSessionAsync(string providerId, Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Binds the plan of the turn about to start (call before launching the CLI for the turn). Tools and connections
    /// outside the plan are neither listed nor executable; grants follow <see cref="AgentTurnPlan.AllowedConnectionIds"/>.
    /// </summary>
    Task<AgentMcpChannelStatus> UpdateTurnAsync(AgentMcpChannelHandle handle, AgentTurnPlan plan,
        AgentProviderPermissions permissions, CancellationToken cancellationToken = default);

    /// <summary>Binds the immutable workspace snapshot captured by the originating chat turn.</summary>
    Task<AgentMcpChannelStatus> UpdateTurnAsync(AgentMcpChannelHandle handle, AgentTurnPlan plan,
        AgentProviderPermissions permissions, AgentWorkspaceContext? workspaceContext,
        CancellationToken cancellationToken = default) =>
        UpdateTurnAsync(handle, plan, permissions, cancellationToken);

    /// <summary>Revokes the channel (durably) and forgets the scope. Idempotent.</summary>
    Task CloseSessionAsync(AgentMcpChannelHandle handle, CancellationToken cancellationToken = default);
}
