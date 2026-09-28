namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

/// <summary>
/// <see cref="ITextSnapshot"/> over an immutable string, for tests, benchmarks and validation. Edits return a new
/// version of the same document; only the change records (never old texts) are retained to answer
/// <see cref="GetChangesSince"/>. Branches from the same version are different lineages and report unknown changes.
/// </summary>
public sealed class StringTextSnapshot : ITextSnapshot
{
    private readonly History _history;

    /// <summary>First version (sequence 0) of a new document.</summary>
    public StringTextSnapshot(string text) : this(text, new TextSnapshotVersion(TextSnapshotVersion.NewDocumentId(), 0)) { }

    /// <summary>Snapshot with an explicit version (fixtures that mirror an editor); earlier history is unknown.</summary>
    public StringTextSnapshot(string text, TextSnapshotVersion version) : this(text, version, new History(version.Sequence, null, null)) { }

    private StringTextSnapshot(string text, TextSnapshotVersion version, History history)
    {
        ArgumentNullException.ThrowIfNull(text);
        Text = text; Version = version; _history = history;
    }

    public string Text { get; }
    public TextSnapshotVersion Version { get; }
    public int Length => Text.Length;
    public char this[int index] => Text[index];

    public string GetText(int start, int length) => Text.Substring(start, length);
    public string GetText(TextSpan span) => Text.Substring(span.Start, span.Length);
    public void CopyTo(int start, Span<char> destination) => Text.AsSpan(start, destination.Length).CopyTo(destination);
    public ReadOnlySpan<char> AsSpan() => Text;

    public StringTextSnapshot Replace(int start, int oldLength, string newText)
    {
        ArgumentNullException.ThrowIfNull(newText);
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(oldLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)start + oldLength, Text.Length, nameof(oldLength));
        var version = Version.Next();
        var text = string.Concat(Text.AsSpan(0, start), newText, Text.AsSpan(start + oldLength));
        return new(text, version, new History(version.Sequence, new TextChange(start, oldLength, newText.Length), _history));
    }

    public StringTextSnapshot Insert(int start, string text) => Replace(start, 0, text);
    public StringTextSnapshot Remove(int start, int length) => Replace(start, length, "");

    /// <summary>New version whose change is not described (e.g. whole document reloaded); changes across it are unknown.</summary>
    public StringTextSnapshot WithText(string text)
    {
        var version = Version.Next();
        return new(text, version, new History(version.Sequence, null, _history));
    }

    public IReadOnlyList<TextChange>? GetChangesSince(ITextSnapshot previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (previous is not StringTextSnapshot other || !Version.IsSameDocument(other.Version) || other.Version.Sequence > Version.Sequence) return null;
        var changes = new List<TextChange>();
        var node = _history;
        while (node is not null && node.Sequence > other.Version.Sequence)
        {
            if (node.Change is not { } change) return null;
            changes.Add(change);
            node = node.Previous;
        }
        // Same sequence is not enough: an edit applied to an older snapshot starts a different lineage.
        if (!ReferenceEquals(node, other._history)) return null;
        changes.Reverse();
        return changes;
    }

    private sealed record History(long Sequence, TextChange? Change, History? Previous);
}
