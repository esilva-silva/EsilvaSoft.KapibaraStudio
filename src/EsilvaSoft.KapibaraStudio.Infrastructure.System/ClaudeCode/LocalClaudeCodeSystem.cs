using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.ClaudeCode;

/// <summary>Única fronteira do provider Claude Code com arquivos, ambiente e processos locais.</summary>
public sealed class LocalClaudeCodeSystem(ILocalWorkspacePaths? workspacePaths = null) : IClaudeCodeSystem
{
    private readonly ILocalWorkspacePaths _workspacePaths = workspacePaths ?? new LocalWorkspacePathResolver();
    private static readonly Lock DebugLogGate = new();
    private const long MaximumDebugLogBytes = 1024 * 1024;

    public string HomeDirectory => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public string TemporaryDirectory => Path.GetTempPath();
    public string DefaultDatabasePath => _workspacePaths.GetDatabasePath();
    public bool IsEnvironmentVariableSet(string name) => Environment.GetEnvironmentVariable(name) is not null;

    public ClaudeCodeExecutableCandidate Locate(string? configuredPath)
    {
        var result = ClaudeCodeExecutableLocator.ForCurrentProcess().Locate(configuredPath);
        return new(result.Path, result.State == ClaudeCodeExecutableState.UnsupportedExecutable);
    }

    public string? ValidateExecutable(string path) => ClaudeCodeExecutableLocator.Validate(path);
    public ClaudeCodeExecutableFingerprint GetExecutableFingerprint(string path)
    {
        var file = new FileInfo(path);
        return new(file.LastWriteTimeUtc, file.Length);
    }

    public bool DirectoryExists(string path) => Directory.Exists(path);
    public string ResolveDirectoryLink(string path) =>
        new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public IClaudeCodeProcess Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, int maxStderrBytes) =>
        ClaudeCodeProcess.Start(executable, arguments, workingDirectory, maxStderrBytes);
    public Task<ClaudeCodeProbeResult> ProbeAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        TimeSpan timeout, int maxOutputBytes, int maxStderrBytes, CancellationToken cancellationToken) =>
        ClaudeCodeProbe.RunAsync(executable, arguments, workingDirectory, timeout, maxOutputBytes, maxStderrBytes, cancellationToken);
    public Task<ClaudeCodeAccountCommandState> RunVisibleAccountCommandAsync(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken) =>
        ClaudeCodeAccountCommands.RunVisibleAsync(executable, arguments, workingDirectory, timeout, cancellationToken);

    public void AppendDebugLog(string directory, string safeJson)
    {
        try
        {
            var line = safeJson + Environment.NewLine;
            lock (DebugLogGate)
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "claude-code-debug.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length + line.Length > MaximumDebugLogBytes)
                    File.Move(path, Path.Combine(directory, "claude-code-debug.1.jsonl"), overwrite: true);
                File.AppendAllText(path, line);
            }
        }
        catch (Exception)
        {
            // O diagnóstico nunca altera o resultado nem propaga dados locais por exceções.
        }
    }
}
