using System.Collections.Concurrent;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>
/// Session-only, decaying completion-use signal. It intentionally stores names only;
/// no document values, credentials, or data are retained.
/// </summary>
public sealed class CompletionUsageTracker
{
    private static readonly TimeSpan DefaultDecay = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan DefaultUndoWindow = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<CompletionUsageKey, Entry> _entries = [];
    private readonly TimeProvider _clock;
    private readonly TimeSpan _decay;
    private readonly TimeSpan _undoWindow;

    public CompletionUsageTracker(TimeProvider? clock = null, TimeSpan? decay = null, TimeSpan? undoWindow = null)
    {
        _clock = clock ?? TimeProvider.System;
        _decay = decay ?? DefaultDecay;
        _undoWindow = undoWindow ?? DefaultUndoWindow;
        if (_decay <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(decay));
        if (_undoWindow < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(undoWindow));
    }

    public void RecordAccepted(CompletionUsageKey key) => GetEntry(key).Add(1, _clock.GetUtcNow(), _decay, true);

    /// <summary>Records a negative signal only when the most recent acceptance is undone promptly.</summary>
    public bool RecordUndone(CompletionUsageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _entries.TryGetValue(key, out var entry) && entry.TryUndo(_clock.GetUtcNow(), _decay, _undoWindow);
    }

    /// <summary>Returns 1 - exp(-signal), where signal is the decayed signed acceptance sum.</summary>
    public double GetUsage(CompletionUsageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _entries.TryGetValue(key, out var entry) ? entry.GetUsage(_clock.GetUtcNow(), _decay) : 0;
    }

    private Entry GetEntry(CompletionUsageKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _entries.GetOrAdd(key, static _ => new Entry());
    }

    private sealed class Entry
    {
        private readonly object _gate = new();
        private double _signal;
        private DateTimeOffset _lastUpdated;
        private DateTimeOffset? _undoableAcceptance;

        public void Add(double value, DateTimeOffset now, TimeSpan decay, bool undoable)
        {
            lock (_gate)
            {
                DecayTo(now, decay);
                _signal += value;
                if (undoable) _undoableAcceptance = now;
            }
        }

        public bool TryUndo(DateTimeOffset now, TimeSpan decay, TimeSpan undoWindow)
        {
            lock (_gate)
            {
                if (_undoableAcceptance is not { } acceptedAt || now - acceptedAt > undoWindow) return false;
                DecayTo(now, decay);
                _signal -= 1;
                _undoableAcceptance = null;
                return true;
            }
        }

        public double GetUsage(DateTimeOffset now, TimeSpan decay)
        {
            lock (_gate)
            {
                DecayTo(now, decay);
                return 1 - Math.Exp(-_signal);
            }
        }

        private void DecayTo(DateTimeOffset now, TimeSpan decay)
        {
            if (_lastUpdated == default) { _lastUpdated = now; return; }
            if (now <= _lastUpdated) return;
            _signal *= Math.Exp(-(now - _lastUpdated).TotalSeconds / decay.TotalSeconds);
            _lastUpdated = now;
        }
    }
}

/// <summary>Privacy-safe identity used to scope a completion-use signal.</summary>
public sealed record CompletionUsageKey
{
    public CompletionUsageKey(string connectionId, string database, string collection, string shape, string symbolId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        ArgumentException.ThrowIfNullOrWhiteSpace(shape);
        ArgumentException.ThrowIfNullOrWhiteSpace(symbolId);
        ConnectionId = connectionId;
        Database = database;
        Collection = collection;
        Shape = shape;
        SymbolId = symbolId;
    }

    public string ConnectionId { get; }
    public string Database { get; }
    public string Collection { get; }
    public string Shape { get; }
    public string SymbolId { get; }
}
