namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

public sealed record CatalogResult(IReadOnlyList<CatalogCandidate> Candidates, CatalogCompleteness Completeness);
