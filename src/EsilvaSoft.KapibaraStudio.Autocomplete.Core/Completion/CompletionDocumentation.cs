namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>Late presentation data for a completion item. It deliberately contains schema facts, never document values.</summary>
public sealed record CompletionDocumentation(string Title, string Text, CompletionItemKind Kind, CompletionItemTags Tags);
