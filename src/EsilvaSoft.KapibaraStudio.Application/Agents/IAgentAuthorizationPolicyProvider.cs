using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Loads the current effective policy snapshot for an authenticated principal. Missing/unreadable is a deny.</summary>
public interface IAgentAuthorizationPolicyProvider
{
    Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken);
}
