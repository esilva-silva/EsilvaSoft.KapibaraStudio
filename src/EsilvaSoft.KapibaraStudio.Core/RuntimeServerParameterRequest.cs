namespace EsilvaSoft.KapibaraStudio.Core;

public static class RuntimeServerParameters
{
    private static readonly string[] AllowedParameterNames = [LogLevel, MaxLogSizeKb];

    public const string LogLevel = "logLevel";
    public const string MaxLogSizeKb = "maxLogSizeKB";
    public const int LogLevelMinimum = 0;
    public const int LogLevelMaximum = 5;
    public const int MaxLogSizeKbMinimum = 1;
    public const int MaxLogSizeKbMaximum = 10;
    public static IReadOnlyList<string> Allowlist => AllowedParameterNames;

    public static bool IsAllowed(string? parameterName) =>
        string.Equals(parameterName, LogLevel, StringComparison.Ordinal)
        || string.Equals(parameterName, MaxLogSizeKb, StringComparison.Ordinal);

    public static (int Minimum, int Maximum) GetRequestedValueBounds(string parameterName) => parameterName switch
    {
        LogLevel => (LogLevelMinimum, LogLevelMaximum),
        MaxLogSizeKb => (MaxLogSizeKbMinimum, MaxLogSizeKbMaximum),
        _ => throw new ArgumentException("O parâmetro runtime não está na allowlist.", nameof(parameterName))
    };
}

/// <summary>Changes one allowlisted, runtime-only server parameter after a captured read and explicit confirmation.</summary>
public sealed record RuntimeServerParameterRequest(
    string ParameterName,
    int PreviousValue,
    int RequestedValue,
    string ConfirmationParameterName)
{
    public RuntimeServerParameterRequest Validate()
    {
        var (minimum, maximum) = RuntimeServerParameters.GetRequestedValueBounds(ParameterName);
        var previousMinimum = ParameterName == RuntimeServerParameters.MaxLogSizeKb ? 0 : minimum;
        if (PreviousValue < previousMinimum)
            throw new ArgumentOutOfRangeException(nameof(PreviousValue), $"O valor anterior de {ParameterName} está fora do intervalo permitido.");

        if (RequestedValue < minimum || RequestedValue > maximum)
            throw new ArgumentOutOfRangeException(nameof(RequestedValue), $"O valor solicitado de {ParameterName} deve estar entre {minimum} e {maximum}.");

        if (!string.Equals(ParameterName, ConfirmationParameterName?.Trim(), StringComparison.Ordinal))
            throw new ArgumentException($"Digite {ParameterName} para confirmar a alteração runtime.", nameof(ConfirmationParameterName));

        return this;
    }
}

public sealed record RuntimeServerParameterMutationResult(
    string ParameterName,
    int PreviousValue,
    int CurrentValue,
    DateTimeOffset ChangedAtUtc);

public enum RuntimeServerParameterFailureKind
{
    PermissionDenied,
    Unsupported,
    ReadbackFailed
}

/// <summary>A sanitized server refusal or uncertain readback from an allowlisted runtime parameter command.</summary>
public sealed class RuntimeServerParameterException(RuntimeServerParameterFailureKind kind) : Exception
{
    public RuntimeServerParameterFailureKind Kind { get; } = kind;
}
