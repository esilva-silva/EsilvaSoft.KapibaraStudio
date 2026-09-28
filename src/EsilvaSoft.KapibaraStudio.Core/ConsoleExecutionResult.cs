namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record ConsoleExecutionResult(IReadOnlyList<ConsoleResultSet> Results, string Messages, string? Error,
    TimeSpan Duration, bool IsCanceled, IReadOnlyList<Guid> ConnectionsUsed, string Environment)
{
    public bool IsTimedOut { get; init; }
}
