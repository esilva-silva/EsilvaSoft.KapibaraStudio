using System.Collections.Concurrent;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Captured, per-turn context for native provider tool calls. Never keyed by the stable provider principal.</summary>
public sealed record AgentNativeChatTurnScope(
    Guid SessionId, Guid TurnId, string ProviderId, AgentTurnPlan Plan,
    AgentProviderPermissions Permissions, AgentWorkspaceContext? WorkspaceContext)
{
    public Guid ConversationId { get; init; }
}

public interface IAgentNativeChatTurnScopes
{
    bool Register(AgentNativeChatTurnScope scope);
    AgentNativeChatTurnScope? Find(Guid sessionId, Guid turnId);
    IReadOnlyList<AgentNativeChatTurnScope> Snapshot();
    bool Remove(Guid sessionId, Guid turnId);
}

/// <summary>In-memory active-turn scopes; exact key matching prevents cross-chat context reuse.</summary>
public sealed class AgentNativeChatTurnScopeRegistry : IAgentNativeChatTurnScopes
{
    private readonly ConcurrentDictionary<(Guid SessionId, Guid TurnId), AgentNativeChatTurnScope> _scopes = new();

    public bool Register(AgentNativeChatTurnScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.SessionId == Guid.Empty || scope.TurnId == Guid.Empty || string.IsNullOrWhiteSpace(scope.ProviderId))
            throw new ArgumentException("Escopo de turno interno inválido.", nameof(scope));
        return _scopes.TryAdd((scope.SessionId, scope.TurnId), scope);
    }

    public AgentNativeChatTurnScope? Find(Guid sessionId, Guid turnId) =>
        _scopes.TryGetValue((sessionId, turnId), out var scope) ? scope : null;

    public IReadOnlyList<AgentNativeChatTurnScope> Snapshot() => [.. _scopes.Values];

    public bool Remove(Guid sessionId, Guid turnId) => _scopes.TryRemove((sessionId, turnId), out _);
}
