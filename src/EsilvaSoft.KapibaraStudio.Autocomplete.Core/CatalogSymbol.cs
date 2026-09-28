namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>One suggestible piece of knowledge. Long documentation is resolved later and never stored here.</summary>
public sealed record CatalogSymbol(string Id, SymbolKind Kind, string Name, string Detail)
{
    public string Category { get; init; } = "";
    public EditorDialects Dialects { get; init; } = EditorDialects.Mql;
    public SymbolTraits Flags { get; init; }
    /// <summary>Shape of the value or argument, when the symbol takes one.</summary>
    public string? ValueShape { get; init; }
    public IReadOnlyList<string> Parameters { get; init; } = [];
    public string? Returns { get; init; }
    /// <summary>LSP snippet syntax: $1, ${1:placeholder}, ${1|a,b|}, $0 and \$ for a literal dollar.</summary>
    public string? Snippet { get; init; }
    /// <summary>BSON types of the field the operator applies to; empty means any type.</summary>
    public IReadOnlyList<string> ApplicableTypes { get; init; } = [];
    public string? Since { get; init; }
    public CatalogScope? Scope { get; init; }
    public EvidenceSources Evidence { get; init; }
    public FieldNode? Field { get; init; }
}
