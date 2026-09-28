using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.SchemaLearning;

/// <summary>
/// Repositório de aprendizado em memória para os benchmarks que <em>não</em> medem I/O: hidratação do catálogo e
/// vazão do coordenador. O custo real de disco é medido separadamente em <see cref="LearnedSchemaCommitBenchmarks"/>,
/// contra o LiteDB de verdade; misturar os dois num só número esconderia qual dos dois domina.
/// </summary>
public sealed class StubLearnedSchemaRepository : ILearnedSchemaRepository
{
    /// <summary>Snapshot devolvido por <see cref="GetAsync"/>; <see langword="null"/> significa "nunca aprendido".</summary>
    public LearnedSchemaSnapshot? Snapshot { get; set; }

    /// <summary>
    /// Latência artificial de cada commit. É o que transforma o worker único do coordenador em gargalo previsível,
    /// sem depender de disco: com ela a fila satura de forma reprodutível e a taxa de descarte é mensurável.
    /// </summary>
    public TimeSpan CommitLatency { get; set; }

    private long _applied;

    /// <summary>Commits concluídos até agora.</summary>
    public long AppliedCount => Interlocked.Read(ref _applied);

    /// <inheritdoc />
    public async Task<SchemaCommitResult> ApplyAsync(SchemaObservationDelta delta, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delta);
        // Honra o token: o StopAsync do coordenador cancela o worker, e um fake que ignorasse o cancelamento
        // faria o encerramento do benchmark medir o timeout, não a vazão.
        if (CommitLatency > TimeSpan.Zero) await Task.Delay(CommitLatency, cancellationToken).ConfigureAwait(false);
        return new SchemaCommitResult(SchemaCommitOutcome.Applied, Interlocked.Increment(ref _applied), null);
    }

    /// <inheritdoc />
    public Task<LearnedSchemaSnapshot?> GetAsync(LearnedSchemaKey key, CancellationToken cancellationToken) =>
        Task.FromResult(Snapshot);

    /// <inheritdoc />
    public Task<IReadOnlyList<LearnedSchemaKey>> ListKeysAsync(Guid profileId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LearnedSchemaKey>>([]);

    /// <inheritdoc />
    public Task<bool> WasBatchCommittedAsync(LearnedSchemaKey key, Guid batchId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    /// <inheritdoc />
    public Task RenameAsync(LearnedSchemaKey source, LearnedSchemaKey destination, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <inheritdoc />
    public Task<int> RemoveAsync(LearnedSchemaKey key, CancellationToken cancellationToken) => Task.FromResult(0);

    /// <inheritdoc />
    public Task<int> RemoveDatabaseAsync(Guid profileId, string database, CancellationToken cancellationToken) => Task.FromResult(0);

    /// <inheritdoc />
    public Task<int> RemoveProfileAsync(Guid profileId, CancellationToken cancellationToken) => Task.FromResult(0);
}
