namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

/// <summary>
/// Immutable text of one document version, safe to read from worker threads. Offsets are UTF-16 code units, identical
/// to AvaloniaEdit. Implementations must not copy the document to create a snapshot (Desktop: TextDocument.CreateSnapshot()).
/// </summary>
public interface ITextSnapshot
{
    /// <summary>Monotonic within the document; versions of different documents are not comparable.</summary>
    TextSnapshotVersion Version { get; }

    int Length { get; }

    char this[int index] { get; }

    string GetText(int start, int length);

    /// <summary>
    /// Ordered changes that turn <paramref name="previous"/> into this snapshot (each one in the coordinates left by the
    /// previous change); empty for the same version; <see langword="null"/> when unknown: another document, a newer or
    /// unrelated snapshot, or history that is no longer available. Callers must then treat the whole text as changed.
    /// </summary>
    IReadOnlyList<TextChange>? GetChangesSince(ITextSnapshot previous);

    string GetText(TextSpan span) => GetText(span.Start, span.Length);

    /// <summary>Copies a range without allocating a string (e.g. into a pooled buffer for the lexer).</summary>
    void CopyTo(int start, Span<char> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)start + destination.Length, Length, nameof(destination));
        for (var index = 0; index < destination.Length; index++) destination[index] = this[start + index];
    }
}

/// <summary>Identity of a document plus a monotonic sequence inside it. Equality means the same text version of the same document.</summary>
public readonly record struct TextSnapshotVersion(long DocumentId, long Sequence)
{
    private static long _lastDocumentId;

    /// <summary>Process-unique identifier for a new document (editor, tab or test document).</summary>
    public static long NewDocumentId() => Interlocked.Increment(ref _lastDocumentId);

    public bool IsSameDocument(TextSnapshotVersion other) => DocumentId == other.DocumentId;

    /// <summary>True only for a later version of the same document; never compares different documents.</summary>
    public bool IsNewerThan(TextSnapshotVersion other) => IsSameDocument(other) && Sequence > other.Sequence;

    public TextSnapshotVersion Next() => this with { Sequence = checked(Sequence + 1) };
}

/// <summary>Replacement of <see cref="OldLength"/> code units at <see cref="Start"/> by <see cref="NewLength"/> code units (AvaloniaEdit Offset/RemovalLength/InsertionLength).</summary>
public readonly record struct TextChange(int Start, int OldLength, int NewLength)
{
    public int Start { get; } = Start >= 0 ? Start : throw new ArgumentOutOfRangeException(nameof(Start), Start, "O início não pode ser negativo.");
    public int OldLength { get; } = OldLength >= 0 ? OldLength : throw new ArgumentOutOfRangeException(nameof(OldLength), OldLength, "O tamanho removido não pode ser negativo.");
    public int NewLength { get; } = NewLength >= 0 ? NewLength : throw new ArgumentOutOfRangeException(nameof(NewLength), NewLength, "O tamanho inserido não pode ser negativo.");

    public TextSpan OldSpan => new(Start, OldLength);
    public TextSpan NewSpan => new(Start, NewLength);
    public int Delta => NewLength - OldLength;
}
