using System.Collections.ObjectModel;

namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// Increment observed for one field path in one batch. Structure and counters only: no value, literal,
/// document <c>_id</c>, document hash or learned enum ever reaches this type.
/// Presence is counted at most once per document per path, so the counters are observations, not unique documents.
/// </summary>
public sealed class SchemaFieldObservationDelta
{
    /// <summary>Builds a field delta. Counter dictionaries are copied with ordinal comparison.</summary>
    public SchemaFieldObservationDelta(
        LearnedFieldPath path,
        long presentDocumentObservations,
        IReadOnlyDictionary<string, long> typeObservations,
        DateTimeOffset firstSeenUtc,
        DateTimeOffset lastSeenUtc,
        bool isArray = false,
        IReadOnlyDictionary<string, long>? arrayElementTypeObservations = null,
        long arrayDocumentObservations = 0,
        bool arrayTruncated = false)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(typeObservations);
        ArgumentOutOfRangeException.ThrowIfNegative(presentDocumentObservations);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayDocumentObservations);
        Path = path;
        PresentDocumentObservations = presentDocumentObservations;
        TypeObservations = Freeze(typeObservations);
        FirstSeenUtc = firstSeenUtc.ToUniversalTime();
        LastSeenUtc = lastSeenUtc.ToUniversalTime();
        IsArray = isArray;
        ArrayElementTypeObservations = Freeze(arrayElementTypeObservations);
        ArrayDocumentObservations = arrayDocumentObservations;
        ArrayTruncated = arrayTruncated;
    }

    /// <summary>Segmented path of the field.</summary>
    public LearnedFieldPath Path { get; }

    /// <summary>Canonical identifier of <see cref="Path"/>, used as the persisted field id.</summary>
    public byte[] FieldId() => Path.ToCanonicalId();

    /// <summary>Documents in this batch where the path was present; at most one per document.</summary>
    public long PresentDocumentObservations { get; }

    /// <summary>BSON type name to observation count, with its own denominator.</summary>
    public IReadOnlyDictionary<string, long> TypeObservations { get; }

    /// <summary>Earliest observation instant in this batch, UTC.</summary>
    public DateTimeOffset FirstSeenUtc { get; }

    /// <summary>Latest observation instant in this batch, UTC.</summary>
    public DateTimeOffset LastSeenUtc { get; }

    /// <summary>Whether the path was seen carrying an array value.</summary>
    public bool IsArray { get; }

    /// <summary>Element type distribution, a statistic distinct from presence and with its own denominator.</summary>
    public IReadOnlyDictionary<string, long> ArrayElementTypeObservations { get; }

    /// <summary>Documents where the path carried an array; denominator of the element distribution.</summary>
    public long ArrayDocumentObservations { get; }

    /// <summary>Whether array walking hit the element budget, so absence of an element type is not evidence.</summary>
    public bool ArrayTruncated { get; }

    private static ReadOnlyDictionary<string, long> Freeze(IReadOnlyDictionary<string, long>? source)
    {
        var copy = new Dictionary<string, long>(StringComparer.Ordinal);
        if (source is not null)
            foreach (var pair in source) copy[pair.Key] = pair.Value;
        return new ReadOnlyDictionary<string, long>(copy);
    }
}
