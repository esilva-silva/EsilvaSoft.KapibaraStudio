namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Rule for the keys of an object shape: field path, operator symbol, fixed name, free name or single exclusive key.</summary>
public sealed record ShapeKeyRule(string Rule)
{
    public IReadOnlyList<string> Names { get; init; } = [];
    public IReadOnlyList<SymbolKind> Kinds { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public string? Value { get; init; }
    public bool Required { get; init; }
    public string? Scope { get; init; }
}
