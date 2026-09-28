using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Trusted binding of an agent session to the authenticated principal and the typed output destination used by the
/// shared tool registry. Implementations derive both from authenticated composition state, never from provider output,
/// tool arguments or model text. Returning null denies the call.
/// </summary>
public interface IAgentToolBindingProvider
{
    Task<AgentToolBinding?> ResolveAsync(
        AgentSessionId sessionId,
        AgentTurnId turnId,
        string providerId,
        string toolName,
        CancellationToken cancellationToken);
}

/// <summary>Registry inputs owned by the trusted side of the runtime.</summary>
public sealed record AgentToolBinding(
    AgentPrincipal Principal,
    AgentOutputDestination Destination,
    AgentOutputDataScope OutputDataScope);
