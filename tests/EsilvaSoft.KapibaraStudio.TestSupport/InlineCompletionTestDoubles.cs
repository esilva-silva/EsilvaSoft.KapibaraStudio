using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

namespace EsilvaSoft.KapibaraStudio.Testing;

/// <summary>
/// Relógio manual com temporizadores próprios. O coordenador usa <see cref="TimeProvider"/> justamente para que o
/// atraso seja avançado pelo teste, sem espera real e sem tolerância de tempo de parede.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset? start = null) : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _now = start ?? new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
    public override long GetTimestamp() { lock (_gate) return _now.UtcTicks; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ManualTimer timer;
        lock (_gate)
        {
            timer = new ManualTimer(this, callback, state);
            _timers.Add(timer);
        }
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Avança o relógio e dispara, fora do bloqueio, tudo o que venceu.</summary>
    public void Advance(TimeSpan delta)
    {
        ManualTimer[] due;
        lock (_gate)
        {
            _now += delta;
            due = _timers.Where(timer => timer.IsDue(_now)).ToArray();
        }
        foreach (var timer in due) timer.Fire();
    }

    public DateTimeOffset Now => GetUtcNow();
    internal void Remove(ManualTimer timer) { lock (_gate) _timers.Remove(timer); }

    internal sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private readonly object _gate = new();
        private DateTimeOffset? _due;
        private TimeSpan _period = Timeout.InfiniteTimeSpan;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner.Now + dueTime;
                _period = period;
            }
            return true;
        }

        public bool IsDue(DateTimeOffset now) { lock (_gate) return _due is { } due && now >= due; }

        public void Fire()
        {
            lock (_gate) _due = _period == Timeout.InfiniteTimeSpan ? null : owner.Now + _period;
            callback(state);
        }

        public void Dispose() { lock (_gate) _due = null; owner.Remove(this); }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

/// <summary>Encaminha para o provedor real e conta as consultas; não altera o resultado.</summary>
internal sealed class CountingCompletionProvider(ICompletionProvider inner, Action onRequest) : ICompletionProvider
{
    public CompletionProviderKind Kind => inner.Kind;

    public ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        onRequest();
        return inner.CompleteAsync(request, cancellationToken);
    }
}

/// <summary>Catálogo estático em memória: nenhuma rede, nenhum arquivo, nenhum modelo.</summary>
internal sealed class StaticCatalog(CatalogCompleteness completeness, params CatalogSymbol[] symbols) : IKnowledgeCatalog
{
    public int Queries { get; private set; }
    public MetadataAccess? LastAccess { get; private set; }

    public CatalogResult Query(CatalogQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        Queries++;
        LastAccess = query.Access;
        return new(symbols.Select(symbol => new CatalogCandidate(symbol, CatalogMatch.Prefix)).ToArray(), completeness);
    }
}
