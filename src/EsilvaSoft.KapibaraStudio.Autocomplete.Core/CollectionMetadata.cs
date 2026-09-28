namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>
/// Type and declared validator schema of one collection. Loaded per collection, never for a whole database,
/// so memory and transfer stay bounded by the collections actually edited.
/// </summary>
public sealed record CollectionMetadata(CollectionKind Kind, CollectionSchema? Validator);
