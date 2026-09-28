using System.Collections.ObjectModel;

namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// Hydrated state of one learned namespace. Coverage is always observational: <c>Complete</c> of a read never
/// means the collection was fully seen. Trust is derived at hydration from
/// <see cref="LastObservedGenerationId"/> and the session (DEC-L-TRUST); it is never a persisted flag.
/// </summary>
public sealed class LearnedSchemaSnapshot
{
    /// <summary>Current format version of the learned content; the identity never carries a version.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Builds a snapshot.</summary>
    public LearnedSchemaSnapshot(
        LearnedSchemaKey key,
        int formatVersion,
        long revision,
        Guid? lastObservedGenerationId,
        DateTimeOffset firstLearnedUtc,
        DateTimeOffset lastObservedUtc,
        long completeDocumentObservations,
        long sampledBatches,
        long skippedDocuments,
        bool isTruncated,
        IReadOnlyList<LearnedFieldStatistics> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        if (!key.IsComplete) throw new ArgumentException("Chave de schema aprendido incompleta.", nameof(key));
        Key = key;
        FormatVersion = formatVersion;
        Revision = revision;
        LastObservedGenerationId = lastObservedGenerationId;
        FirstLearnedUtc = firstLearnedUtc.ToUniversalTime();
        LastObservedUtc = lastObservedUtc.ToUniversalTime();
        CompleteDocumentObservations = completeDocumentObservations;
        SampledBatches = sampledBatches;
        SkippedDocuments = skippedDocuments;
        IsTruncated = isTruncated;
        var copy = new LearnedFieldStatistics[fields.Count];
        for (var index = 0; index < fields.Count; index++)
            copy[index] = fields[index] ?? throw new ArgumentException("Estatística de campo nula.", nameof(fields));
        Fields = new ReadOnlyCollection<LearnedFieldStatistics>(copy);
    }

    /// <summary>Namespace identity.</summary>
    public LearnedSchemaKey Key { get; }

    /// <summary>Format version of the stored content; a future version is isolated as unavailable.</summary>
    public int FormatVersion { get; }

    /// <summary>Revision published atomically after each commit; invalidates context of this namespace only.</summary>
    public long Revision { get; }

    /// <summary>Opaque source generation that produced the observations, or null when unknown.</summary>
    public Guid? LastObservedGenerationId { get; }

    /// <summary>When this namespace was first learned, UTC.</summary>
    public DateTimeOffset FirstLearnedUtc { get; }

    /// <summary>Last observation instant, UTC; drives staleness and retention.</summary>
    public DateTimeOffset LastObservedUtc { get; }

    /// <summary>Total eligible complete document observations; the scaling denominator.</summary>
    public long CompleteDocumentObservations { get; }

    /// <summary>Number of analysed batches.</summary>
    public long SampledBatches { get; }

    /// <summary>Documents skipped by the analyzer; never counted as absent fields.</summary>
    public long SkippedDocuments { get; }

    /// <summary>Whether any batch hit a structural budget.</summary>
    public bool IsTruncated { get; }

    /// <summary>Accumulated field statistics.</summary>
    public IReadOnlyList<LearnedFieldStatistics> Fields { get; }
}
