using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Loads the current effective policy snapshot for an authenticated principal. Missing/unreadable is a deny.</summary>
public interface IAgentAuthorizationPolicyProvider
{
    Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken);
}
