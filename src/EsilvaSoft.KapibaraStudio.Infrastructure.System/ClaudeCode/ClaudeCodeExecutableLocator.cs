namespace EsilvaSoft.KapibaraStudio.SystemAdapters.ClaudeCode;
internal enum ClaudeCodeExecutableState { Found, NotFound, UnsupportedExecutable }
internal sealed class ClaudeCodeExecutableLocator(string? pathVariable, string homeDirectory, string? localAppData,
    Func<string, string?>? validateCandidate = null, Func<string, bool>? fileExists = null, bool? isWindows = null)
{
    private static readonly string[] WindowsShimExtensions = [".cmd", ".bat", ".ps1", ".js", ""];
    private readonly Func<string, string?> _validate = validateCandidate ?? Validate;
    private readonly Func<string, bool> _exists = fileExists ?? SafeFileExists;
    private readonly bool _isWindows = isWindows ?? OperatingSystem.IsWindows();

    public static ClaudeCodeExecutableLocator ForCurrentProcess() => new(
        Environment.GetEnvironmentVariable("PATH"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) : null);

    /// <summary>Primeiro executável aceito, ou o motivo de recusa quando só havia candidatos inválidos.</summary>
    public (string? Path, ClaudeCodeExecutableState State) Locate(string? configuredPath)
    {
        if (configuredPath is not null)
        {
            return _validate(configuredPath) is { } accepted
                ? (accepted, ClaudeCodeExecutableState.Found)
                : (null, _exists(configuredPath) ? ClaudeCodeExecutableState.UnsupportedExecutable : ClaudeCodeExecutableState.NotFound);
        }

        var sawUnsupported = false;
        foreach (var candidate in Candidates())
        {
            if (_validate(candidate) is { } accepted)
            {
                return (accepted, ClaudeCodeExecutableState.Found);
            }

            sawUnsupported |= _exists(candidate);
        }

        sawUnsupported |= ShimCandidates().Any(_exists);
        return (null, sawUnsupported ? ClaudeCodeExecutableState.UnsupportedExecutable : ClaudeCodeExecutableState.NotFound);
    }

    private IEnumerable<string> Candidates()
    {
        var name = _isWindows ? "claude.exe" : "claude";
        foreach (var directory in PathDirectories())
        {
            yield return Path.Combine(directory, name);
        }

        if (!string.IsNullOrEmpty(homeDirectory))
        {
            yield return Path.Combine(homeDirectory, ".local", "bin", name);
        }

        if (_isWindows && !string.IsNullOrEmpty(localAppData))
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
        if (!_isWindows)
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
            .Distinct(_isWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>Caminho final (links resolvidos) aceito como executável nativo, ou nulo.</summary>
    internal static string? Validate(string candidate) => Validate(candidate, OperatingSystem.IsWindows(),
        OperatingSystem.IsLinux(), ReadCandidate);

    internal sealed record CandidateMetadata(string FinalPath, bool IsDirectory, UnixFileMode Mode,
        Func<byte[]> ReadHeader);

    /// <summary>Pure selection policy; metadata, link resolution and bytes belong to the supplied probe.</summary>
    internal static string? Validate(string candidate, bool isWindows, bool isLinux,
        Func<string, CandidateMetadata?> probe)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate) || candidate.Any(char.IsControl))
        {
            return null;
        }

        try
        {
            if (probe(candidate) is not { } final || final.IsDirectory || !Path.IsPathFullyQualified(final.FinalPath))
            {
                return null;
            }

            if (isLinux &&
                (final.Mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) == 0)
            {
                return null;
            }

            if (isWindows &&
                (!string.Equals(Path.GetExtension(candidate), ".exe", StringComparison.OrdinalIgnoreCase) ||
                 !string.Equals(Path.GetExtension(final.FinalPath), ".exe", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var header = final.ReadHeader();
            if (header.Length < 4) return null;

            var native = isWindows
                ? header[0] == (byte)'M' && header[1] == (byte)'Z'
                : header[0] == 0x7F && header[1] == (byte)'E' && header[2] == (byte)'L' && header[3] == (byte)'F';
            return native ? final.FinalPath : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static CandidateMetadata? ReadCandidate(string candidate)
    {
        var info = new FileInfo(candidate);
        if (!info.Exists) return null;
        var final = info.ResolveLinkTarget(returnFinalTarget: true) is FileInfo target ? target : info;
        if (!final.Exists) return null;
        return new(final.FullName, (final.Attributes & FileAttributes.Directory) != 0,
            OperatingSystem.IsLinux() ? final.UnixFileMode : (UnixFileMode)0,
            () =>
            {
                var header = new byte[4];
                using var stream = new FileStream(final.FullName, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                return stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length ? [] : header;
            });
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

