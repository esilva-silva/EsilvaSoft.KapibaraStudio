namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Typed outcome of an agent persistence operation. Failures are values the UI must show, never swallowed.</summary>
public enum AgentPersistenceStatus
{
    Succeeded = 0,
    NotFound = 1,

    /// <summary>Optimistic concurrency: the stored revision differs from the expected one. Nothing was written.</summary>
    Conflict = 2,

    /// <summary>The stored document cannot be read. It is kept as is and never overwritten by an empty/default value.</summary>
    Unreadable = 3,

    /// <summary>The stored document has a newer format version. It is kept as is (read-only for this version).</summary>
    UnsupportedVersion = 4,

    /// <summary>The store failed (I/O, locked file...). Nothing may be assumed written.</summary>
    Failed = 5,

    /// <summary>The value was refused before writing (limits, invalid fields).</summary>
    Invalid = 6,
}

/// <summary>Result without a value (delete). <see cref="ErrorCode"/> is a safe, stable code; never paths or data.</summary>
public sealed record AgentPersistenceOutcome(AgentPersistenceStatus Status, string? ErrorCode = null)
{
    public static AgentPersistenceOutcome Success { get; } = new(AgentPersistenceStatus.Succeeded);

    public bool Succeeded => Status == AgentPersistenceStatus.Succeeded;
}

/// <summary>Result with a value, present only when <see cref="Succeeded"/>. Create it through <see cref="AgentPersistenceResult"/>.</summary>
public sealed record AgentPersistenceResult<T>(AgentPersistenceStatus Status, T? Value = default, string? ErrorCode = null)
{
    public bool Succeeded => Status == AgentPersistenceStatus.Succeeded;
}

/// <summary>Factories for <see cref="AgentPersistenceResult{T}"/>.</summary>
public static class AgentPersistenceResult
{
    public static AgentPersistenceResult<T> Success<T>(T value) => new(AgentPersistenceStatus.Succeeded, value);

    public static AgentPersistenceResult<T> Failure<T>(AgentPersistenceStatus status, string? errorCode = null) =>
        status == AgentPersistenceStatus.Succeeded
            ? throw new ArgumentException("Falha exige um status diferente de sucesso.", nameof(status))
            : new(status, default, errorCode);
}
