using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

public interface IAgentMongoExplainSource
{
    Task<AgentMongoExplainResult> ExplainAsync(ConnectionProfile profile, AgentMongoFindQuery query,
        CancellationToken cancellationToken);
}

public sealed record AgentMongoExplainResult(string PlanEjson, bool TargetVerified, bool ResultTooLarge);
