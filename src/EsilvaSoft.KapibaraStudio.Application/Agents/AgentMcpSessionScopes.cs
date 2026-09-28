using System.Collections.Concurrent;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Trusted state of one per-session MCP channel (for example the <c>claude-code</c> provider of one conversation): the
/// enrolled channel and principal, and the plan of the turn in progress. It is created and updated only by trusted
/// composition (the channel provisioner, driven by the provider adapter before each turn), never from tool arguments,
/// MCP client names or model output. Without a plan nothing is exposed.
/// </summary>
public sealed record AgentMcpSessionScope(
    Guid ChannelId,
    Guid PrincipalId,
    string ProviderId,
    Guid ConversationId,
    AgentTurnPlan? Plan = null,
    AgentProviderPermissions? Permissions = null,
    bool Closed = false,
    AgentWorkspaceContext? WorkspaceContext = null,
    ConcurrentDictionary<string, byte>? SessionApprovals = null,
    object? ApprovalGate = null,
    long Generation = 0)
{
    /// <summary>
    /// Whether the current turn exposes <paramref name="toolName"/> (registry name, without the MCP prefix). The
    /// permission-prompt tool is exposed only when the plan requires confirmations.
    /// </summary>
    public bool Exposes(string? toolName) =>
        toolName is not null && !Closed && Plan is { IsBlocked: false } plan && Permissions is not null &&
        (toolName == AgentToolRegistry.ApproveToolName
            ? plan.RequiresPermissionPromptTool
            : plan.ProductTools.Contains(toolName, StringComparer.Ordinal));

    /// <summary><c>AllowedConnectionIds</c>: null means every connection; an empty list means none.</summary>
    public bool AllowsConnection(Guid connectionId) =>
        connectionId != Guid.Empty && !Closed && Plan is { IsBlocked: false } plan &&
        (plan.AllowedConnectionIds is null || plan.AllowedConnectionIds.Contains(connectionId));
}

/// <summary>Read side of the per-session channel scopes, consumed by the registry and the broker.</summary>
public interface IAgentMcpSessionScopes
{
    /// <summary>Scope of an authenticated principal, or null when the principal is not a per-session channel.</summary>
    AgentMcpSessionScope? FindByPrincipal(Guid principalId);

    /// <summary>Scope of an enrolled channel, or null when the channel is not a per-session channel.</summary>
    AgentMcpSessionScope? FindByChannel(Guid channelId);

    /// <summary>Channels that still have a scope (open or tombstoned) in this process; everything else is an orphan.</summary>
    IReadOnlyCollection<Guid> ChannelIds { get; }
}

/// <summary>
/// In-memory registry of the per-session channels of this IDE process. Not persisted: a scope lives only while its
/// conversation session is open. The channel row itself is durably marked as a session channel, so a principal of a
/// session channel without an active scope here is denied by the broker and the registry (fail-closed), and channels
/// left by a crash, kill or cancellation are revoked as orphans (<c>RevokeOrphanSessionChannelsAsync</c>) on the first
/// session and when the broker starts. Closing keeps a tombstone (<see cref="Close"/>: everything denied) until the
/// durable revocation succeeds.
/// </summary>
public sealed class AgentMcpSessionRegistry : IAgentMcpSessionScopes
{
    private readonly ConcurrentDictionary<Guid, AgentMcpSessionScope> _byPrincipal = new();
    private readonly ConcurrentDictionary<Guid, Guid> _principalByChannel = new();

    public int Count => _byPrincipal.Count;

    public AgentMcpSessionScope? FindByPrincipal(Guid principalId) =>
        _byPrincipal.TryGetValue(principalId, out var scope) ? scope : null;

    public AgentMcpSessionScope? FindByChannel(Guid channelId) =>
        _principalByChannel.TryGetValue(channelId, out var principalId) ? FindByPrincipal(principalId) : null;

    /// <summary>Registers a new channel without a plan (everything denied until the first turn plan).</summary>
    /// <exception cref="InvalidOperationException">The channel or principal is already registered.</exception>
    public AgentMcpSessionScope Register(Guid channelId, Guid principalId, string providerId, Guid conversationId)
    {
        if (channelId == Guid.Empty || principalId == Guid.Empty || conversationId == Guid.Empty)
            throw new ArgumentException("Identificadores do canal de sessão inválidos.");
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);
        var scope = new AgentMcpSessionScope(channelId, principalId, providerId, conversationId,
            SessionApprovals: new ConcurrentDictionary<string, byte>(StringComparer.Ordinal), ApprovalGate: new object());
        if (!_principalByChannel.TryAdd(channelId, principalId))
            throw new InvalidOperationException("Canal de sessão já registrado.");
        if (!_byPrincipal.TryAdd(principalId, scope))
        {
            _principalByChannel.TryRemove(channelId, out _);
            throw new InvalidOperationException("Principal de sessão já registrado.");
        }
        return scope;
    }

    /// <summary>Channel ids that still have a scope (open or tombstoned); never revoked as orphans.</summary>
    public IReadOnlyCollection<Guid> ChannelIds => [.. _principalByChannel.Keys];

    /// <summary>Replaces the plan of the turn in progress. Returns false for an unknown or closed principal.</summary>
    public bool UpdateTurn(Guid principalId, AgentTurnPlan plan, AgentProviderPermissions permissions,
        AgentWorkspaceContext? workspaceContext = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(permissions);
        while (_byPrincipal.TryGetValue(principalId, out var current) && !current.Closed)
        {
            lock (current.ApprovalGate ?? current)
            {
                if (!ReferenceEquals(FindByPrincipal(principalId), current)) continue;
                var revoke = current.Permissions is not null &&
                    (!Equals(current.Permissions, permissions) || !SameWorkspace(current.WorkspaceContext, workspaceContext));
                if (_byPrincipal.TryUpdate(principalId, current with
                    {
                        Plan = plan, Permissions = permissions, WorkspaceContext = workspaceContext,
                        Generation = current.Generation + 1,
                        SessionApprovals = revoke ? new ConcurrentDictionary<string, byte>(StringComparer.Ordinal) : current.SessionApprovals
                    }, current)) return true;
            }
        }
        return false;
    }

    /// <summary>Tombstone: the scope stays registered but exposes nothing, until the revocation is durable.</summary>
    public bool Close(Guid principalId)
    {
        while (_byPrincipal.TryGetValue(principalId, out var current))
        {
            lock (current.ApprovalGate ?? current)
                if (_byPrincipal.TryUpdate(principalId, current with
                    { Plan = null, Permissions = null, WorkspaceContext = null, Closed = true,
                      Generation = current.Generation + 1,
                      SessionApprovals = new ConcurrentDictionary<string, byte>(StringComparer.Ordinal) }, current)) return true;
        }
        return false;
    }

    /// <summary>Removes the channel; later calls of its principal see no scope and are denied.</summary>
    public bool Remove(Guid principalId)
    {
        while (_byPrincipal.TryGetValue(principalId, out var scope))
        {
            lock (scope.ApprovalGate ?? scope)
            {
                if (!ReferenceEquals(FindByPrincipal(principalId), scope)) continue;
                if (!_byPrincipal.TryRemove(new KeyValuePair<Guid, AgentMcpSessionScope>(principalId, scope))) continue;
                _principalByChannel.TryRemove(scope.ChannelId, out _);
                return true;
            }
        }
        return false;
    }

    private static bool SameWorkspace(AgentWorkspaceContext? left, AgentWorkspaceContext? right) =>
        left?.WorkspaceFolder == right?.WorkspaceFolder && left?.TabId == right?.TabId &&
        left?.ActiveFilePath == right?.ActiveFilePath && left?.ConnectionId == right?.ConnectionId;
}
