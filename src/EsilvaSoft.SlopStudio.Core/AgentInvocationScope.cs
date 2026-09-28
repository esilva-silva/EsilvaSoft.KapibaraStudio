namespace EsilvaSoft.SlopStudio.Core;

/// <summary>Explicit authorization lifetime for one session or one turn within that session.</summary>
public sealed record AgentInvocationScope
{
    private AgentInvocationScope(AgentInvocationScopeKind kind, Guid sessionId, Guid? turnId)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (sessionId == Guid.Empty) throw new ArgumentException("A sessão precisa ter um identificador.", nameof(sessionId));
        if (kind == AgentInvocationScopeKind.Session && turnId is not null)
            throw new ArgumentException("O escopo de sessão não pode conter um turno.", nameof(turnId));
        if (kind == AgentInvocationScopeKind.Turn && (turnId is null || turnId == Guid.Empty))
            throw new ArgumentException("O escopo de turno exige um identificador de turno.", nameof(turnId));
        Kind = kind;
        SessionId = sessionId;
        TurnId = turnId;
    }

    public AgentInvocationScopeKind Kind { get; }
    public Guid SessionId { get; }
    public Guid? TurnId { get; }

    public static AgentInvocationScope ForSession(Guid sessionId) => new(AgentInvocationScopeKind.Session, sessionId, null);

    public static AgentInvocationScope ForTurn(Guid sessionId, Guid turnId) => new(AgentInvocationScopeKind.Turn, sessionId, turnId);

    /// <summary>Checks the requested invocation identity against this session or exact-turn grant.</summary>
    public bool Covers(AgentInvocationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.SessionId != SessionId || context.TurnId is null || context.TurnId == Guid.Empty) return false;
        return Kind == AgentInvocationScopeKind.Session ||
            Kind == AgentInvocationScopeKind.Turn && context.TurnId == TurnId;
    }
}
