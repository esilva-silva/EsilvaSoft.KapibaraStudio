using System.Collections.ObjectModel;

namespace EsilvaSoft.KapibaraStudio.Core.Agents;

/// <summary>Transient, immutable output of an execution initiated by the user. Never executes or persists a query.</summary>
public sealed class AgentQueryExecutionSnapshot
{
    public AgentQueryExecutionSnapshot(Guid executionId, string tabId, DateTimeOffset startedAtUtc,
        string status, string? errorCode, string messages, string errors, TimeSpan duration,
        IEnumerable<AgentQueryOrigin> origins, IEnumerable<AgentQueryResultSnapshot> results)
    {
        ExecutionId = executionId;
        TabId = tabId;
        StartedAtUtc = startedAtUtc;
        Status = status;
        ErrorCode = errorCode;
        Messages = messages;
        Errors = errors;
        Duration = duration;
        Origins = Array.AsReadOnly(origins.Distinct().ToArray());
        Results = Array.AsReadOnly(results.ToArray());
    }

    public Guid ExecutionId { get; }
    public string TabId { get; }
    public DateTimeOffset StartedAtUtc { get; }
    public string Status { get; }
    public string? ErrorCode { get; }
    public string Messages { get; }
    public string Errors { get; }
    public TimeSpan Duration { get; }
    public ReadOnlyCollection<AgentQueryOrigin> Origins { get; }
    public ReadOnlyCollection<AgentQueryResultSnapshot> Results { get; }
    public override string ToString() => $"{nameof(AgentQueryExecutionSnapshot)} {{ ExecutionId = {ExecutionId} }}";
}

public sealed record AgentQueryOrigin(Guid ConnectionId, Guid? SourceGenerationId, string? Database, string? Collection);

public sealed class AgentQueryResultSnapshot
{
    public AgentQueryResultSnapshot(AgentQueryOrigin origin, IEnumerable<string> valuesEjson, bool isTruncated)
    {
        Origin = origin;
        ValuesEjson = Array.AsReadOnly(valuesEjson.ToArray());
        IsTruncated = isTruncated;
    }

    public AgentQueryOrigin Origin { get; }
    public ReadOnlyCollection<string> ValuesEjson { get; }
    public bool IsTruncated { get; }
    public override string ToString() => nameof(AgentQueryResultSnapshot);
}
