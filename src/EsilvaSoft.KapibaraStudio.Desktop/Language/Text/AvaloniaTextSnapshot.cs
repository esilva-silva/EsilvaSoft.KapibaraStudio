using System.Runtime.CompilerServices;
using AvaloniaEdit.Document;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Desktop.Language.Text;

/// <summary>
/// Immutable <see cref="ITextSnapshot"/> backed by AvaloniaEdit's immutable <see cref="ITextSource"/>.
/// Constructing this type captures <see cref="TextDocument.CreateSnapshot()"/>; it does not materialize
/// <see cref="TextDocument.Text"/> or otherwise copy the document.
/// </summary>
public sealed class AvaloniaTextSnapshot : ITextSnapshot
{
    private static readonly ConditionalWeakTable<TextDocument, DocumentState> DocumentStates = new();

    private readonly ITextSource _source;
    private readonly ITextSourceVersion _sourceVersion;
    private readonly DocumentState _documentState;

    /// <summary>Captures the document's current immutable AvaloniaEdit snapshot.</summary>
    public AvaloniaTextSnapshot(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        _source = document.CreateSnapshot();
        _sourceVersion = _source.Version;
        _documentState = DocumentStates.GetValue(document, static _ => new DocumentState());
        Version = new TextSnapshotVersion(_documentState.DocumentId, _documentState.GetSequence(_sourceVersion));
    }

    public TextSnapshotVersion Version { get; }
    public int Length => _source.TextLength;
    public char this[int index] => _source.GetCharAt(index);

    public string GetText(int start, int length) => _source.GetText(start, length);

    public void CopyTo(int start, Span<char> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThan((long)start + destination.Length, Length, nameof(destination));
        _source.GetTextAsMemory(start, destination.Length).Span.CopyTo(destination);
    }

    /// <summary>
    /// Uses the version lineage retained by AvaloniaEdit. A snapshot from another document or adapter implementation,
    /// and a newer predecessor, have no usable history for this contract.
    /// </summary>
    public IReadOnlyList<TextChange>? GetChangesSince(ITextSnapshot previous)
    {
        ArgumentNullException.ThrowIfNull(previous);
        if (previous is not AvaloniaTextSnapshot other || !ReferenceEquals(_documentState, other._documentState)) return null;

        var age = other._sourceVersion.CompareAge(_sourceVersion);
        if (age > 0) return null;
        if (age == 0) return Array.Empty<TextChange>();

        try
        {
            return other._sourceVersion.GetChangesTo(_sourceVersion)
                .Select(static change => new TextChange(change.Offset, change.RemovalLength, change.InsertionLength))
                .ToArray();
        }
        catch (ArgumentException)
        {
            // AvaloniaEdit reports unavailable/unrelated lineage as an argument error. The interface represents it as null.
            return null;
        }
    }

    private sealed class DocumentState
    {
        private readonly object _gate = new();
        private readonly Dictionary<ITextSourceVersion, long> _sequences = new(ReferenceEqualityComparer.Instance);
        private long _lastSequence = -1;

        public long DocumentId { get; } = TextSnapshotVersion.NewDocumentId();

        public long GetSequence(ITextSourceVersion version)
        {
            lock (_gate)
            {
                if (_sequences.TryGetValue(version, out var sequence)) return sequence;

                foreach (var (knownVersion, knownSequence) in _sequences)
                {
                    if (knownVersion.CompareAge(version) == 0)
                    {
                        _sequences.Add(version, knownSequence);
                        return knownSequence;
                    }
                }

                sequence = checked(_lastSequence + 1);
                _lastSequence = sequence;
                _sequences.Add(version, sequence);
                return sequence;
            }
        }
    }
}
