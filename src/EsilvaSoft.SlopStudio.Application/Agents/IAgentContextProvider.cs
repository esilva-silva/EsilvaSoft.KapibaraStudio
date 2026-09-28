using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Produces the consent-filtered context of one turn. Callers pass an immutable request captured from the originating
/// tab before awaiting; implementations must not read the explorer selection or mutable tab state after that point,
/// and must never include credentials, connection strings or query results.
/// </summary>
public interface IAgentContextProvider
{
    Task<AgentContextSnapshot> CaptureAsync(AgentContextCaptureRequest request, CancellationToken cancellationToken);
}
