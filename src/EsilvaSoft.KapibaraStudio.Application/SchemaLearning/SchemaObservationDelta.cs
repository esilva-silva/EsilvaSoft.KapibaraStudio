using System.Collections.ObjectModel;

namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// The probabilistic increment produced by the analyzer for one batch and applied by the repository in L14.
/// It contains observed fields and increments, dates and coverage — never the documents, the query or any value.
/// Repeating a <c>find</c> produces new <em>observations</em>; it never proves unique documents.
/// </summary>
public sealed class SchemaObservationDelta
{
    /// <summary>Builds a delta for one batch of one namespace.</summary>
    public SchemaObservationDelta(
        LearnedSchemaKey key,
        SchemaLearningBatchContext context,
        long completeDocumentObservations,
        long skippedDocuments,
        bool isTruncated,
        IReadOnlyList<SchemaFieldObservationDelta> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentOutOfRangeException.ThrowIfNegative(completeDocumentObservations);
        ArgumentOutOfRangeException.ThrowIfNegative(skippedDocuments);
        if (!key.IsComplete) throw new ArgumentException("Chave de schema aprendido incompleta.", nameof(key));
        Key = key;
        Context = context;
        CompleteDocumentObservations = completeDocumentObservations;
        SkippedDocuments = skippedDocuments;
        IsTruncated = isTruncated;
        var copy = new SchemaFieldObservationDelta[fields.Count];
        for (var index = 0; index < fields.Count; index++)
            copy[index] = fields[index] ?? throw new ArgumentException("Delta de campo nulo.", nameof(fields));
        Fields = new ReadOnlyCollection<SchemaFieldObservationDelta>(copy);
    }

    /// <summary>Namespace the increment belongs to.</summary>
    public LearnedSchemaKey Key { get; }

    /// <summary>Batch coordinates; <see cref="SchemaLearningBatchContext.BatchId"/> makes the commit idempotent.</summary>
    public SchemaLearningBatchContext Context { get; }

    /// <summary>Batch identifier recorded together with the increment, in the same transaction.</summary>
    public Guid BatchId => Context.BatchId;

    /// <summary>Eligible complete document observations analysed in this batch; denominator of field frequency.</summary>
    public long CompleteDocumentObservations { get; }

    /// <summary>Documents that could not be analysed; counted as skipped, never as an absent field.</summary>
    public long SkippedDocuments { get; }

    /// <summary>Whether structural walking hit a budget, so absence in this batch is not valid evidence.</summary>
    public bool IsTruncated { get; }

    /// <summary>Observed field increments; only the paths actually touched by this batch.</summary>
    public IReadOnlyList<SchemaFieldObservationDelta> Fields { get; }

    /// <summary>First observation instant of the batch, UTC; <see cref="DateTimeOffset.MinValue"/> when empty.</summary>
    public DateTimeOffset FirstSeenUtc => Fields.Count == 0 ? Context.CapturedAtUtc.ToUniversalTime() : Fields.Min(entry => entry.FirstSeenUtc);

    /// <summary>Last observation instant of the batch, UTC.</summary>
    public DateTimeOffset LastSeenUtc => Fields.Count == 0 ? Context.CapturedAtUtc.ToUniversalTime() : Fields.Max(entry => entry.LastSeenUtc);
}
