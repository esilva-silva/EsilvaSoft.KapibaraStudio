namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Insertion at the captured cursor, without replacing the prefix.</summary>
public sealed record AutocompleteResult(string Text, bool IsAi, string Description);
