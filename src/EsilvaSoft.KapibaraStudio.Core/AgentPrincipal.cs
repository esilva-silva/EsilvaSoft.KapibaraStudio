namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Authenticated authorization subject. It has no public constructor and must be issued from trusted runtime context,
/// never from model/tool arguments. Provider, client, session and turn identifiers belong to invocation context,
/// not to the MongoDB grant subject.
/// </summary>
public sealed class AgentPrincipal
{
    internal AgentPrincipal(Guid id, AgentPrincipalOrigin origin, long policyRevision, bool isSessionChannel = false)
    {
        if (isSessionChannel && origin != AgentPrincipalOrigin.External)
            throw new ArgumentException("Canal de sessão é sempre externo.", nameof(isSessionChannel));
        if (id == Guid.Empty) throw new ArgumentException("O principal precisa ter um identificador.", nameof(id));
        if (!Enum.IsDefined(origin)) throw new ArgumentOutOfRangeException(nameof(origin));
        ArgumentOutOfRangeException.ThrowIfLessThan(policyRevision, 1);
        Id = id;
        Origin = origin;
        PolicyRevision = policyRevision;
        IsSessionChannel = isSessionChannel;
    }

    /// <summary>Opaque grant subject identifier, unrelated to provider, client, session or turn identifiers.</summary>
    public Guid Id { get; }
    public AgentPrincipalOrigin Origin { get; }
    public long PolicyRevision { get; }

    /// <summary>
    /// Durable fact of the authenticated channel: a per-session channel of an integrated provider (ADR-056), never an
    /// external MCP client. Such a principal is authorized only while its session scope is active in this process.
    /// </summary>
    public bool IsSessionChannel { get; }
}
