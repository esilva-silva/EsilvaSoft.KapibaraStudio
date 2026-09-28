using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>The central default-deny gate for one permission at one exact MongoDB namespace.</summary>
public interface IAgentPermissionEvaluator
{
    Task<AgentPermissionDecision> EvaluateAsync(AgentPermissionRequest request, CancellationToken cancellationToken);
}
