using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Literal, bounded distinct-values read after explicit agent authorization.</summary>
public interface IAgentMongoDistinctSource
{
    Task<AgentMongoDistinctPage> DistinctAsync(ConnectionProfile profile, AgentMongoDistinctQuery query,
        CancellationToken cancellationToken);
}

public sealed record AgentMongoDistinctQuery(string Database, string Collection, string Field,
    string FilterEjson, int MaximumValues, int MaxTimeMs);

public sealed record AgentMongoDistinctPage(IReadOnlyList<string> ValuesEjson, bool Truncated,
    bool TargetVerified, bool ResultTooLarge, AgentMongoDistinctTruncationReason? TruncationReason = null);

public enum AgentMongoDistinctTruncationReason { ValueLimit, OutputLimit }
