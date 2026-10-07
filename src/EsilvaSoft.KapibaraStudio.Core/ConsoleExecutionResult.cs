namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record ConsoleExecutionResult(IReadOnlyList<ConsoleResultSet> Results, string Messages, string? Error,
    TimeSpan Duration, bool IsCanceled, IReadOnlyList<Guid> ConnectionsUsed, string Environment)
{
    public bool IsTimedOut { get; init; }
    public string? ErrorCode { get; init; }
    /// <summary>Generations of connections actually used, captured by the execution; no URIs or credentials.</summary>
    public IReadOnlyDictionary<Guid, Guid?> ConnectionGenerations { get; init; } = new Dictionary<Guid, Guid?>();
}
