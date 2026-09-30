using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Processes;

/// <summary>OS boundary for official CLI discovery, private storage and process-tree ownership.</summary>
public sealed class LocalCodexAppServerProcessLauncher : ICodexAppServerProcessLauncher
{
    public IAgentStdioProcess Start(string? executable, IReadOnlyList<string> arguments,
        string? workingDirectory, string codexHome, int maxStderrBytes)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentException.ThrowIfNullOrWhiteSpace(codexHome);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStderrBytes, 1);
        var resolved = executable ?? FindExecutable();
        if (!Path.IsPathFullyQualified(resolved) || !File.Exists(resolved))
            throw new FileNotFoundException("Codex CLI is unavailable.");
        var work = Path.GetFullPath(workingDirectory ?? Environment.CurrentDirectory);
        var home = Path.GetFullPath(codexHome);
        return CodexAppServerProcess.Start(resolved, arguments, work, home, maxStderrBytes);
    }

    private static string FindExecutable()
    {
        var name = OperatingSystem.IsWindows() ? "codex.exe" : "codex";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "")
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }
        throw new FileNotFoundException("Codex CLI is unavailable.");
    }
}
