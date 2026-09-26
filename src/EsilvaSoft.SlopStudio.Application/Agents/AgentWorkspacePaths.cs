namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>Typed reason a path was refused. Codes are safe to show; they never embed the path.</summary>
public enum AgentWorkspacePathError
{
    None = 0,

    /// <summary>No usable workspace folder (missing, relative or not an existing directory).</summary>
    NoWorkspace,

    /// <summary>Empty, malformed or not normalizable.</summary>
    InvalidPath,

    /// <summary>
    /// A segment could alias another file: an NTFS alternate data stream (<c>:</c>), an 8.3 short name (<c>~</c>
    /// followed by a digit) or a trailing dot/space (Windows strips them). Refused on every platform so exclusions
    /// cannot be bypassed through an alias.
    /// </summary>
    UnsafePath,

    /// <summary>Not strictly inside the workspace folder after full-path normalization.</summary>
    OutsideWorkspace,

    /// <summary>A symbolic link or junction is traversed: its target cannot be proven to be inside the workspace.</summary>
    LinkTraversal,

    /// <summary>Matches a workspace exclusion glob.</summary>
    Excluded,

    /// <summary>An exclusion glob is invalid; every path is refused (fail closed).</summary>
    InvalidExclusion,
}

/// <summary>
/// Single place for workspace path safety, shared by the attachment resolver and <c>propose_file_edit</c>: full-path
/// normalization, strict containment (a workspace at a drive root included), alias segments (ADS, 8.3, trailing
/// dot/space), link traversal and exclusion matching by the <b>real</b> path (never a tab title). Comparison is
/// case-insensitive on Windows and macOS. Pure except for the link check, which inspects the file system.
/// </summary>
public static class AgentWorkspacePaths
{
    /// <summary>Normalized workspace root (no trailing separator except for a drive/file-system root).</summary>
    public static bool TryGetWorkspaceRoot(string? folder, out string root)
    {
        root = string.Empty;
        // The root itself is the user's choice and may legitimately be an 8.3 form (e.g. a temp folder); only the
        // segments below it are checked for aliases.
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder) || !TryGetFullPath(folder, out var full))
        {
            return false;
        }

        try
        {
            if (!Directory.Exists(full))
            {
                return false;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        root = full;
        return true;
    }

    /// <summary>
    /// Resolves <paramref name="relativeOrAbsolute"/> against <paramref name="workspaceRoot"/> and checks, in order:
    /// validity, alias segments, strict containment, exclusions (by the path relative to the workspace, with <c>/</c>)
    /// and link traversal. <paramref name="relativePath"/> uses <c>/</c> separators.
    /// </summary>
    public static bool TryResolveInside(
        string? workspaceRoot,
        string? relativeOrAbsolute,
        IReadOnlyList<string>? exclusions,
        out string fullPath,
        out string relativePath,
        out AgentWorkspacePathError error)
    {
        fullPath = string.Empty;
        relativePath = string.Empty;
        if (!TryGetWorkspaceRoot(workspaceRoot, out var root))
        {
            error = AgentWorkspacePathError.NoWorkspace;
            return false;
        }

        if (string.IsNullOrWhiteSpace(relativeOrAbsolute) ||
            !TryGetFullPath(Path.IsPathRooted(relativeOrAbsolute) ? relativeOrAbsolute : Path.Combine(root, relativeOrAbsolute),
                out var full))
        {
            error = AgentWorkspacePathError.InvalidPath;
            return false;
        }

        if (!IsStrictlyInside(full, root))
        {
            error = AgentWorkspacePathError.OutsideWorkspace;
            return false;
        }

        var nativeRelative = Path.GetRelativePath(root, full);
        if (HasUnsafeSegment(nativeRelative))
        {
            error = AgentWorkspacePathError.UnsafePath;
            return false;
        }

        var relative = AgentWorkspaceExclusions.NormalizeRelativePath(nativeRelative);
        error = CheckExclusions(relative, exclusions);
        if (error != AgentWorkspacePathError.None)
        {
            return false;
        }

        if (TraversesLink(full, root))
        {
            error = AgentWorkspacePathError.LinkTraversal;
            return false;
        }

        fullPath = full;
        relativePath = relative;
        return true;
    }

    /// <summary>
    /// Exclusion check for a file that may be outside the workspace (active file, external attachment), by its real
    /// full path: relative to the workspace when strictly inside it, otherwise every segment of the full path below
    /// its root (so <c>.env</c> and <c>**/secrets/**</c> still match). Alias segments of the checked part are refused
    /// (for a file outside the workspace that is the whole path below its root).
    /// </summary>
    public static AgentWorkspacePathError CheckFile(string? fullPath, string? workspaceRoot, IReadOnlyList<string>? exclusions)
    {
        if (string.IsNullOrWhiteSpace(fullPath) || !Path.IsPathFullyQualified(fullPath) || !TryGetFullPath(fullPath, out var full))
        {
            return AgentWorkspacePathError.InvalidPath;
        }

        var relative = TryGetWorkspaceRoot(workspaceRoot, out var root) && IsStrictlyInside(full, root)
            ? Path.GetRelativePath(root, full)
            : full[(Path.GetPathRoot(full)?.Length ?? 0)..];
        if (HasUnsafeSegment(relative))
        {
            return AgentWorkspacePathError.UnsafePath;
        }

        return CheckExclusions(AgentWorkspaceExclusions.NormalizeRelativePath(relative), exclusions);
    }

    /// <summary>True when <paramref name="fullPath"/> is strictly below <paramref name="root"/> (never equal).</summary>
    public static bool IsStrictlyInside(string fullPath, string root)
    {
        ArgumentNullException.ThrowIfNull(fullPath);
        ArgumentNullException.ThrowIfNull(root);
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return fullPath.Length > prefix.Length && fullPath.StartsWith(prefix, Comparison);
    }

    /// <summary>
    /// True when any segment of <paramref name="path"/> (below its root, if rooted) is an alias: ADS <c>:</c>, 8.3
    /// <c>~digit</c>, or a trailing dot/space.
    /// </summary>
    public static bool HasUnsafeSegment(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var rootLength = Path.GetPathRoot(path)?.Length ?? 0;
        var segments = path[rootLength..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment.Contains(':', StringComparison.Ordinal) || segment.EndsWith('.') || segment.EndsWith(' '))
            {
                return true;
            }

            for (var i = 0; i + 1 < segment.Length; i++)
            {
                if (segment[i] == '~' && char.IsAsciiDigit(segment[i + 1]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal static StringComparison Comparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static bool TryGetFullPath(string path, out string full)
    {
        full = string.Empty;
        if (path.Any(char.IsControl) || path.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException
                                              or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static AgentWorkspacePathError CheckExclusions(string relativePath, IReadOnlyList<string>? exclusions)
    {
        var patterns = exclusions ?? Core.Agents.AgentWorkspacePermissions.DefaultExclusions;
        if (!patterns.All(AgentWorkspaceExclusions.IsValidPattern))
        {
            return AgentWorkspacePathError.InvalidExclusion;
        }

        return AgentWorkspaceExclusions.IsExcluded(relativePath, patterns)
            ? AgentWorkspacePathError.Excluded
            : AgentWorkspacePathError.None;
    }

    private static bool TraversesLink(string fullPath, string root)
    {
        try
        {
            for (var current = fullPath; IsStrictlyInside(current, root); current = Path.GetDirectoryName(current) ?? root)
            {
                FileSystemInfo info = File.Exists(current) ? new FileInfo(current) : new DirectoryInfo(current);
                if (info.Exists && info.LinkTarget is not null)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }
}
