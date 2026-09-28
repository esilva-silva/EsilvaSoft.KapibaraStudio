using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>
/// Small per-editor context cache. A context is immutable and keyed by the full captured request identity, so a
/// changed destination or cursor can never reuse semantic data from another request. It owns the tokens of the documents
/// it analyzes, so a repeated analysis of the same version does not read or lex the document again; entries are keyed by
/// document and released by <see cref="ClearDocument"/>. No syntax tree is built on this path: nothing here reads nodes.
/// </summary>
public sealed class CompletionContextCache
{
    private readonly object _gate = new();
    private readonly TokenCache _tokens = new();
    private Entry? _last;

    public CompletionContextAnalysis Analyze(ContextRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var key = ContextKey.From(request);
        lock (_gate)
            if (_last is { Key: var cached } entry && cached == key) return entry.Analysis;
        var analysis = CompletionContextEngine.Analyze(request, _tokens, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate) _last = new(key, analysis);
        return analysis;
    }

    /// <summary>Releases everything held for a closed document: its context, its text and its tokens.</summary>
    public void ClearDocument(long documentId)
    {
        lock (_gate)
            if (_last?.Key.Version.DocumentId == documentId) _last = null;
        _tokens.RemoveDocument(documentId);
    }

    private sealed record Entry(ContextKey Key, CompletionContextAnalysis Analysis);

    // O schema de entrada entra na chave por referência: uma análise feita antes de o schema carregar não pode ser servida depois.
    private readonly record struct ContextKey(TextSnapshotVersion Version, int Caret, EditorDialects Dialect,
        CatalogScope? Scope, CompletionTrigger Trigger, CollectionSchema? InputSchema)
    {
        public static ContextKey From(ContextRequest request) =>
            new(request.Snapshot.Version, request.ValidCaret, request.Dialect, request.TabScope, request.Trigger, request.InputSchema);
    }
}
