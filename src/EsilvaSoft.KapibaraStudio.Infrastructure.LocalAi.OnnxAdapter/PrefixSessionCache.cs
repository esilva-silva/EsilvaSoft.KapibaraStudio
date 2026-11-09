namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>LRU em memória. Uma sessão cancelada é removida imediatamente e liberada após seu último uso.</summary>
internal sealed class PrefixSessionCache<T>(Action<string>? invalidated = null) : IDisposable where T : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Lease> _sessions = new(StringComparer.Ordinal);
    private long _clock;

    internal sealed class Lease : IDisposable
    {
        internal required PrefixSessionCache<T> Owner { get; init; }
        internal required string Key { get; init; }
        internal required T Generator { get; init; }
        internal int ContextLength { get; init; }
        internal long LastUsed { get; set; }
        internal bool Active { get; set; }
        internal bool Removed { get; set; }
        internal CancellationTokenRegistration Registration { get; set; }
        internal int[] Tokens { get; set; } = [];
        public void Dispose() => Owner.Return(this);
    }

    internal Lease Acquire(string key, int capacity, int contextLength, Func<T> create, CancellationToken lifetime = default)
    {
        lock (_gate)
        {
            lifetime.ThrowIfCancellationRequested();
            if (_sessions.TryGetValue(key, out var existing))
            {
                if (existing.Active) throw new InvalidOperationException("Sessão de prefixo já em uso.");
                if (existing.ContextLength == contextLength)
                {
                    existing.Active = true;
                    existing.LastUsed = ++_clock;
                    return existing;
                }
                Remove(existing, "context_resize");
            }
            while (_sessions.Count >= capacity)
            {
                var victim = _sessions.Values.Where(item => !item.Active).MinBy(item => item.LastUsed)
                    ?? throw new InvalidOperationException("Todas as sessões de prefixo estão em uso.");
                Remove(victim, "lru");
            }
            var lease = new Lease { Owner = this, Key = key, Generator = create(), ContextLength = contextLength,
                LastUsed = ++_clock, Active = true };
            _sessions.Add(key, lease);
            lease.Registration = lifetime.Register(() => Invalidate(lease, "session_closed"));
            return lease;
        }
    }

    internal void Invalidate(Lease lease, string reason = "failure")
    {
        lock (_gate) Remove(lease, reason);
    }

    internal void InvalidateSession(string key, string reason)
    {
        lock (_gate)
            if (_sessions.TryGetValue(key, out var lease)) Remove(lease, reason);
    }

    private void Remove(Lease lease, string reason)
    {
        if (lease.Removed) return;
        lease.Removed = true;
        _sessions.Remove(lease.Key);
        // Não aguardar um callback que pode estar esperando este mesmo lock.
        lease.Registration.Unregister();
        if (!lease.Active) lease.Generator.Dispose();
        invalidated?.Invoke(reason);
    }

    private void Return(Lease lease)
    {
        lock (_gate)
        {
            if (!lease.Active) return;
            lease.Active = false;
            if (lease.Removed) lease.Generator.Dispose();
        }
    }

    /// <summary>
    /// Bloco mínimo sempre reprocessado no fim do prompt. Um prefill de apenas um token mudou a saída INT4 na prova
    /// nativa; reprocessar um bloco mínimo mantém prompts curtos no caminho completo. A equivalência numérica ainda
    /// exige homologação por pacote.
    /// </summary>
    internal const int MinimumRecomputedTokens = 32;

    /// <summary>
    /// Quantos tokens do KV da sessão o próximo prompt reaproveita: o maior prefixo comum, limitado pelo teto do
    /// chamador e deixando ao menos <see cref="MinimumRecomputedTokens"/> para reprocessar. Comparação de ids, nunca
    /// de texto.
    /// </summary>
    internal static int ReusableTokensForPrompt(IReadOnlyList<int> previous, IReadOnlyList<int> input, int? cap)
        => ReusableTokens(previous, input, Math.Min(cap ?? int.MaxValue, Math.Max(0, input.Count - MinimumRecomputedTokens)));

    internal static int ReusableTokens(IReadOnlyList<int> previous, IReadOnlyList<int> input, int? cap)
    {
        var maximum = Math.Min(Math.Min(previous.Count, Math.Max(0, input.Count - 1)), Math.Max(0, cap ?? int.MaxValue));
        var count = 0;
        while (count < maximum && previous[count] == input[count]) count++;
        return count;
    }

    public void Dispose()
    {
        lock (_gate)
            foreach (var lease in _sessions.Values.ToArray()) Remove(lease, "model_release");
    }
}
