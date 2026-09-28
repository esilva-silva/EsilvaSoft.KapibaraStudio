using System.Threading.Channels;

namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// Owns the bounded queue between the result-delivery path and <see cref="BackgroundSchemaAnalyzer"/> /
/// <see cref="ILearnedSchemaRepository"/> (schema-learning.md § Fila e amostragem). <see cref="TryEnqueue"/> is the
/// only member the producer calls; it is synchronous and never throws, even when the queue is full or the single
/// worker is stuck on a slow or blocked repository.
/// <para>
/// <strong>Coalescing simplification.</strong> The specification asks that two pending envelopes of the same
/// <see cref="LearnedSchemaKey"/> never interleave with another key's envelope. This implementation has exactly one
/// worker consuming the channel strictly in FIFO order, so no two envelopes — same key or not — are ever processed
/// concurrently; the "coalescing by key" requirement is satisfied trivially by the absence of any parallelism, not
/// by an explicit per-key grouping step. Parallelising distinct keys is out of scope for this lote and would need
/// its own per-key sequencing before it could be introduced safely.
/// </para>
/// </summary>
public sealed class SchemaLearningCoordinator : IAsyncDisposable
{
    /// <summary>Batches kept in flight before a new one is dropped (schema-learning.md § Fila e amostragem).</summary>
    public const int DefaultCapacity = 32;

    private static readonly TimeSpan DefaultDrainTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DefaultForceTimeout = TimeSpan.FromSeconds(1);

    private readonly Channel<SchemaLearningEnvelope> _channel;
    private readonly BackgroundSchemaAnalyzer _analyzer;
    private readonly ILearnedSchemaRepository _repository;
    private readonly CancellationTokenSource _workerCancellation = new();
    private readonly Task _workerTask;
    private long _droppedCount;
    private long _processedCount;
    private long _failedCount;
    private long _notPersistedCount;
    private bool _stopped;

    public SchemaLearningCoordinator(BackgroundSchemaAnalyzer analyzer, ILearnedSchemaRepository repository, int capacity = DefaultCapacity, Action<LearnedSchemaKey>? committed = null)
    {
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _analyzer = analyzer;
        _repository = repository;
        Committed = committed;
        _channel = Channel.CreateBounded<SchemaLearningEnvelope>(new BoundedChannelOptions(capacity)
        {
            // Deliberately BoundedChannelFullMode.Wait, not DropWrite: with DropWrite the channel silently
            // discards the new item and TryWrite/WriteAsync still report success, so a caller could never learn a
            // batch was dropped and DroppedCount could never be accurate. Wait keeps TryWrite's contract exactly
            // right for this coordinator instead: it is never awaited (only TryWrite is ever called, never
            // WriteAsync), so it already never blocks; the full queue still drops the new batch, never the one
            // already in flight (schema-learning.md), but now TryWrite's own return value tells us so.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
        _workerTask = Task.Run(() => RunWorkerAsync(_workerCancellation.Token));
    }

    /// <summary>Batches accepted by the analyzer/repository so far.</summary>
    public long ProcessedCount => Interlocked.Read(ref _processedCount);

    /// <summary>Batches whose analysis or commit raised; the worker kept consuming the next one regardless.</summary>
    public long FailedCount => Interlocked.Read(ref _failedCount);

    public long NotPersistedCount => Interlocked.Read(ref _notPersistedCount);

    public Action<LearnedSchemaKey>? Committed { get; }

    /// <summary>
    /// Batches discarded because the queue was full, or because the queue was already draining for shutdown.
    /// Never includes batches rejected by admission — those never reach the coordinator at all.
    /// </summary>
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    /// <summary>
    /// Enqueues one envelope. Synchronous, never throws, never blocks and never awaits queue space: a full queue
    /// or a defunct channel only increments <see cref="DroppedCount"/> and returns <see langword="false"/>. The
    /// result already delivered to the user is never affected by this call succeeding or failing.
    /// </summary>
    public bool TryEnqueue(SchemaLearningEnvelope? envelope)
    {
        if (envelope is null) return false;
        try
        {
            if (_channel.Writer.TryWrite(envelope)) return true;
        }
        catch
        {
            // TryWrite is not documented to throw, but the contract of this method is "never throws";
            // a defensive catch keeps that true even against a future channel implementation detail.
        }
        Interlocked.Increment(ref _droppedCount);
        return false;
    }

    private async Task RunWorkerAsync(CancellationToken forcedStop)
    {
        var reader = _channel.Reader;
        while (true)
        {
            SchemaLearningEnvelope envelope;
            try
            {
                if (!await reader.WaitToReadAsync(forcedStop).ConfigureAwait(false)) return;
                if (!reader.TryRead(out envelope!)) continue;
            }
            catch (OperationCanceledException) { return; }
            catch (ChannelClosedException) { return; }

            try
            {
                // Pure, synchronous transformation (BackgroundSchemaAnalyzer's own contract), then the one
                // short transaction owned by the repository. Never runs concurrently with another envelope.
                var delta = _analyzer.Analyze(envelope);
                if (!envelope.Policy.PersistenceEnabled)
                {
                    Interlocked.Increment(ref _notPersistedCount);
                    Interlocked.Increment(ref _processedCount);
                    continue;
                }

                var result = await _repository.ApplyAsync(delta, forcedStop).ConfigureAwait(false);
                if (!result.IsPersisted)
                {
                    Interlocked.Increment(ref _failedCount);
                    continue;
                }

                Interlocked.Increment(ref _processedCount);
                try { Committed?.Invoke(delta.Key); }
                catch { /* cache invalidation must never stop the learning worker */ }
            }
            catch (OperationCanceledException) when (forcedStop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception)
            {
                // Learning is never part of the success of the query (schema-learning.md): one envelope's
                // failure is counted and the worker keeps consuming the next one, never crashing the loop.
                Interlocked.Increment(ref _failedCount);
            }
        }
    }

    /// <summary>
    /// Stops accepting new work and drains what is already queued, bounded so shutdown is never blocked by a
    /// stuck repository. First waits <paramref name="drainTimeout"/> for a graceful finish; if the worker is still
    /// running past that, cancellation is requested and a second, shorter <paramref name="forceTimeout"/> window is
    /// given before <see cref="StopAsync"/> returns regardless of whether the worker actually stopped.
    /// </summary>
    public async Task StopAsync(TimeSpan? drainTimeout = null, TimeSpan? forceTimeout = null)
    {
        if (_stopped) { ObserveOutcome(_workerTask); return; }
        _stopped = true;
        _channel.Writer.TryComplete();

        var graceful = await Task.WhenAny(_workerTask, Task.Delay(drainTimeout ?? DefaultDrainTimeout)).ConfigureAwait(false);
        if (!ReferenceEquals(graceful, _workerTask))
        {
            _workerCancellation.Cancel();
            await Task.WhenAny(_workerTask, Task.Delay(forceTimeout ?? DefaultForceTimeout)).ConfigureAwait(false);
        }

        // Never await an unfinished task here: a repository/analyzer that ignores cancellation entirely must
        // not be able to hang application shutdown. Its eventual fault, if any, is still observed below.
        ObserveOutcome(_workerTask);
    }

    private static void ObserveOutcome(Task task)
    {
        if (task.IsCompleted) { _ = task.Exception; return; }
        task.ContinueWith(static t => { _ = t.Exception; }, TaskContinuationOptions.ExecuteSynchronously);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        if (_workerTask.IsCompleted) _workerCancellation.Dispose();
    }
}
