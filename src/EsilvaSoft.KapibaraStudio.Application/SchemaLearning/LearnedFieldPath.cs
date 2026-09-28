using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text;

namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// A field path kept as ordered segments, never as an ambiguous dotted string. The nested path
/// <c>Customer</c> › <c>Id</c> and the literal field named <c>"Customer.Id"</c> are different paths and
/// produce different identifiers, by the same length-prefixed rule used by <see cref="LearnedSchemaKey"/>.
/// </summary>
public sealed class LearnedFieldPath : IEquatable<LearnedFieldPath>
{
    /// <summary>Version of the path encoding; changes only when the segment encoding rule changes.</summary>
    public const byte EncodingVersion = 0x01;

    private readonly string[] _segments;

    /// <summary>Builds a path from its segments. Segments are copied; an empty path is rejected.</summary>
    public LearnedFieldPath(IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count == 0) throw new ArgumentException("Um caminho de campo precisa de ao menos um segmento.", nameof(segments));
        _segments = new string[segments.Count];
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment is null) throw new ArgumentException("Segmento de caminho nulo.", nameof(segments));
            _segments[index] = segment;
        }
        Segments = new ReadOnlyCollection<string>(_segments);
    }

    /// <summary>Ordered segments of the path.</summary>
    public IReadOnlyList<string> Segments { get; }

    /// <summary>Number of segments; depth 1 is a top level field.</summary>
    public int Depth => _segments.Length;

    /// <summary>Last segment, the name the user sees in the completion list.</summary>
    public string Name => _segments[^1];

    /// <summary>Parent path, or null for a top level field.</summary>
    public LearnedFieldPath? Parent => _segments.Length == 1 ? null : new LearnedFieldPath(_segments[..^1]);

    /// <summary>Appends a segment, returning a new path.</summary>
    public LearnedFieldPath Append(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var segments = new string[_segments.Length + 1];
        _segments.CopyTo(segments, 0);
        segments[^1] = segment;
        return new LearnedFieldPath(segments);
    }

    /// <summary>
    /// Canonical identifier of the path, big-endian throughout:
    /// <code>0x01 ‖ count32be ‖ (len32be(utf8(segment)) ‖ utf8(segment))*</code>
    /// </summary>
    public byte[] ToCanonicalId()
    {
        var size = 1 + 4;
        var lengths = new int[_segments.Length];
        for (var index = 0; index < _segments.Length; index++)
        {
            lengths[index] = Encoding.UTF8.GetByteCount(_segments[index]);
            size += 4 + lengths[index];
        }
        var buffer = new byte[size];
        buffer[0] = EncodingVersion;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(1, 4), (uint)_segments.Length);
        var offset = 5;
        for (var index = 0; index < _segments.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), (uint)lengths[index]);
            offset += 4;
            Encoding.UTF8.GetBytes(_segments[index], buffer.AsSpan(offset, lengths[index]));
            offset += lengths[index];
        }
        return buffer;
    }

    /// <summary>Ordinal, segment by segment.</summary>
    public bool Equals(LearnedFieldPath? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_segments.Length != other._segments.Length) return false;
        for (var index = 0; index < _segments.Length; index++)
            if (!string.Equals(_segments[index], other._segments[index], StringComparison.Ordinal)) return false;
        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LearnedFieldPath);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var segment in _segments) hash.Add(segment, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <summary>Display only; never used as identity because the dot is ambiguous.</summary>
    public override string ToString() => string.Join('.', _segments);
}
