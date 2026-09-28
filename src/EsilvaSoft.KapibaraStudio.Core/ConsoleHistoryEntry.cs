namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record ConsoleHistoryEntry(int Version, Guid Id, DateTimeOffset ExecutedAt, Guid ProfileId,
    string Connection, string Database, string Environment, string Script, double DurationMs, string Status,
    IReadOnlyList<Guid> ConnectionsUsed)
{
    public string? TargetHost { get; init; }
    // Additive fields: old version-1 entries reopen as Console with their original defaults.
    public string Mode { get; init; } = "Console";
    public string Collection { get; init; } = "";
    public int DocumentLimit { get; init; } = 100;
    public string DisplayText => $"{ExecutedAt.LocalDateTime:yyyy-MM-dd HH:mm:ss} · {Mode} · {Connection} › {Database}{(Collection.Length == 0 ? "" : " › " + Collection)} · {Environment} · {Status}";
}
