using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// Facade called by the result-delivery producers (schema-learning.md § Fluxo e isolamento) right after a
/// <see cref="StructuredResultSet"/> is already visible to the user. It never queries MongoDB again: the only input
/// is the result set already held in memory by the caller.
/// <para>
/// <strong>Idempotency / duplicate deliveries.</strong> Each call is treated as a new <em>observation</em>, never
/// deduplicated against a previous delivery of what looks like the same query — this mirrors schema-learning.md § Fila
/// e amostragem verbatim ("Repetir find em outra execução produz novas observações, não prova documentos únicos").
/// <see cref="SchemaLearningBatchContext.ForNewBatch"/> always mints a fresh <c>BatchId</c> here; the idempotency
/// <see cref="ILearnedSchemaRepository"/> guarantees for a *retry* of the very same batch (L14) is a different,
/// narrower guarantee than deduplicating distinct deliveries, and this service deliberately does not conflate them.
/// </para>
/// </summary>
public sealed class SchemaLearningService
{
    private readonly SchemaLearningCoordinator _coordinator;

    public SchemaLearningService(SchemaLearningCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        _coordinator = coordinator;
    }

    /// <summary>
    /// Evaluates admission for one already-delivered result set and enqueues it when eligible. Synchronous,
    /// fire-and-forget safe: never throws back into the caller and never awaits anything the worker does, so it can
    /// be called from the result-delivery path without delaying or risking what the user already sees.
    /// </summary>
    /// <param name="resultSet">Result set already assigned/rendered to the tab; null is a no-op.</param>
    /// <param name="executionId">Opaque execution coordinate, carried only as batch metadata.</param>
    /// <param name="resultSetNumber">Number of the result set inside the execution.</param>
    /// <param name="pageSequence">Zero based page inside the result set.</param>
    /// <param name="policy">Policy to evaluate; defaults to <see cref="SchemaLearningPolicy.Default"/>.</param>
    /// <param name="observedGenerationId">Opaque source generation captured with the batch, or null when unknown.</param>
    public void NotifyResultDelivered(
        StructuredResultSet? resultSet,
        Guid executionId,
        int resultSetNumber,
        int pageSequence,
        SchemaLearningPolicy? policy = null,
        Guid? observedGenerationId = null)
    {
        if (resultSet is null) return;
        try
        {
            var context = SchemaLearningBatchContext.ForNewBatch(executionId, resultSetNumber, pageSequence, observedGenerationId);
            var admission = SchemaLearningAdmissionPolicy.Evaluate(resultSet, context, policy ?? SchemaLearningPolicy.Default);
            if (admission.IsAdmitted) _coordinator.TryEnqueue(admission.Envelope);
        }
        catch
        {
            // The result was already handed to the user before this call happens; a malformed result set or an
            // admission failure must never surface on the delivery path. Learning is never part of query success.
        }
    }
}
