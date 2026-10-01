namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Recursos locais do Claude Code. A autenticação pertence exclusivamente à CLI oficial.</summary>
public interface IClaudeCodeSystem
{
    string HomeDirectory { get; }
    string TemporaryDirectory { get; }
    string DefaultDatabasePath { get; }
    bool IsEnvironmentVariableSet(string name);
    ClaudeCodeExecutableCandidate Locate(string? configuredPath);
    string? ValidateExecutable(string path);
    ClaudeCodeExecutableFingerprint GetExecutableFingerprint(string path);
    bool DirectoryExists(string path);
    string ResolveDirectoryLink(string path);
    void CreateDirectory(string path);
    IClaudeCodeProcess Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, int maxStderrBytes);
    Task<ClaudeCodeProbeResult> ProbeAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        TimeSpan timeout, int maxOutputBytes, int maxStderrBytes, CancellationToken cancellationToken);
    Task<ClaudeCodeAccountCommandState> RunVisibleAccountCommandAsync(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken);
    /// <summary>Recebe somente JSON já filtrado pela política de diagnóstico do provider.</summary>
    void AppendDebugLog(string directory, string safeJson);
}

public sealed record ClaudeCodeExecutableCandidate(string? Path, bool Unsupported = false);
public readonly record struct ClaudeCodeExecutableFingerprint(DateTime LastWriteTimeUtc, long Length);
public readonly record struct ClaudeCodeProbeResult(bool TimedOut, bool Overflow, int? ExitCode, string Output);

public enum ClaudeCodeAccountCommandState
{
    Completed,
    StillRunning,
    ExecutableUnavailable,
    NoVisibleTerminal,
    StartFailed,
    CommandFailed,
}

/// <summary>Processo isolado por operação, com captura limitada de stderr e propriedade da árvore.</summary>
public interface IClaudeCodeProcess : IAsyncDisposable
{
    Stream StandardOutput { get; }
    StreamWriter StandardInput { get; }
    bool HasExited { get; }
    int? ExitCode { get; }
    bool WasKilled { get; }
    string StderrSnapshot { get; }
    void KillTree();
    Task<bool> WaitForExitAsync(TimeSpan timeout);
}
