using System.Diagnostics;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Motivo pelo qual um pedido automático foi descartado; vira métrica, nunca texto do editor.</summary>
public enum InlineCompletionCancelReason : byte
{
    Superseded,
    Typing,
    CaretMoved,
    SelectionChanged,
    DocumentChanged,
    ExplicitRequest,
    Disabled,
    Closed
}

/// <summary>
/// Dono único da sugestão automática de um editor. Mantém no máximo uma pendência, substituível: qualquer evento novo
/// cancela a anterior, de modo que eventos da mesma edição são coalescidos em uma computação só. O atraso usa
/// <see cref="TimeProvider"/> (não <c>Task.Delay</c> cru) para que o teste avance um relógio falso, e o resultado de um
/// pedido antigo nunca é aplicado: a geração é reconferida depois de cada await, mesmo que o gerador ignore o
/// cancelamento cooperativo. Uma instância por editor; o <see cref="CancellationTokenSource"/> nunca atravessa abas.
/// </summary>
public sealed class InlineCompletionCoordinator(TimeProvider? timeProvider = null) : IDisposable
{
    private static readonly KeyValuePair<string, object?> InlineModality = new("modality", "inline");
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;
    private readonly object _gate = new();
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _disposed;

    /// <summary>Cresce a cada pedido e a cada cancelamento; identifica a pendência atual deste editor.</summary>
    public long Generation { get { lock (_gate) return _generation; } }

    /// <summary>Quantidade de computações efetivamente iniciadas (pós-atraso). Observável para medição e teste.</summary>
    public long Computations => Interlocked.Read(ref _computations);
    private long _computations;

    /// <summary>Cancela a pendência atual, se houver. Seguro de chamar quando não há nenhuma.</summary>
    public void Cancel(InlineCompletionCancelReason reason = InlineCompletionCancelReason.Superseded)
    {
        CancellationTokenSource? pending;
        lock (_gate)
        {
            _generation++;
            pending = _pending;
            _pending = null;
        }
        if (pending is null) return;
        try { pending.Cancel(); } catch (ObjectDisposedException) { }
        AutocompleteMetrics.CompletionCancelled.Add(1, InlineModality, new KeyValuePair<string, object?>("reason", Tag(reason)));
    }

    /// <summary>
    /// Agenda uma sugestão automática. Devolve <c>null</c> sem computar nada quando a configuração ou o estado do
    /// editor não permitem, quando o atraso é interrompido por um evento novo, ou quando a resposta chega tarde demais
    /// para a edição que a originou.
    /// </summary>
    /// <param name="settings">Configuração capturada antes do await.</param>
    /// <param name="state">Estado do editor capturado antes do await.</param>
    /// <param name="compute">Geração propriamente dita; recebe o token da pendência.</param>
    public async Task<InlineCompletionSuggestion?> RequestAsync(AutocompleteSettings settings, InlineCompletionEditorState state,
        Func<CancellationToken, Task<InlineCompletionSuggestion?>> compute)
        => await RequestAsync(settings, state, null, compute).ConfigureAwait(true);

    /// <summary>
    /// Runs a cheap deterministic provider immediately, then applies the configured debounce to optional fallbacks
    /// (lexical/AI) only when the deterministic provider abstains. A null <paramref name="computeImmediate"/> keeps
    /// the whole request behind the configured delay.
    /// </summary>
    public async Task<InlineCompletionSuggestion?> RequestAsync(AutocompleteSettings settings, InlineCompletionEditorState state,
        Func<CancellationToken, Task<InlineCompletionSuggestion?>>? computeImmediate,
        Func<CancellationToken, Task<InlineCompletionSuggestion?>> computeAfterDelay)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(computeAfterDelay);
        if (!InlineCompletionPolicy.Allows(settings, state))
        {
            Cancel(InlineCompletionPolicy.AnyInline(settings) ? InlineCompletionCancelReason.Superseded : InlineCompletionCancelReason.Disabled);
            return null;
        }

        using var cancellation = new CancellationTokenSource();
        long generation;
        CancellationTokenSource? superseded;
        lock (_gate)
        {
            if (_disposed) return null;
            superseded = _pending;
            _pending = cancellation;
            generation = ++_generation;
        }
        // Cancelado fora do lock, como em Cancel e Dispose: as continuações síncronas da pendência anterior não
        // rodam com o gate deste coordenador na mão.
        try { superseded?.Cancel(); } catch (ObjectDisposedException) { }
        var started = Stopwatch.GetTimestamp();
        AutocompleteMetrics.CompletionRequested.Add(1, InlineModality, new KeyValuePair<string, object?>("trigger", "automatic"));
        try
        {
            InlineCompletionSuggestion? suggestion = null;
            if (computeImmediate is not null)
            {
                Interlocked.Increment(ref _computations);
                suggestion = await computeImmediate(cancellation.Token).ConfigureAwait(true);
                if (!IsCurrent(generation, cancellation)) return Obsolete();
                if (suggestion is not null)
                {
                    RecordReturned(suggestion);
                    AutocompleteMetrics.CompletionLatency.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, InlineModality);
                    return suggestion;
                }
            }
            // Debounce apenas as fontes opcionais. Um hit determinístico publica sem atraso; se ele abstém,
            // typeahead continua substituindo esta pendência e IA permanece protegida pelo intervalo configurado.
            var delay = TimeSpan.FromMilliseconds(Math.Clamp(settings.DelayMilliseconds, 50, 2000));
            await Task.Delay(delay, _clock, cancellation.Token).ConfigureAwait(true);
            if (!IsCurrent(generation, cancellation)) return Obsolete();
            Interlocked.Increment(ref _computations);
            suggestion = await computeAfterDelay(cancellation.Token).ConfigureAwait(true);
            // Reconferência obrigatória: um gerador que ignore o token ainda assim não atualiza o editor.
            if (!IsCurrent(generation, cancellation)) return Obsolete();
            RecordReturned(suggestion);
            AutocompleteMetrics.CompletionLatency.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, InlineModality);
            return suggestion;
        }
        catch (OperationCanceledException)
        {
            AutocompleteMetrics.CompletionCancelled.Add(1, InlineModality, new KeyValuePair<string, object?>("reason", "superseded"));
            return null;
        }
        finally
        {
            lock (_gate) if (ReferenceEquals(_pending, cancellation)) _pending = null;
        }
    }

    private static void RecordReturned(InlineCompletionSuggestion? suggestion) => AutocompleteMetrics.CompletionReturned.Add(1, InlineModality,
        new KeyValuePair<string, object?>("source", suggestion is null ? "none" : suggestion.IsAi ? "ai" : "inline"),
        new KeyValuePair<string, object?>("count_bucket", suggestion is null ? "0" : "1"));

    private bool IsCurrent(long generation, CancellationTokenSource cancellation)
    {
        lock (_gate) return !_disposed && generation == _generation && !cancellation.IsCancellationRequested;
    }

    private static InlineCompletionSuggestion? Obsolete()
    {
        AutocompleteMetrics.CompletionCancelled.Add(1, InlineModality, new KeyValuePair<string, object?>("reason", "obsolete"));
        return null;
    }

    private static string Tag(InlineCompletionCancelReason reason) => reason switch
    {
        InlineCompletionCancelReason.Typing => "typing",
        InlineCompletionCancelReason.CaretMoved => "caret",
        InlineCompletionCancelReason.SelectionChanged => "selection",
        InlineCompletionCancelReason.DocumentChanged => "document",
        InlineCompletionCancelReason.ExplicitRequest => "explicit",
        InlineCompletionCancelReason.Disabled => "disabled",
        InlineCompletionCancelReason.Closed => "closed",
        _ => "superseded"
    };

    public void Dispose()
    {
        CancellationTokenSource? pending;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _generation++;
            pending = _pending;
            _pending = null;
        }
        try { pending?.Cancel(); } catch (ObjectDisposedException) { }
    }
}
