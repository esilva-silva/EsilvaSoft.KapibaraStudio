using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>One instance per editor. A monotonically increasing version also rejects edit/undo ABA responses.</summary>
public sealed class CompletionSession : IDisposable
{
    private static readonly KeyValuePair<string, object?> InlineModality = new("modality", "inline");
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private long _version;
    private bool _disposed;
    public long Version { get { lock (_gate) return _version; } }

    public void Invalidate()
    {
        lock (_gate) { _version++; _pending?.Cancel(); }
    }

    /// <summary>
    /// Pedido pelo caminho legado. <c>policy</c> traz as origens permitidas, já decididas por
    /// <see cref="InlineCompletionPolicy"/>: o atalho do dicionário é parte do pedido, não um privilégio interno, e com
    /// <see cref="CompletionSourcePolicy.Dictionary"/> falso ele não é sequer consultado — a origem determinística
    /// desligada não reaparece por aqui.
    /// </summary>
    public async Task<AutocompleteResult?> RequestAsync(IAutocompleteService service, AutocompleteRequest request, bool immediate = false,
        CompletionSourcePolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        var sources = policy ?? CompletionSourcePolicy.All;
        using var cancellation = new CancellationTokenSource();
        long version;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _pending?.Cancel(); _pending = cancellation; version = ++_version;
        }
        var started = Stopwatch.GetTimestamp();
        AutocompleteMetrics.CompletionRequested.Add(1, InlineModality, new KeyValuePair<string, object?>("trigger", immediate ? "invoked" : "automatic"));
        try
        {
            if (sources.Dictionary && service.GetImmediateCompletion(request) is { } dictionary)
                return Complete(dictionary, version, cancellation, started, "dictionary");
            if (!immediate) await Task.Delay(service.Settings.DelayMilliseconds, cancellation.Token).ConfigureAwait(false);
            var result = await service.GetCompletionAsync(request, sources, cancellation.Token).ConfigureAwait(false);
            return Complete(result, version, cancellation, started, result is { IsAi: true } ? "ai" : "dictionary");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            AutocompleteMetrics.CompletionCancelled.Add(1, InlineModality, new KeyValuePair<string, object?>("reason", "superseded"));
            return null;
        }
        finally { lock (_gate) if (ReferenceEquals(_pending, cancellation)) _pending = null; }
    }

    private AutocompleteResult? Complete(AutocompleteResult? result, long version, CancellationTokenSource cancellation, long started, string source)
    {
        bool current;
        lock (_gate) current = !_disposed && version == _version && !cancellation.IsCancellationRequested;
        if (!current)
        {
            AutocompleteMetrics.CompletionCancelled.Add(1, InlineModality, new KeyValuePair<string, object?>("reason", "obsolete"));
            return null;
        }
        AutocompleteMetrics.CompletionReturned.Add(1, InlineModality, new KeyValuePair<string, object?>("source", result is null ? "none" : source),
            new KeyValuePair<string, object?>("count_bucket", result is null ? "0" : "1"));
        AutocompleteMetrics.CompletionLatency.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, InlineModality);
        return result;
    }

    public void Dispose() { lock (_gate) { _disposed = true; _version++; _pending?.Cancel(); } }
}
