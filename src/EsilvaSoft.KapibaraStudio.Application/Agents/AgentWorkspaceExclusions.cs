using System.Text;
using System.Text.RegularExpressions;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>
/// Workspace exclusion globs, shared by the attachment resolver (matching) and the mode policy (native deny rules), so
/// both sides interpret a pattern the same way. Semantics (gitignore-like, case-insensitive, <c>/</c> separators):
/// a pattern without <c>/</c> matches any path segment (file or folder name) at any depth; a pattern with <c>/</c> is
/// anchored at the workspace root (a leading <c>/</c> is optional); a match on a folder excludes everything inside it;
/// <c>*</c> never crosses <c>/</c>, <c>**</c> crosses folders, <c>?</c> is one character. Patterns with characters
/// the rule syntax cannot express safely are invalid and callers fail closed. A match that cannot complete in time
/// (regex timeout) counts as excluded.
/// <para>
/// The native <c>Read(...)</c> rules are an approximation, not an equivalence: the Claude Code CLI applies <c>Read</c>
/// rules to Glob/Grep only on a best-effort basis and its case sensitivity may differ from this matcher. For that
/// reason the mode policy does not expose native Grep while exclusions are active, and the adapter lote (CLP-4) must
/// homologate the rules against the real CLI.
/// </para>
/// </summary>
public static class AgentWorkspaceExclusions
{
    public const int MaximumPatternChars = 256;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(250);

    public static bool IsValidPattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > MaximumPatternChars || pattern != pattern.Trim())
        {
            return false;
        }

        var trimmed = pattern.TrimStart('/');
        if (trimmed.Length == 0 || trimmed.Contains("//", StringComparison.Ordinal))
        {
            return false;
        }

        if (trimmed.Split('/').Any(static segment => segment is "." or ".."))
        {
            return false;
        }

        return !pattern.Any(static c => char.IsControl(c) ||
            c is '\\' or '(' or ')' or '[' or ']' or '{' or '}' or '!' or '"' or '\'' or '`' or ':' or '~');
    }

    /// <summary>True when <paramref name="relativePath"/> (relative to the workspace) matches any valid pattern.</summary>
    /// <exception cref="ArgumentException">A pattern is invalid (callers validate first and fail closed).</exception>
    public static bool IsExcluded(string relativePath, IReadOnlyList<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(patterns);
        var path = NormalizeRelativePath(relativePath);
        if (path.Length == 0)
        {
            return false;
        }

        var segments = path.Split('/');
        foreach (var pattern in patterns)
        {
            if (!IsValidPattern(pattern))
            {
                throw new ArgumentException("Padrão de exclusão inválido.", nameof(patterns));
            }

            try
            {
                if (!pattern.Contains('/', StringComparison.Ordinal))
                {
                    var segmentRegex = new Regex("^" + Translate(pattern) + "$", Options, MatchTimeout);
                    if (segments.Any(segment => segmentRegex.IsMatch(segment)))
                    {
                        return true;
                    }

                    continue;
                }

                var anchored = pattern.TrimStart('/');
                var regex = new Regex("^" + Translate(anchored) + "(?:/.*)?$", Options, MatchTimeout);
                if (regex.IsMatch(path))
                {
                    return true;
                }
            }
            catch (RegexMatchTimeoutException)
            {
                return true; // Fail closed.
            }
        }

        return false;
    }

    /// <summary>
    /// Claude Code-style <c>Read(...)</c> deny rules approximating the patterns (see the class remarks: Glob/Grep
    /// coverage is best-effort in the CLI). Relative rules resolve against the session working directory, which is the
    /// workspace folder.
    /// </summary>
    /// <exception cref="ArgumentException">A pattern is invalid.</exception>
    public static IReadOnlyList<string> ToNativeReadRules(IReadOnlyList<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(patterns);
        var rules = new List<string>();
        foreach (var pattern in patterns)
        {
            if (!IsValidPattern(pattern))
            {
                throw new ArgumentException("Padrão de exclusão inválido.", nameof(patterns));
            }

            if (!pattern.Contains('/', StringComparison.Ordinal))
            {
                rules.Add("Read(**/" + pattern + ")");
                rules.Add("Read(**/" + pattern + "/**)");
                continue;
            }

            var anchored = pattern.TrimStart('/');
            var prefix = anchored.StartsWith("**/", StringComparison.Ordinal) ? string.Empty : "./";
            rules.Add("Read(" + prefix + anchored + ")");
            if (!anchored.EndsWith("/**", StringComparison.Ordinal))
            {
                rules.Add("Read(" + prefix + anchored + "/**)");
            }
        }

        return [.. rules.Distinct(StringComparer.Ordinal)];
    }

    internal static string NormalizeRelativePath(string relativePath)
    {
        var path = relativePath.Replace('\\', '/');
        while (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        return path.Trim('/');
    }

    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private static string Translate(string glob)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            if (c == '*' && i + 1 < glob.Length && glob[i + 1] == '*')
            {
                var followedBySlash = i + 2 < glob.Length && glob[i + 2] == '/';
                builder.Append(followedBySlash ? "(?:.*/)?" : ".*");
                i += followedBySlash ? 2 : 1;
            }
            else if (c == '*')
            {
                builder.Append("[^/]*");
            }
            else if (c == '?')
            {
                builder.Append("[^/]");
            }
            else
            {
                builder.Append(Regex.Escape(c.ToString()));
            }
        }

        return builder.ToString();
    }
}
