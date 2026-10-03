using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;

/// <summary>Uses an explicit executable, the native per-user Windows installation, then PATH.</summary>
public sealed class LocalCopilotCliConfiguration : ICopilotCliConfiguration
{
    private string? _executablePath;
    public string? ExecutablePath
    {
        get => Volatile.Read(ref _executablePath);
        set => Volatile.Write(ref _executablePath, NormalizeExecutablePath(value, OperatingSystem.IsWindows()));
    }

    public string? ResolveExecutablePath() => Resolve(ExecutablePath,
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    public string? ValidateExecutablePath(string? path)
    {
        var normalized = NormalizeExecutablePath(path, OperatingSystem.IsWindows());
        return normalized is null ? null : Resolve(normalized, null, null);
    }

    internal static string? NormalizeExecutablePath(string? value, bool windows)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim();
        if (!Path.IsPathFullyQualified(path) || path.Any(char.IsControl) ||
            (windows && !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Informe o caminho absoluto de um executável nativo do GitHub Copilot.", nameof(value));
        return Path.GetFullPath(path);
    }

    internal static string? Resolve(string? configuredPath, string? path, string? localApplicationData,
        bool? isWindows = null, Func<string, bool>? exists = null, Func<string, bool>? executableProbe = null)
    {
        var windows = isWindows ?? OperatingSystem.IsWindows();
        var fileExists = exists ?? File.Exists;
        bool IsAvailable(string candidate) => fileExists(candidate) &&
            ((executableProbe is null && !OperatingSystem.IsLinux()) ||
             (executableProbe ?? LinuxExecutableProbe.IsExecutable)(candidate));

        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var normalized = NormalizeExecutablePath(configuredPath, windows)!;
            if (!IsAvailable(normalized))
                throw new InvalidOperationException("CopilotConfiguredCliInvalid");
            return normalized;
        }

        if (windows && !string.IsNullOrWhiteSpace(localApplicationData) && Path.IsPathFullyQualified(localApplicationData))
        {
            var installed = Path.GetFullPath(Path.Combine(localApplicationData, "GitHubCopilotCLI", "copilot.exe"));
            try
            {
                if (IsAvailable(installed)) return installed;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Automatic discovery can examine the next native installation.
            }
        }
        return LocalCopilotAccountCommands.FindCliExecutable(path, windows, fileExists, executableProbe);
    }
}
