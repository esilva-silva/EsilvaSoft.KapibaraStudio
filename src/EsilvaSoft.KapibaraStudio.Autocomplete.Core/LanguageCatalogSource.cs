namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Built-in MongoDB language: one name table per symbol kind.</summary>
public sealed class LanguageCatalogSource : ICatalogSource
{
    private readonly KeyValuePair<SymbolKind, NameTable<CatalogSymbol>>[] _tables;
    private readonly CatalogSymbol[] _snippetSymbols;

    public LanguageCatalogSource(LanguageDefinition? language = null)
    {
        var definition = language ?? LanguageDefinition.Default;
        _tables = definition.Symbols.GroupBy(symbol => symbol.Kind).OrderBy(group => group.Key)
            .Select(group => KeyValuePair.Create(group.Key, new NameTable<CatalogSymbol>(group, symbol => symbol.Name, SearchKey))).ToArray();
        _snippetSymbols = definition.Symbols.Where(symbol => symbol.Kind == SymbolKind.Snippet).ToArray();
        ProvidedKinds = _tables.Aggregate(SymbolKinds.None, (all, table) => all | table.Key.ToFlag());
    }

    public SymbolKinds ProvidedKinds { get; }

    public CatalogCompleteness Collect(CatalogQuery query, ICollection<CatalogCandidate> sink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(sink);
        // Operators match with or without the leading $: "eq" finds $eq.
        var prefix = SearchKey(query.Prefix);
        var dialects = query.Dialect == EditorDialects.None ? EditorDialects.All : query.Dialect | EditorDialects.Mql;
        foreach (var (kind, table) in _tables)
        {
            if ((query.Kinds & kind.ToFlag()) == 0) continue;
            cancellationToken.ThrowIfCancellationRequested();
            table.Collect(prefix, query.MaximumCandidates - sink.Count, symbol => (symbol.Dialects & dialects) != 0, (symbol, match) => sink.Add(new(symbol, match)), cancellationToken);
        }
        // With an empty normalized prefix the NameTable already returns every snippet. The manual path exists only
        // for stage/filter ids whose visible label is not the searchable token ("stage.lookup" -> "$lookup").
        if ((query.Kinds & SymbolKinds.Snippet) != 0 && prefix.Length > 0 && sink.Count < query.MaximumCandidates)
        {
            CollectSnippets(prefix, query.MaximumCandidates - sink.Count, dialects, sink, cancellationToken);
        }
        return CatalogCompleteness.Complete;
    }

    internal static string SearchKey(string name) => name.TrimStart('$');

    private void CollectSnippets(string prefix, int maximum, EditorDialects dialects, ICollection<CatalogCandidate> sink, CancellationToken cancellationToken)
    {
        if (maximum <= 0) return;
        var count = 0;
        foreach (var symbol in _snippetSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((symbol.Dialects & dialects) == 0) continue;
            var searchName = SnippetSearchKey(symbol);
            if (prefix.Length != 0 && !searchName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && !searchName.Contains(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            sink.Add(new(symbol, prefix.Length == 0 ? CatalogMatch.Any : CatalogMatch.Prefix));
            if (++count >= maximum) break;
        }
    }

    private static string SnippetSearchKey(CatalogSymbol symbol)
    {
        if (symbol.Id.StartsWith("Snippet/stage.", StringComparison.Ordinal)) return symbol.Id[14..];
        if (symbol.Id.StartsWith("Snippet/filter.", StringComparison.Ordinal)) return symbol.Id[15..];
        return SearchKey(symbol.Name);
    }
}
