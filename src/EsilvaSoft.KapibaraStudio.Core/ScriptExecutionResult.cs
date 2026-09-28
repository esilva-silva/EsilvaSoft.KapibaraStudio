namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record ScriptExecutionResult(
    int ExitCode,
    IReadOnlyList<string> Results,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration);
