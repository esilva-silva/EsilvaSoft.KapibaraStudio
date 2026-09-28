namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Sampled field with its BSON type name and nested structure, as returned by the server-side key/type pipeline.</summary>
public sealed record SampledField(string Name, string Type, IReadOnlyList<SampledField> Children, IReadOnlyList<SampledElement> Elements);
