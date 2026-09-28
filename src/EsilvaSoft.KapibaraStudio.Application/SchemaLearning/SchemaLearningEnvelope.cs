using System.Collections.ObjectModel;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// What crosses the queue from the producer to the background analyzer. It carries the already materialised
/// JSON strings and nothing else from the result set.
/// <para>
/// It deliberately does <strong>not</strong> carry <c>StructuredResultDocument</c>: that type exposes
/// <c>Set</c>, which points back at <c>StructuredResultSet.Documents</c> and would keep the whole result set
/// — every page the user is looking at — alive for as long as the envelope sits in the queue.
/// </para>
/// </summary>
public sealed class SchemaLearningEnvelope
{
    private SchemaLearningEnvelope(
        LearnedSchemaKey key,
        SchemaLearningBatchContext context,
        SchemaLearningPolicy policy,
        string method,
        ResultCompleteness completeness,
        bool isTruncated,
        int skippedDocuments,
        string[] documents)
    {
        Key = key;
        Context = context;
        Policy = policy;
        Method = method;
        Completeness = completeness;
        IsTruncated = isTruncated;
        SkippedDocuments = skippedDocuments;
        Documents = new ReadOnlyCollection<string>(documents);
    }

    /// <summary>Namespace the observations belong to.</summary>
    public LearnedSchemaKey Key { get; }

    /// <summary>Batch and execution coordinates captured before any await.</summary>
    public SchemaLearningBatchContext Context { get; }

    /// <summary>Policy captured with the execution; rechecked again before the commit.</summary>
    public SchemaLearningPolicy Policy { get; }

    /// <summary>Batch identifier, idempotent across retries.</summary>
    public Guid BatchId => Context.BatchId;

    /// <summary>Query method that produced the documents; always <c>find</c> or <c>findOne</c> here.</summary>
    public string Method { get; }

    /// <summary>Completeness of the admitted result; always <see cref="ResultCompleteness.Complete"/> here.</summary>
    public ResultCompleteness Completeness { get; }

    /// <summary>Whether the page itself was truncated by the runtime limits.</summary>
    public bool IsTruncated { get; }

    /// <summary>Documents dropped by the producer (oversized, unreadable); never counted as an absent field.</summary>
    public int SkippedDocuments { get; }

    /// <summary>Extended JSON of the sampled documents, already materialised and detached from the result set.</summary>
    public IReadOnlyList<string> Documents { get; }

    /// <summary>
    /// Builds an envelope from JSON strings already selected by the producer. The strings are copied into an
    /// independent array so no result set, page or view model stays reachable from the queue.
    /// </summary>
    public static SchemaLearningEnvelope Create(
        LearnedSchemaKey key,
        SchemaLearningBatchContext context,
        SchemaLearningPolicy policy,
        string method,
        IReadOnlyList<string> documents,
        bool isTruncated = false,
        int skippedDocuments = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentOutOfRangeException.ThrowIfNegative(skippedDocuments);
        if (!key.IsComplete) throw new ArgumentException("Chave de schema aprendido incompleta.", nameof(key));
        var copy = new string[documents.Count];
        for (var index = 0; index < documents.Count; index++)
            copy[index] = documents[index] ?? throw new ArgumentException("Documento JSON nulo no envelope.", nameof(documents));
        return new SchemaLearningEnvelope(key, context, policy, method, ResultCompleteness.Complete, isTruncated, skippedDocuments, copy);
    }
}
