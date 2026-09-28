using System.Text.Json.Serialization;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Bounded index metadata projection for the internal agent registry.</summary>
public interface IAgentMongoIndexSource
{
    Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database,
        string collection, TimeSpan maximumExecutionTime, CancellationToken cancellationToken);
}

public sealed record AgentMongoIndexSummary(string Name, IReadOnlyList<string> KeyFields,
    bool Unique, bool Sparse, bool Hidden)
{
    /// <summary>Kind of each key field, parallel to <see cref="KeyFields"/> ("1", "-1", "text", "2dsphere", "hashed"...).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? KeyDirections { get; init; }

    /// <summary><c>expireAfterSeconds</c> of a TTL index.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TtlSeconds { get; init; }

    /// <summary>Field paths referenced by the partial filter; the filter values never leave the source.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? PartialFilterFields { get; init; }
}

public sealed record AgentMongoIndexPage(IReadOnlyList<AgentMongoIndexSummary> Indexes,
    bool Truncated, bool TargetVerified);
