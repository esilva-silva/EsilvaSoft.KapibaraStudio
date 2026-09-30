namespace EsilvaSoft.KapibaraStudio.SystemAdapters.ClaudeCode;
internal enum ClaudeCodeExecutableState { Found, NotFound, UnsupportedExecutable }
internal sealed class ClaudeCodeExecutableLocator(string? pathVariable, string homeDirectory, string? localAppData)
{
    private static readonly string[] WindowsShimExtensions = [".cmd", ".bat", ".ps1", ".js", ""];

    public static ClaudeCodeExecutableLocator ForCurrentProcess() => new(
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : null);

    /// <summary>Primeiro executável aceito, ou o motivo de recusa quando só havia candidatos inválidos.</summary>
    public (string? Path, ClaudeCodeExecutableState State) Locate(string? configuredPath)
    {
        if (configuredPath is not null)
        {
            return Validate(configuredPath) is { } accepted
                ? (accepted, ClaudeCodeExecutableState.Found)
                : (null, File.Exists(configuredPath) ? ClaudeCodeExecutableState.UnsupportedExecutable : ClaudeCodeExecutableState.NotFound);
        }

        var sawUnsupported = false;
        foreach (var candidate in Candidates())
        {
            if (Validate(candidate) is { } accepted)
            {
                return (accepted, ClaudeCodeExecutableState.Found);
            }

            sawUnsupported |= SafeFileExists(candidate);
        }

        sawUnsupported |= ShimCandidates().Any(SafeFileExists);
        return (null, sawUnsupported ? ClaudeCodeExecutableState.UnsupportedExecutable : ClaudeCodeExecutableState.NotFound);
    }

    private IEnumerable<string> Candidates()
    {
        var name = OperatingSystem.IsWindows() ? "claude.exe" : "claude";
        foreach (var directory in PathDirectories())
        {
            yield return Path.Combine(directory, name);
        }

        if (!string.IsNullOrEmpty(homeDirectory))
        {
            yield return Path.Combine(homeDirectory, ".local", "bin", name);
        }

        if (OperatingSystem.IsWindows() && !string.IsNullOrEmpty(localAppData))
        {
            var winget = Path.Combine(localAppData, "Microsoft", "WinGet");
            yield return Path.Combine(winget, "Links", name);
            var packages = Path.Combine(winget, "Packages");
            IEnumerable<string> directories;
            try
            {
                directories = Directory.Exists(packages)
                    ? Directory.EnumerateDirectories(packages, "Anthropic.ClaudeCode_*", SearchOption.TopDirectoryOnly).Order(StringComparer.Ordinal).ToArray()
                    : [];
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                directories = [];
            }

            foreach (var directory in directories)
            {
                yield return Path.Combine(directory, name);
            }
        }
    }

    /// <summary>Shims conhecidos (npm, scripts) que indicam instalação não nativa; nunca executados.</summary>
    private IEnumerable<string> ShimCandidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (var directory in PathDirectories())
        {
            foreach (var extension in WindowsShimExtensions)
            {
                yield return Path.Combine(directory, "claude" + extension);
            }
        }
    }

    private IEnumerable<string> PathDirectories() =>
        (pathVariable ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static entry => entry.Trim('"'))
            .Where(static entry => entry.Length > 0 && !entry.Any(char.IsControl) && Path.IsPathFullyQualified(entry))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>Caminho final (links resolvidos) aceito como executável nativo, ou nulo.</summary>
    internal static string? Validate(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate) || candidate.Any(char.IsControl))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(candidate);
            if (!info.Exists)
            {
                return null;
            }

            var final = info.ResolveLinkTarget(returnFinalTarget: true) is FileInfo target ? target : info;
            if (!final.Exists || (final.Attributes & FileAttributes.Directory) != 0)
            {
                return null;
            }

            if (OperatingSystem.IsWindows() &&
                (!string.Equals(Path.GetExtension(info.FullName), ".exe", StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(Path.GetExtension(final.FullName), ".exe", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            Span<byte> header = stackalloc byte[4];
            using (var stream = new FileStream(final.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
                {
                    return null;
                }
            }

            var native = OperatingSystem.IsWindows()
                ? header[0] == (byte)'M' && header[1] == (byte)'Z'
                : header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F';
            return native ? final.FullName : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}

