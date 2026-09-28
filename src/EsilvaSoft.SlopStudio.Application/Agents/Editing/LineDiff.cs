using System.Text;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents.Editing;

/// <summary>Outcome of applying or reverting one hunk on a given text.</summary>
public enum LineDiffHunkStatus
{
    /// <summary>The hunk was located; <see cref="LineDiffHunkResult.Text"/> and <see cref="LineDiffHunkResult.Edit"/> are set.</summary>
    Succeeded,

    /// <summary>The expected lines (with anchors) are no longer present, or are ambiguous; nothing changed.</summary>
    Stale,
}

/// <summary>
/// A replacement in character offsets of the text it was computed on, for editors that apply only the region
/// (e.g. <c>document.Replace(Offset, Length, Replacement)</c> inside one undo group). Text outside the region, line
/// terminators included, is untouched.
/// </summary>
public sealed record LineDiffTextEdit(int Offset, int Length, string Replacement)
{
    public string ApplyTo(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return string.Concat(text.AsSpan(0, Offset), Replacement, text.AsSpan(Offset + Length));
    }

    public override string ToString() =>
        $"{nameof(LineDiffTextEdit)} {{ Offset = {Offset}, Length = {Length}, ReplacementLength = {Replacement.Length} }}";
}

/// <summary>Result of applying/reverting one hunk. <see cref="ToString"/> omits the text.</summary>
public sealed record LineDiffHunkResult(LineDiffHunkStatus Status, string? Text, LineDiffTextEdit? Edit = null)
{
    public bool Succeeded => Status == LineDiffHunkStatus.Succeeded;

    internal static LineDiffHunkResult Stale { get; } = new(LineDiffHunkStatus.Stale, null);

    public override string ToString() => $"{nameof(LineDiffHunkResult)} {{ Status = {Status}, Edit = {Edit} }}";
}

/// <summary>
/// Line diff (Myers, greedy O((N+M)·D)) with per-hunk apply/revert. Texts are split on CRLF, LF or CR; lines carry no
/// terminator and a trailing line break is represented by a final empty line. Apply/revert replace only the hunk's
/// region, so every terminator outside it is preserved byte for byte (mixed files included); lines written inside the
/// region use the terminator of the region's own line (or its nearest predecessor), so apply followed by revert
/// restores the original exactly unless the region itself mixed terminators. Hunks closer than
/// <see cref="ContextLines"/> unchanged lines are merged, so the anchors of a hunk never overlap another hunk's changed
/// lines. Inputs above <see cref="MaximumInputChars"/> per side are refused (<see cref="TryCompute"/>); above
/// <see cref="MaximumEditDistance"/> the differing middle becomes a single hunk (bounded memory).
/// </summary>
public static class LineDiff
{
    public const int ContextLines = 2;

    public const int MaximumEditDistance = 2048;

    /// <summary>Maximum length (UTF-16 chars) of each side of <see cref="Compute"/>.</summary>
    public const int MaximumInputChars = 256 * 1024;

    /// <summary>Typed form: false (and no hunks) when a side exceeds <see cref="MaximumInputChars"/>.</summary>
    public static bool TryCompute(string original, string proposed, out IReadOnlyList<AgentEditHunk> hunks)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(proposed);
        if (original.Length > MaximumInputChars || proposed.Length > MaximumInputChars)
        {
            hunks = [];
            return false;
        }

        hunks = ComputeCore(original, proposed);
        return true;
    }

    /// <exception cref="ArgumentOutOfRangeException">A side exceeds <see cref="MaximumInputChars"/>; use <see cref="TryCompute"/>.</exception>
    public static IReadOnlyList<AgentEditHunk> Compute(string original, string proposed) =>
        TryCompute(original, proposed, out var hunks)
            ? hunks
            : throw new ArgumentOutOfRangeException(nameof(original), "Texto grande demais para o diff por linhas.");

    private static List<AgentEditHunk> ComputeCore(string original, string proposed)
    {
        var a = LineTable.Parse(original).Lines;
        var b = LineTable.Parse(proposed).Lines;
        var merged = Merge(Diff(a, b));
        var hunks = new List<AgentEditHunk>(merged.Count);
        foreach (var region in merged)
        {
            var beforeStart = Math.Max(0, region.AStart - ContextLines);
            var afterEnd = Math.Min(a.Length, region.AEnd + ContextLines);
            hunks.Add(new AgentEditHunk(
                hunks.Count,
                region.AStart,
                a[region.AStart..region.AEnd],
                b[region.BStart..region.BEnd])
            {
                ProposedStartLine = region.BStart,
                AnchorBefore = a[beforeStart..region.AStart],
                AnchorAfter = a[region.AEnd..afterEnd],
            });
        }

        return hunks;
    }

    /// <summary>
    /// Replaces the hunk's original lines with its proposed lines in <paramref name="text"/>. The change is located by
    /// its anchors and lines: the candidate exactly at <paramref name="hintLine"/> (default
    /// <see cref="AgentEditHunk.OriginalStartLine"/>) wins; otherwise a single candidate is used, and several candidates
    /// are ambiguous (<see cref="LineDiffHunkStatus.Stale"/>). Callers that track which hunks are applied should pass the
    /// exact expected line. <paramref name="allowShiftedBoundaryAnchor"/> lets that tracked hint locate a hunk whose
    /// short start/end context moved away from the document boundary. <paramref name="fallbackLineEnding"/> (default <see cref="Environment.NewLine"/>) is used
    /// only when the text has no line break at all.
    /// </summary>
    public static LineDiffHunkResult ApplyHunk(
        string text, AgentEditHunk hunk, int? hintLine = null, string? fallbackLineEnding = null,
        bool allowShiftedBoundaryAnchor = false)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        return Replace(text, hunk, hunk.OriginalLines, hunk.ProposedLines, hintLine ?? hunk.OriginalStartLine,
            fallbackLineEnding, allowShiftedBoundaryAnchor);
    }

    /// <summary>
    /// Replaces the hunk's proposed lines with its original lines in <paramref name="text"/>; the default hint is
    /// <see cref="AgentEditHunk.ProposedStartLine"/> (exact when every hunk is applied). Same location rules and
    /// <paramref name="allowShiftedBoundaryAnchor"/> behavior as <see cref="ApplyHunk"/>.
    /// </summary>
    public static LineDiffHunkResult RevertHunk(
        string text, AgentEditHunk hunk, int? hintLine = null, string? fallbackLineEnding = null,
        bool allowShiftedBoundaryAnchor = false)
    {
        ArgumentNullException.ThrowIfNull(hunk);
        return Replace(text, hunk, hunk.ProposedLines, hunk.OriginalLines, hintLine ?? hunk.ProposedStartLine,
            fallbackLineEnding, allowShiftedBoundaryAnchor);
    }

    /// <summary>
    /// The first line terminator of <paramref name="text"/>, or <paramref name="fallback"/> (when it is a valid
    /// terminator; otherwise <see cref="Environment.NewLine"/>) when the text has none.
    /// </summary>
    public static string DetectLineEnding(string text, string? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var index = text.AsSpan().IndexOfAny('\r', '\n');
        if (index < 0)
        {
            return IsTerminator(fallback) ? fallback! : Environment.NewLine;
        }

        return text[index] == '\n' ? "\n" : index + 1 < text.Length && text[index + 1] == '\n' ? "\r\n" : "\r";
    }

    /// <summary>Normalizes every line break of <paramref name="text"/> to <paramref name="lineEnding"/>.</summary>
    public static string NormalizeLineEndings(string text, string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!IsTerminator(lineEnding))
        {
            throw new ArgumentException("Terminador de linha inválido.", nameof(lineEnding));
        }

        return string.Join(lineEnding, LineTable.Parse(text).Lines);
    }

    private static bool IsTerminator(string? value) => value is "\n" or "\r\n" or "\r";

    /// <summary>Lines with their offsets and terminators; the last line never has a terminator.</summary>
    private sealed class LineTable
    {
        private LineTable(string[] lines, int[] starts, string[] terminators, int length)
        {
            Lines = lines;
            Starts = starts;
            Terminators = terminators;
            Length = length;
        }

        public string[] Lines { get; }

        public int[] Starts { get; }

        public string[] Terminators { get; }

        public int Length { get; }

        public int Count => Lines.Length;

        public int ContentEnd(int line) => Starts[line] + Lines[line].Length;

        public static LineTable Parse(string text)
        {
            var lines = new List<string>();
            var starts = new List<int>();
            var terminators = new List<string>();
            var start = 0;
            for (var i = 0; i < text.Length; i++)
            {
                if (text[i] is not ('\r' or '\n'))
                {
                    continue;
                }

                lines.Add(text[start..i]);
                starts.Add(start);
                if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    terminators.Add("\r\n");
                    i++;
                }
                else
                {
                    terminators.Add(text[i] == '\r' ? "\r" : "\n");
                }

                start = i + 1;
            }

            lines.Add(text[start..]);
            starts.Add(start);
            terminators.Add(string.Empty);
            return new LineTable([.. lines], [.. starts], [.. terminators], text.Length);
        }

        /// <summary>Terminator of the line at <paramref name="line"/> or its nearest predecessor that has one.</summary>
        public string LocalTerminator(int line, string? fallback)
        {
            for (var index = Math.Min(line, Count - 2); index >= 0; index--)
            {
                if (Terminators[index].Length > 0)
                {
                    return Terminators[index];
                }
            }

            return IsTerminator(fallback) ? fallback! : Environment.NewLine;
        }
    }

    private static LineDiffHunkResult Replace(
        string text,
        AgentEditHunk hunk,
        IReadOnlyList<string> expected,
        IReadOnlyList<string> replacement,
        int hint,
        string? fallbackLineEnding,
        bool allowShiftedBoundaryAnchor)
    {
        ArgumentNullException.ThrowIfNull(text);
        var table = LineTable.Parse(text);
        var lines = table.Lines;
        var before = hunk.AnchorBefore;
        var after = hunk.AnchorAfter;
        var window = before.Count + expected.Count + after.Count;
        var candidates = 0;
        var single = -1;
        var exact = -1;
        for (var start = 0; start + window <= lines.Length; start++)
        {
            // Short anchors mean the hunk touches the start/end of the text: then the position is fixed.
            if ((!allowShiftedBoundaryAnchor && before.Count < ContextLines && start != 0) ||
                (!allowShiftedBoundaryAnchor && after.Count < ContextLines && start + window != lines.Length) ||
                !Matches(lines, start, before) ||
                !Matches(lines, start + before.Count, expected) ||
                !Matches(lines, start + before.Count + expected.Count, after))
            {
                continue;
            }

            candidates++;
            single = start;
            if (start + before.Count == hint)
            {
                exact = start;
            }
        }

        // The exact position wins; a single candidate elsewhere is safe; several candidates are ambiguous.
        var chosen = exact >= 0 ? exact : candidates == 1 ? single : -1;
        if (chosen < 0)
        {
            return LineDiffHunkResult.Stale;
        }

        var edit = BuildEdit(table, chosen + before.Count, expected.Count, replacement, fallbackLineEnding);
        return new LineDiffHunkResult(LineDiffHunkStatus.Succeeded, edit.ApplyTo(text), edit);
    }

    private static LineDiffTextEdit BuildEdit(
        LineTable table, int start, int count, IReadOnlyList<string> replacement, string? fallbackLineEnding)
    {
        var eol = table.LocalTerminator(start, fallbackLineEnding);
        var n = table.Count;
        var builder = new StringBuilder();
        if (count > 0 && start + count < n)
        {
            // Whole lines with their terminators; the next line starts right after the region.
            foreach (var line in replacement)
            {
                builder.Append(line).Append(eol);
            }

            var offset = table.Starts[start];
            return new LineDiffTextEdit(offset, table.Starts[start + count] - offset, builder.ToString());
        }

        if (count > 0)
        {
            // The region ends at the last line, which has no terminator.
            if (replacement.Count > 0)
            {
                var offset = table.Starts[start];
                return new LineDiffTextEdit(offset, table.Length - offset, string.Join(eol, replacement));
            }

            // Removing the tail also removes the terminator of the new last line.
            var tailStart = start == 0 ? 0 : table.ContentEnd(start - 1);
            return new LineDiffTextEdit(tailStart, table.Length - tailStart, string.Empty);
        }

        if (replacement.Count == 0)
        {
            return new LineDiffTextEdit(0, 0, string.Empty);
        }

        if (start < n)
        {
            foreach (var line in replacement)
            {
                builder.Append(line).Append(eol);
            }

            return new LineDiffTextEdit(table.Starts[start], 0, builder.ToString());
        }

        // Appending after the last line.
        foreach (var line in replacement)
        {
            builder.Append(eol).Append(line);
        }

        return new LineDiffTextEdit(table.Length, 0, builder.ToString());
    }

    private static bool Matches(string[] lines, int start, IReadOnlyList<string> expected)
    {
        for (var i = 0; i < expected.Count; i++)
        {
            if (!string.Equals(lines[start + i], expected[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct Region(int AStart, int AEnd, int BStart, int BEnd);

    private static List<Region> Merge(List<Region> regions)
    {
        var merged = new List<Region>(regions.Count);
        foreach (var region in regions)
        {
            if (merged.Count > 0 && region.AStart - merged[^1].AEnd < ContextLines)
            {
                var last = merged[^1];
                merged[^1] = new Region(last.AStart, region.AEnd, last.BStart, region.BEnd);
            }
            else
            {
                merged.Add(region);
            }
        }

        return merged;
    }

    /// <summary>Change regions (half-open ranges in both sequences) in order.</summary>
    private static List<Region> Diff(string[] a, string[] b)
    {
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && string.Equals(a[prefix], b[prefix], StringComparison.Ordinal))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix &&
               string.Equals(a[^(suffix + 1)], b[^(suffix + 1)], StringComparison.Ordinal))
        {
            suffix++;
        }

        var n = a.Length - prefix - suffix;
        var m = b.Length - prefix - suffix;
        if (n == 0 && m == 0)
        {
            return [];
        }

        // Compare interned IDs instead of strings in the inner loop.
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var x = new int[n];
        var y = new int[m];
        for (var i = 0; i < n; i++)
        {
            x[i] = Id(ids, a[prefix + i]);
        }

        for (var j = 0; j < m; j++)
        {
            y[j] = Id(ids, b[prefix + j]);
        }

        var ops = Myers(x, y);
        if (ops is null)
        {
            return [new Region(prefix, prefix + n, prefix, prefix + m)];
        }

        var regions = new List<Region>();
        int ai = 0, bi = 0, index = 0;
        while (index < ops.Count)
        {
            if (ops[index] == Op.Equal)
            {
                ai++;
                bi++;
                index++;
                continue;
            }

            int aStart = ai, bStart = bi;
            while (index < ops.Count && ops[index] != Op.Equal)
            {
                if (ops[index] == Op.Delete)
                {
                    ai++;
                }
                else
                {
                    bi++;
                }

                index++;
            }

            regions.Add(new Region(prefix + aStart, prefix + ai, prefix + bStart, prefix + bi));
        }

        return regions;
    }

    private static int Id(Dictionary<string, int> ids, string line)
    {
        if (!ids.TryGetValue(line, out var id))
        {
            id = ids.Count;
            ids.Add(line, id);
        }

        return id;
    }

    private enum Op : byte
    {
        Equal,
        Delete,
        Insert,
    }

    /// <summary>Greedy Myers with one V snapshot per D; null when D exceeds <see cref="MaximumEditDistance"/>.</summary>
    private static List<Op>? Myers(int[] a, int[] b)
    {
        int n = a.Length, m = b.Length;
        var max = Math.Min(n + m, MaximumEditDistance);
        var offset = max + 1;
        var v = new int[2 * max + 3];
        var trace = new List<int[]>();
        var found = -1;
        for (var d = 0; d <= max && found < 0; d++)
        {
            for (var k = -d; k <= d; k += 2)
            {
                var x = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]
                    : v[offset + k - 1] + 1;
                var y = x - k;
                while (x < n && y < m && a[x] == b[y])
                {
                    x++;
                    y++;
                }

                v[offset + k] = x;
                if (x >= n && y >= m)
                {
                    found = d;
                }
            }

            trace.Add(v[(offset - d)..(offset + d + 1)]);
        }

        if (found < 0)
        {
            return null;
        }

        var ops = new List<Op>(n + m);
        int cx = n, cy = m;
        for (var d = found; d > 0; d--)
        {
            var previous = trace[d - 1];
            var k = cx - cy;
            int At(int diagonal) => previous[diagonal + d - 1];
            var down = k == -d || (k != d && At(k - 1) < At(k + 1));
            var prevK = down ? k + 1 : k - 1;
            var prevX = At(prevK);
            var prevY = prevX - prevK;
            var midX = down ? prevX : prevX + 1;
            while (cx > midX)
            {
                ops.Add(Op.Equal);
                cx--;
                cy--;
            }

            ops.Add(down ? Op.Insert : Op.Delete);
            cx = prevX;
            cy = prevY;
        }

        while (cx > 0)
        {
            ops.Add(Op.Equal);
            cx--;
        }

        ops.Reverse();
        return ops;
    }
}
