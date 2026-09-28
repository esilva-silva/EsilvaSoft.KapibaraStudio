namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Ordered from best to worst for the user: a loading scope matters more than an unavailable one.</summary>
public enum CatalogCompleteness { Complete, Partial, Unavailable, Loading }
