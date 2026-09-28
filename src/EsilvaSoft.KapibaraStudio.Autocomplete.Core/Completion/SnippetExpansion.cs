namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

public sealed record SnippetExpansion(string Text, IReadOnlyList<SnippetPlaceholder> Placeholders);
