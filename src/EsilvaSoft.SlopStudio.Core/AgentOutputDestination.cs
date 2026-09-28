namespace EsilvaSoft.SlopStudio.Core;

/// <summary>
/// Typed recipient of tool output. The identifier routes an output grant (provider ID or MCP route) and is not principal
/// identity. Only trusted runtime/broker composition creates instances (internal factories).
/// </summary>
public sealed record AgentOutputDestination
{
    private AgentOutputDestination(AgentOutputDestinationKind kind, string? providerId)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == AgentOutputDestinationKind.Local && providerId is not null)
            throw new ArgumentException("O destino local não recebe identificador de provider.", nameof(providerId));
        if (kind != AgentOutputDestinationKind.Local &&
            (string.IsNullOrWhiteSpace(providerId) || providerId.Length > 64 || providerId.Any(char.IsControl)))
            throw new ArgumentException("O destino externo exige um identificador de roteamento válido.", nameof(providerId));
        Kind = kind;
        ProviderId = providerId;
    }

    public AgentOutputDestinationKind Kind { get; }

    /// <summary>Routing identifier of an external destination (provider ID or MCP route); null for local.</summary>
    public string? ProviderId { get; }

    /// <summary>True for every destination that leaves the process (provider or MCP client).</summary>
    public bool IsExternal => Kind != AgentOutputDestinationKind.Local;

    internal static AgentOutputDestination Local() => new(AgentOutputDestinationKind.Local, null);
    internal static AgentOutputDestination ProviderExternal(string providerId) => new(AgentOutputDestinationKind.ProviderExternal, providerId);

    /// <summary>External MCP client reached through the authenticated broker; <paramref name="routeId"/> routes the grant.</summary>
    internal static AgentOutputDestination McpExternal(string routeId) => new(AgentOutputDestinationKind.McpExternal, routeId);
}
