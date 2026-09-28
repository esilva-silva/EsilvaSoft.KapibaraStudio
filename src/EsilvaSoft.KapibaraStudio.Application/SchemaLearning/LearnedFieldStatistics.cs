using System.Collections.ObjectModel;

namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// Accumulated statistics of one field path. <see cref="Frequency"/> is a frequency among the observations of
/// this namespace, never an estimate of the collection: the sample is biased by the query, sort, limit and permissions.
/// </summary>
public sealed class LearnedFieldStatistics
{
    /// <summary>Builds accumulated statistics for one path.</summary>
    public LearnedFieldStatistics(
        LearnedFieldPath path,
        long presentDocumentObservations,
        long eligibleDocumentObservations,
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
        ArgumentOutOfRangeException.ThrowIfNegative(eligibleDocumentObservations);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayDocumentObservations);
        Path = path;
        PresentDocumentObservations = presentDocumentObservations;
        EligibleDocumentObservations = eligibleDocumentObservations;
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

    /// <summary>Observations where the path was present.</summary>
    public long PresentDocumentObservations { get; }

    /// <summary>Observations where the path could have been present; the denominator of <see cref="Frequency"/>.</summary>
    public long EligibleDocumentObservations { get; }

    /// <summary>Presence frequency among the observations of this path, or null without a denominator.</summary>
    public double? Frequency => EligibleDocumentObservations == 0 ? null : (double)PresentDocumentObservations / EligibleDocumentObservations;

    /// <summary>BSON type name to observation count; the type distribution has its own denominator.</summary>
    public IReadOnlyDictionary<string, long> TypeObservations { get; }

    /// <summary>First time the path was observed, UTC.</summary>
    public DateTimeOffset FirstSeenUtc { get; }

    /// <summary>Last time the path was observed, UTC.</summary>
    public DateTimeOffset LastSeenUtc { get; }

    /// <summary>Whether the path was ever observed carrying an array.</summary>
    public bool IsArray { get; }

    /// <summary>Element type distribution of the array values.</summary>
    public IReadOnlyDictionary<string, long> ArrayElementTypeObservations { get; }

    /// <summary>Observations where the path carried an array.</summary>
    public long ArrayDocumentObservations { get; }

    /// <summary>Whether array walking was ever truncated for this path.</summary>
    public bool ArrayTruncated { get; }

    private static ReadOnlyDictionary<string, long> Freeze(IReadOnlyDictionary<string, long>? source)
    {
        var copy = new Dictionary<string, long>(StringComparer.Ordinal);
        if (source is not null)
            foreach (var pair in source) copy[pair.Key] = pair.Value;
        return new ReadOnlyDictionary<string, long>(copy);
    }
}
