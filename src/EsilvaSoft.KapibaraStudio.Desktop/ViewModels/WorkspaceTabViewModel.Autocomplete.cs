using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    /// <summary>
    /// Gerador determinístico da sugestão automática, atribuído pela composição do workspace. Nulo em abas isoladas
    /// (design-time ou teste), caso em que o ghost simplesmente não tem origem contextual.
    /// </summary>
    public ICompletionProvider? InlinePreemptiveCompletion { get; set; }

    private long _inlineCompletionRequestId;

    /// <summary>
    /// Sugestão automática determinística para esta aba, a partir de texto solto. Sintetiza uma versão de documento
    /// própria do caminho automático (nunca a da lista explícita: são duas modalidades, e um contador compartilhado
    /// faria uma invalidar a versão da outra). Preferir a sobrecarga que recebe o <see cref="ITextSnapshot"/> do
    /// editor: só ela tem linhagem de versões e permite reaproveitar os tokens já lexificados.
    /// </summary>
    public Task<InlineCompletionSuggestion?> GetInlineCompletionAsync(string text, int caret, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(text);
        TextSnapshotVersion version;
        lock (_inlineSnapshotGate)
        {
            if (!string.Equals(_inlineSnapshotText, text, StringComparison.Ordinal))
            {
                _inlineSnapshotText = text;
                _inlineSnapshotSequence++;
            }
            version = new TextSnapshotVersion(_inlineCompletionDocumentId, _inlineSnapshotSequence);
        }
        return GetInlineCompletionAsync(new StringTextSnapshot(text, version), caret, token);
    }

    /// <summary>
    /// Sugestão automática determinística para esta aba. Não faz rede, não carrega metadados (o provedor impõe
    /// <see cref="MetadataAccess.Peek"/>) e não depende de modelo de IA. Todo o contexto vem de valores capturados
    /// antes do primeiro await; o token é o da pendência do coordenador desta aba. O snapshot vem do editor, então
    /// duas análises da mesma versão (ou de versões encadeadas) reaproveitam o cache de tokens em vez de relexificar.
    /// </summary>
    public async Task<InlineCompletionSuggestion?> GetInlineCompletionAsync(ITextSnapshot snapshot, int caret, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (InlinePreemptiveCompletion is not { } provider) return null;
        token.ThrowIfCancellationRequested();
        var profile = Profile;
        var scope = profile is null ? null : new CatalogScope(ConnectionIdentity.From(profile), Database, Collection);
        var dialect = Mode switch { "Agregação" => EditorDialects.AggregationJson, "Script" => EditorDialects.MongoshScript, _ => EditorDialects.Console };
        var capturedCaret = Math.Clamp(caret, 0, snapshot.Length);
        // Peek: lê o cache de metadados já carregado, nunca agenda I/O.
        var inputSchema = scope is null ? null : PipelineInputSchema?.Invoke(scope);
        var requestId = Interlocked.Increment(ref _inlineCompletionRequestId);
        return await Task.Run(async () =>
        {
            var context = _inlineContextCache.Analyze(
                new ContextRequest(snapshot, capturedCaret, dialect, scope, CompletionTrigger.Automatic) { InputSchema = inputSchema }, token).Context;
            var request = new CompletionRequest(context, requestId, 0);
            var response = await provider.CompleteAsync(request, token);
            token.ThrowIfCancellationRequested();
            // Resposta de outro pedido desta aba nunca vira ghost, mesmo que o provedor ignore o cancelamento.
            if (!response.IsFor(request) || response.List.Items.Count == 0) return null;
            return InlineCompletionSuggestion.TryCreate(context, response.List.Items[0], snapshot, capturedCaret);
        }, token);
    }

    // Cache próprio do caminho automático: a lista explícita tem o seu, e uma análise não pode ser invalidada pela
    // outra modalidade nem competir com ela pela mesma entrada.
    private readonly CompletionContextCache _inlineContextCache = new();
    // Identidade e contador de versão do caminho automático, separados dos da lista explícita pelo mesmo motivo do
    // cache acima. Só são usados quando a origem do texto não é um snapshot do editor (testes e chamadas por string).
    private readonly long _inlineCompletionDocumentId = TextSnapshotVersion.NewDocumentId();
    private readonly object _inlineSnapshotGate = new();
    private string _inlineSnapshotText = "";
    private long _inlineSnapshotSequence;

    public Func<IReadOnlyList<EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting.SyntaxNamespace>> KnownSyntaxNamespaces { get; set; } = () => [];
    public void RefreshSyntaxContext() => OnPropertyChanged("SyntaxContext");
    public EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting.SyntaxContext CaptureSyntaxContext() =>
        new(KnownSyntaxNamespaces().Append(new(Profile?.Name ?? "", Database, Collection)).Distinct().ToArray(), Profile?.Name ?? "", Database, Collection);
    public Func<IReadOnlyList<string>> KnownAutocompleteNames { get; set; } = () => [];
    public IReadOnlyList<string> GetObservedCompletionFields(string prefix)
    {
        var target = IsAggregation ? new EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting.SyntaxNamespace(Profile?.Name ?? "", Database, Collection)
            : MongoCompletionTarget.Resolve(prefix, CaptureSyntaxContext());
        // A bare field prefix has no query path; use an unambiguous result source in this tab's current destination.
        if (target is null && prefix.All(c => char.IsLetterOrDigit(c) || c is '_' or '$'))
        {
            var origins = ResultSets.Select(set => set.Origin).Where(origin => origin.Profile == Profile && origin.Database == Database && origin.HasCollection)
                .Select(origin => new EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting.SyntaxNamespace(origin.Profile!.Name, origin.Database!, origin.Collection!)).Distinct().ToArray();
            if (origins.Length == 1) target = origins[0];
        }
        IReadOnlyList<string> Observed(string collection)
        {
            if (!Autocomplete.Settings.UseResultPanelContext || target is null || collection.Length == 0) return [];
            // Results only change on execution or destination change: reuse the inference instead of parsing documents per keystroke.
            if (_observedFields is not { } memo || !ReferenceEquals(memo.Sets, _resultSets) || memo.Profile != Profile
                || memo.Connection != target.Connection || memo.Database != target.Database)
                _observedFields = memo = (_resultSets, Profile, target.Connection, target.Database, new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
            if (memo.Fields.TryGetValue(collection, out var cached)) return cached;
            var documents = _resultSets.Where(set => set.Origin.Profile?.Name == target.Connection && set.Origin.Database == target.Database &&
                    set.Origin.Collection == collection && (target.Connection != Profile?.Name || set.Origin.Profile == Profile))
                .SelectMany(set => set.Documents ?? []).Take(8).Select(document => document.Json).Where(json => json.Length <= 65536);
            return memo.Fields[collection] = MqlAutocompleteService.InferFieldPaths(documents).Take(128).ToArray();
        }
        var fields = Observed(target?.Collection ?? "");
        return Autocomplete.Settings.UseEditorContext ? AggregationFieldInference.Infer(prefix, fields, Observed) : fields;
    }
    // Inferred fields per collection, valid while the result sets, profile, connection and database stay the same.
    private (StructuredResultSet[] Sets, ConnectionProfile? Profile, string Connection, string Database, Dictionary<string, IReadOnlyList<string>> Fields)? _observedFields;
    public AutocompleteRequest CaptureAutocompleteRequest(string text, int caret)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var settings = Autocomplete.Settings;
        var fields = GetObservedCompletionFields(text[..Math.Clamp(caret, 0, text.Length)]);
        var names = KnownAutocompleteNames().Concat(new[] { Profile?.Name ?? "", Database, Collection }).ToArray();
        var history = ConsoleHistory.Where(entry => entry.ProfileId == Profile?.Id && entry.Database == Database)
            .OrderByDescending(entry => entry.ExecutedAt).Take(3).Select(entry => entry.Script).ToArray();
        var language = Mode switch { "Agregação" => "json", "Script" => "JavaScript (mongosh)", _ => "Mongo Console JavaScript" };
        var request = AutocompleteContextBuilder.Build(new(text, caret, language,
            InputJson, fields, names, history, FilePath), settings);
        EsilvaSoft.KapibaraStudio.Autocomplete.Core.AutocompleteMetrics.ContextBuildDuration.Record(
            System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, new KeyValuePair<string, object?>("pipeline", "legacy"));
        return request;
    }
}
