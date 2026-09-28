namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Valid keys, values or elements of a MongoDB document or array in a given position.</summary>
public sealed record ShapeDefinition(string Id)
{
    public IReadOnlyList<ShapeKeyRule> Keys { get; init; } = [];
    public IReadOnlyList<string> Values { get; init; } = [];
    public string? Element { get; init; }
}
