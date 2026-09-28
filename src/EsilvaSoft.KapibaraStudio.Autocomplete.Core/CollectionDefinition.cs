namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>One collection as returned by listCollections filtered by name.</summary>
public sealed record CollectionDefinition(string Name, CollectionKind Kind, string? ValidatorJson);
