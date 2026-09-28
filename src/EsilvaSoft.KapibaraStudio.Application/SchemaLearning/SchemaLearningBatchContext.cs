namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// Scalar execution coordinates captured by the producer before any await, travelling with the envelope.
/// <see cref="BatchId"/> is what makes the commit idempotent in L14: a retry reuses it and a delta already
/// committed under it is not counted twice.
/// </summary>
/// <param name="BatchId">Stable identifier of this learning batch; reused by retries.</param>
/// <param name="ExecutionId">Execution that produced the result set.</param>
/// <param name="ResultSetNumber">Number of the result set inside the execution.</param>
/// <param name="PageSequence">Zero based page inside the result set.</param>
/// <param name="ObservedGenerationId">
/// Opaque source generation of the profile at capture time, or null while the generation is unknown.
/// It is a non-key column (DEC-L-TRUST): the trust state is derived at hydration, never persisted as a flag.
/// </param>
/// <param name="CapturedAtUtc">Capture instant, in UTC.</param>
public readonly record struct SchemaLearningBatchContext(
    Guid BatchId,
    Guid ExecutionId,
    int ResultSetNumber,
    int PageSequence,
    Guid? ObservedGenerationId,
    DateTimeOffset CapturedAtUtc)
{
    /// <summary>Context for a first, non retried batch of the current instant.</summary>
    public static SchemaLearningBatchContext ForNewBatch(Guid executionId, int resultSetNumber, int pageSequence, Guid? observedGenerationId = null)
        => new(Guid.NewGuid(), executionId, resultSetNumber, pageSequence, observedGenerationId, DateTimeOffset.UtcNow);
}
