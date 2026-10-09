namespace EsilvaSoft.KapibaraStudio.Core;

public enum ProfilerCaptureState { Prepared, Active, Uncertain }

/// <summary>Sanitized recovery receipt. Never contains connection strings, profiler filters or captured operations.</summary>
public sealed record ProfilerCaptureTicket(
    int Version,
    Guid Id,
    Guid ProfileId,
    string Database,
    string NodeIdentity,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset EndsAtUtc,
    ProfilerSettings Previous,
    ProfilerSettings Applied,
    ProfilerCaptureState State)
{
    public const int CurrentVersion = 1;

    public ProfilerCaptureTicket Validate()
    {
        if (Version != CurrentVersion || Id == Guid.Empty || ProfileId == Guid.Empty
            || string.IsNullOrWhiteSpace(Database) || Database is "admin" or "config" or "local"
            || string.IsNullOrWhiteSpace(NodeIdentity) || NodeIdentity.Length > 256
            || EndsAtUtc <= StartedAtUtc || EndsAtUtc - StartedAtUtc > TimeSpan.FromHours(1)
            || Previous is null || Applied is null
            || Previous.FilterDigest is not null || Applied.FilterDigest is not null
            || Previous.Level is < 0 or > 1 || Applied.Level is < 1 or > 2
            || Previous.SlowMs is null || Previous.SampleRate is null
            || Applied.SlowMs is null || Applied.SampleRate is null
            || !Enum.IsDefined(State))
            throw new InvalidDataException("Registro de recuperação do profiler inválido ou incompatível.");
        return this;
    }
}

public sealed record ProfilerCaptureEntry(DateTimeOffset TimestampUtc, string Operation, string Namespace, int? Milliseconds, string? PlanSummary);

public sealed record ProfilerCapturePage(IReadOnlyList<ProfilerCaptureEntry> Entries, bool Truncated, DateTimeOffset ThroughUtc);
