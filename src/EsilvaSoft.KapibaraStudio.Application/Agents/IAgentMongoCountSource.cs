using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Literal, bounded document count used only after agent authorization.</summary>
public interface IAgentMongoCountSource
{
    Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query,
        CancellationToken cancellationToken);
}

public sealed record AgentMongoCountQuery(string Database, string Collection, string FilterEjson, int MaxTimeMs);

public sealed record AgentMongoCountResult(string CountEjson, bool TargetVerified);
