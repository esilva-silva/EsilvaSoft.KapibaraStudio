using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>Deterministic catalog orchestration. It never asks a source for kinds the context did not request.</summary>
public sealed class CompletionService
{
    private readonly IKnowledgeCatalog _catalog;
    private readonly CompletionRanker _ranker;
    private readonly ICompletionProfileResolver? _profiles;

    public CompletionService(IKnowledgeCatalog catalog, CompletionRanker? ranker = null, ICompletionProfileResolver? profiles = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _ranker = ranker ?? new CompletionRanker();
        _profiles = profiles;
    }

    public ValueTask<CompletionList> CompleteAsync(CompletionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        var (queryPrefix, queryParentPath) = CatalogLocation(context);
        var query = new CatalogQuery(context.ExpectedKinds, context.Dialect, queryPrefix)
        {
            Connection = context.Scope is { } scope ? _profiles?.Resolve(scope.Connection.ProfileId) : null,
            Database = context.Scope?.Database ?? "",
            Collection = context.Scope?.Collection ?? "",
            ParentPath = queryParentPath,
            LocalSchemas = context.LocalSchemas,
            LocalSymbols = context.LocalSymbols,
            MaximumCandidates = context.MaximumCandidates,
            Access = context.CatalogAccess,
            RestrictFieldsToLocalSchemas = context.RestrictFieldsToLocalSchemas,
            IncludeNestedFields = context.RestrictFieldsToLocalSchemas
        };
        var result = _catalog.Query(query, cancellationToken);
        var candidates = result.Candidates.Where(candidate => IsAllowedByShape(candidate.Symbol, context)).ToList();
        AddFixedKeys(candidates, context);
        var items = candidates.Select(candidate => ToItem(candidate, context))
            .Concat(context.LocalSymbols.Select(symbol => ToLocalItem(symbol, context)))
            .ToArray();
        // Passa o contexto, e não só o prefixo: é ele que identifica conexão, banco, coleção e forma para o termo
        // de uso recente. Sem rastreador de uso configurado o resultado é idêntico ao do ranking por texto.
        var ranked = _ranker.Rank(items, context with { Prefix = queryPrefix }, context.MaximumItems, cancellationToken);
        // Truncamento tem três origens e todas precisam aparecer em IsIncomplete, senão um consumidor que exige
        // "o conjunto inteiro foi visto" (o portão de confiança do ghost) concluiria unicidade a partir de uma
        // amostra. (1) Completeness da fonte, para metadados parciais/carregando. (2) Corte por MaximumCandidates:
        // o catálogo para de percorrer grupos quando a cota enche e, com fonte única, isso não vira Partial — o
        // sinal aqui é a cota ter sido atingida. (3) Corte por MaximumItems: a página voltou cheia e ainda havia
        // candidatos não devolvidos, então o que ficou de fora pode conter justamente o desempate.
        var truncatedByCandidates = result.Candidates.Count >= query.MaximumCandidates;
        var truncatedByItems = ranked.Count >= context.MaximumItems && ranked.Count < items.Length;
        return ValueTask.FromResult(new CompletionList(context.Version, ranked,
            result.Completeness != CatalogCompleteness.Complete || truncatedByCandidates || truncatedByItems));
    }

    private static CompletionItem ToItem(CatalogCandidate candidate, CompletionContext context)
    {
        var symbol = candidate.Symbol;
        var isSnippet = symbol.Snippet is not null && !(context.Role == Context.CompletionCursorRole.MemberAccess && context.CallAhead);
        var kind = symbol.Kind switch
        {
            SymbolKind.Field => CompletionItemKind.Field,
            SymbolKind.ConnectionMethod or SymbolKind.DatabaseMethod or SymbolKind.CollectionMethod or SymbolKind.CursorMethod => CompletionItemKind.Method,
            SymbolKind.GlobalFunction or SymbolKind.BsonConstructor => CompletionItemKind.Function,
            SymbolKind.Keyword => CompletionItemKind.Keyword,
            SymbolKind.QueryOperator or SymbolKind.UpdateOperator or SymbolKind.ExpressionOperator or SymbolKind.AggregationStage => CompletionItemKind.Operator,
            SymbolKind.Snippet => CompletionItemKind.Snippet,
            _ => CompletionItemKind.Value
        };
        var tags = CompletionItemTags.None;
        if (symbol.Flags.HasFlag(SymbolTraits.Write)) tags |= CompletionItemTags.Write;
        if (symbol.Flags.HasFlag(SymbolTraits.Deprecated)) tags |= CompletionItemTags.Deprecated;
        if (symbol.Flags.HasFlag(SymbolTraits.Stale)) tags |= CompletionItemTags.Stale;
        var text = EditText(symbol, context, isSnippet);
        var filterText = symbol.Kind == SymbolKind.Snippet ? SnippetFilterText(symbol) : symbol.Name;
        return new(symbol.Id, symbol.Name, symbol.Detail, kind,
            new(context.ReplaceSpan, context.ReplaceSpan, text, isSnippet), filterText, 0,
            symbol.Kind == SymbolKind.Snippet ? CompletionSource.Snippet :
            symbol.Evidence != EvidenceSources.None ? CompletionSource.Schema : CompletionSource.Catalog)
        {
            Tags = tags,
            CatalogKind = symbol.Kind,
            Scope = symbol.Scope,
            ApplicableTypes = symbol.ApplicableTypes,
            FieldPath = symbol.Field?.Path,
            Documentation = new(symbol.Category, symbol.ValueShape, symbol.Parameters, symbol.Returns, symbol.Since, symbol.Evidence)
        };
    }

    private static string EditText(CatalogSymbol symbol, CompletionContext context, bool isSnippet)
    {
        var text = isSnippet ? symbol.Snippet! : symbol.Name;
        if (symbol.Snippet is not null && isSnippet) return text;
        if (symbol.Kind == SymbolKind.Field && context.Prefix.Contains('.', StringComparison.Ordinal) && symbol.Field?.Path is { Length: > 0 } fieldPath)
            text = fieldPath;
        if (context.Role == Context.CompletionCursorRole.FieldReferenceString && context.Prefix.StartsWith('$')) text = "$" + text;
        if (symbol.Kind is not (SymbolKind.Field or SymbolKind.QueryOperator or SymbolKind.AggregationStage)) return text;
        if (context.Role == Context.CompletionCursorRole.PropertyKeyString)
        {
            if (context.UnterminatedQuote && context.Prefix.Contains('.', StringComparison.Ordinal))
                return text + context.Quote;
            return context.UnterminatedQuote ? text + context.Quote : text;
        }
        if (context.Role == Context.CompletionCursorRole.PropertyKey && context.Prefix.Contains('.', StringComparison.Ordinal))
            return $"\"{text}\"";
        if (context.Role == Context.CompletionCursorRole.PropertyKey && context.PreferredQuote is { } preferred)
            return $"{preferred}{text}{preferred}";
        return text;
    }

    private static string SnippetFilterText(CatalogSymbol symbol)
    {
        var id = symbol.Id.StartsWith("Snippet/", StringComparison.Ordinal) ? symbol.Id[8..] : symbol.Name;
        if (id.StartsWith("stage.", StringComparison.Ordinal)) return "$" + id[6..];
        if (id.StartsWith("filter.", StringComparison.Ordinal)) return "$" + id[7..];
        return symbol.Name;
    }

    private static bool IsAllowedByShape(CatalogSymbol symbol, CompletionContext context)
    {
        if (context.ShapeId is not { } shapeId || !LanguageDefinition.Default.Shapes.TryGetValue(shapeId, out var shape)) return true;
        if (context.ValueTypes.Count > 0 && symbol.Kind == SymbolKind.BsonConstructor &&
            TryConstructorTypes(symbol.Name, out var constructorTypes) && !constructorTypes.Any(context.ValueTypes.Contains)) return false;
        if (symbol.Kind == SymbolKind.Snippet && shapeId == "Pipeline")
            return string.Equals(symbol.ValueShape, "Pipeline", StringComparison.Ordinal);
        // A forma descreve a estrutura do objeto, não o valor de uma referência textual nem
        // um receiver de método. Aplicá-la nesses papéis elimina candidatos válidos como `$foo`
        // e `cursor.sort()` antes que o catálogo possa ranqueá-los.
        if (context.ShapeId is "Expression" or "ExpressionOperatorObject" or "Any"
            && symbol.Kind is SymbolKind.Field or SymbolKind.SystemVariable)
            return true;
        if (context.ExpectedKinds is var expected &&
            (expected & (SymbolKinds.CursorMethod | SymbolKinds.CollectionMethod | SymbolKinds.DatabaseMethod | SymbolKinds.ConnectionMethod)) != 0)
            return true;
        if (shapeId == "UpdateModifierObject" && symbol.Kind == SymbolKind.UpdateOperator)
            return symbol.Name is "$each" or "$position" or "$slice" or "$sort";
        if (shape.Keys.Count == 0) return true;
        if (shapeId == "OperatorObject" && string.Equals(symbol.Name, "$expr", StringComparison.Ordinal)) return false;
        if (symbol.Kind == SymbolKind.Snippet)
            return string.Equals(symbol.ValueShape, shapeId, StringComparison.Ordinal)
                || shapeId == "Stage" && string.Equals(symbol.ValueShape, "Pipeline", StringComparison.Ordinal);
        foreach (var rule in shape.Keys)
        {
            if (rule.Rule == "FieldPath" && symbol.Kind == SymbolKind.Field) return true;
            if (rule.Rule is not ("Operator" or "Exclusive")) continue;
            if (rule.Kinds.Count > 0 && !rule.Kinds.Contains(symbol.Kind)) continue;
            if (rule.Names.Count > 0 && !rule.Names.Contains(symbol.Name, StringComparer.Ordinal)) continue;
            if (rule.Categories.Count > 0 && !rule.Categories.Contains(symbol.Category, StringComparer.Ordinal)) continue;
            return true;
        }
            return false;
    }

    private static bool TryConstructorTypes(string name, out IReadOnlySet<string> types)
    {
        types = name switch
        {
            "ObjectId" => new HashSet<string>(["objectId"], StringComparer.Ordinal),
            "UUID" or "CGUUID" or "JUUID" or "GUUID" or "BinData" => new HashSet<string>(["uuid", "binData"], StringComparer.Ordinal),
            "NumberInt" => new HashSet<string>(["int"], StringComparer.Ordinal),
            "NumberLong" => new HashSet<string>(["long"], StringComparer.Ordinal),
            "NumberDecimal" => new HashSet<string>(["decimal"], StringComparer.Ordinal),
            "ISODate" or "Date" => new HashSet<string>(["date"], StringComparer.Ordinal),
            _ => []
        };
        return types.Count > 0;
    }

    private static (string Prefix, string ParentPath) CatalogLocation(CompletionContext context)
    {
        var parent = context.ParentPath;
        var prefix = context.Prefix;
        if (context.ExpectedKinds == SymbolKinds.Field && prefix.StartsWith('$'))
            prefix = prefix.TrimStart('$');
        if (context.ShapeId is "Projection" or "Sort" or "FieldValueMap") parent = "";
        var separator = prefix.LastIndexOf('.');
        if (separator >= 0 && context.ExpectedKinds.HasFlag(SymbolKinds.Field))
        {
            var dottedParent = prefix[..separator];
            parent = parent.Length == 0 || parent.Equals(dottedParent, StringComparison.Ordinal)
                ? dottedParent
                : parent.EndsWith("." + dottedParent, StringComparison.Ordinal) ? parent : parent + "." + dottedParent;
            prefix = prefix[(separator + 1)..];
        }
        return (prefix, parent);
    }

    private static void AddFixedKeys(List<CatalogCandidate> candidates, CompletionContext context)
    {
        if (context.ShapeId is not { } shapeId || !LanguageDefinition.Default.Shapes.TryGetValue(shapeId, out var shape)) return;
        foreach (var name in shape.Keys.Where(rule => rule.Rule == "Fixed").SelectMany(rule => rule.Names).Distinct(StringComparer.Ordinal))
        {
            if (context.Prefix.Length > 0 && !name.StartsWith(context.Prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (candidates.Any(candidate => string.Equals(candidate.Symbol.Name, name, StringComparison.Ordinal))) continue;
            candidates.Add(new(new CatalogSymbol($"shape:{shapeId}/fixed/{name}", SymbolKind.Keyword, name, "Chave da forma")
            { Dialects = context.Dialect }, CatalogMatch.Prefix));
        }
    }

    private static CompletionItem ToLocalItem(CatalogSymbol symbol, CompletionContext context) => new(
        symbol.Id, symbol.Name, symbol.Detail, CompletionItemKind.Variable,
        new(context.ReplaceSpan, context.ReplaceSpan, symbol.Name), symbol.Name, 0, CompletionSource.Local)
    { CatalogKind = symbol.Kind, FieldPath = symbol.Field?.Path, Documentation = new(symbol.Category, symbol.ValueShape, symbol.Parameters, symbol.Returns, symbol.Since, symbol.Evidence) };
}
