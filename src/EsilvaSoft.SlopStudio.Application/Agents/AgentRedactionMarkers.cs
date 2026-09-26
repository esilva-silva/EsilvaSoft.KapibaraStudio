using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Markers written by the shared secret redaction (<see cref="AgentContextProvider"/>). The agent only ever sees
/// redacted text, so a proposal that contains a marker absent from the original would write the marker over the real
/// secret: <c>propose_file_edit</c> must refuse such a hunk (or force human review, never auto-apply it).
/// </summary>
public static class AgentRedactionMarkers
{
    public static IReadOnlyList<string> All { get; } =
        [AgentContextProvider.RedactedConnectionString, AgentContextProvider.RedactedSecret];

    /// <summary>
    /// True when <paramref name="proposed"/> contains more occurrences of any marker than <paramref name="original"/>
    /// (ordinal comparison).
    /// </summary>
    public static bool IntroducesRedactionMarker(string? original, string? proposed)
    {
        if (string.IsNullOrEmpty(proposed))
        {
            return false;
        }

        foreach (var marker in All)
        {
            if (Count(proposed, marker) > Count(original, marker))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Hunk form: compares the hunk's proposed lines with its original lines.</summary>
    public static bool IntroducesRedactionMarker(AgentEditHunk hunk)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        return IntroducesRedactionMarker(string.Join('\n', hunk.OriginalLines), string.Join('\n', hunk.ProposedLines));
    }

    private static int Count(string? text, string marker)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        for (var index = text.IndexOf(marker, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(marker, index + marker.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
