using System.Collections.Frozen;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

/// <summary>
/// Projection of the MongoDB language data used by visual consumers; never contains user namespace names.
/// Only visual aliases accepted by the highlighter for documentation examples are declared here.
/// </summary>
public static class MongoSyntaxVocabulary
{
    public static FrozenSet<string> Functions { get; } = Names(symbol => (SymbolKinds.Methods & symbol.Kind.ToFlag()) != 0
        || symbol.Kind == SymbolKind.GlobalFunction && symbol.Dialects.HasFlag(EditorDialects.MongoshScript));
    public static FrozenSet<string> Operators { get; } = Names(symbol => (SymbolKinds.Operators & symbol.Kind.ToFlag()) != 0
        && symbol.Name.Length > 1 && symbol.Name[0] == '$' && char.IsLetter(symbol.Name[1]));
    public static FrozenSet<string> AggregationStages { get; } = Names(symbol => symbol.Kind == SymbolKind.AggregationStage);
    public static FrozenSet<string> AtlasSearchOperators { get; } = Names(symbol => symbol.Kind is SymbolKind.SearchOperator or SymbolKind.SearchOption);
    public static FrozenSet<string> ExtendedJsonTypes { get; } = Names(symbol => symbol.Kind == SymbolKind.BsonConstructor
        || symbol.Kind == SymbolKind.BsonType && symbol.Category == "extended-json");
    public static FrozenSet<string> Keywords { get; } = Names(symbol => symbol.Kind == SymbolKind.Keyword);
    /// <summary>GetDatabase and GetCollection are visual aliases only; the runtime does not accept them.</summary>
    public static FrozenSet<string> DslFunctions { get; } = Names(symbol => symbol.Category == "dsl", "GetDatabase", "GetCollection");
    /// <summary>ConnectionPull is a visual alias only; the runtime does not accept it.</summary>
    public static FrozenSet<string> DslRoots { get; } = Names(symbol => symbol.Category == "connection-pool", "ConnectionPull");

    private static FrozenSet<string> Names(Func<CatalogSymbol, bool> predicate, params string[] aliases) =>
        LanguageDefinition.Default.Symbols.Where(predicate).Select(symbol => symbol.Name).Concat(aliases).ToFrozenSet(StringComparer.Ordinal);
}
