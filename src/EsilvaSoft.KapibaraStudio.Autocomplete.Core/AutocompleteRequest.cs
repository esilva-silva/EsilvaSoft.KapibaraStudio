namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Immutable bounded editor snapshot; auxiliary context is transient and never persisted.</summary>
public sealed record AutocompleteRequest(string Prefix, string Suffix, string? Language = null, string? FileName = null)
{
    public bool RequireComplete { get; init; }
    public string Context { get; init; } = "";
    public IReadOnlyList<string> Dictionary { get; init; } = [];
    public const int MaximumContextCharacters = 32768;
    public AutocompleteRequest Bounded() => this with
    {
        Prefix = Prefix.Length > MaximumContextCharacters ? Prefix[^MaximumContextCharacters..] : Prefix,
        Suffix = Suffix.Length > MaximumContextCharacters ? Suffix[..MaximumContextCharacters] : Suffix,
        Context = Context[..Math.Min(Context.Length, 8192)],
        Dictionary = Dictionary.Take(256).Select(word => word[..Math.Min(word.Length, 128)]).ToArray()
    };
}
