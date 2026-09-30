using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.Testing;

/// <summary>Fronteira em memória para composição sem instalação, processo ou arquivo real.</summary>
internal sealed class MissingClaudeCodeSystem : IClaudeCodeSystem
{
    private static string Root => Path.DirectorySeparatorChar == '\\' ? @"C:\test-workspace" : "/test-workspace";
    public string HomeDirectory => Path.Combine(Root, "home");
    public string TemporaryDirectory => Path.Combine(Root, "temp");
    public string DefaultDatabasePath => Path.Combine(Root, "data", "workspace.db");
    public string AcceptedWorkspace { get; } = Path.Combine(Root, "project");
    public bool IsEnvironmentVariableSet(string name) => false;
    public ClaudeCodeExecutableCandidate Locate(string? configuredPath) => new(null);
    public string? ValidateExecutable(string path) => null;
    public ClaudeCodeExecutableFingerprint GetExecutableFingerprint(string path) => throw new InvalidOperationException("Nenhum executável no cenário.");
    public bool DirectoryExists(string path) => path == AcceptedWorkspace || path == HomeDirectory;
    public string ResolveDirectoryLink(string path) => path;
    public void CreateDirectory(string path) => throw new InvalidOperationException("Composição/prévia não cria diretórios.");
    public IClaudeCodeProcess Start(string executable, IReadOnlyList<string> arguments, string workingDirectory, int maxStderrBytes) =>
        throw new InvalidOperationException("Composição não inicia processo.");
    public Task<ClaudeCodeProbeResult> ProbeAsync(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        TimeSpan timeout, int maxOutputBytes, int maxStderrBytes, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Composição não consulta CLI.");
    public Task<ClaudeCodeAccountCommandState> RunVisibleAccountCommandAsync(string executable, IReadOnlyList<string> arguments,
        string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("CLI ausente não abre login.");
    public void AppendDebugLog(string directory, string safeJson) => throw new InvalidOperationException("Composição não grava logs.");
}
