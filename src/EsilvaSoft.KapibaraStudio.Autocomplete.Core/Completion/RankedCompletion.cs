namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

public sealed record RankedCompletion(CompletionItem Item, CatalogMatch Match, double Score, IReadOnlyDictionary<string, double> Contributions);
