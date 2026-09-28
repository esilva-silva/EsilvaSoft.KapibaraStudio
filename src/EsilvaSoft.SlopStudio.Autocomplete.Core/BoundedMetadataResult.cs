namespace EsilvaSoft.SlopStudio.Autocomplete.Core;

/// <summary>A bounded cursor read. Overflow means at least one further name existed.</summary>
public sealed record BoundedMetadataResult<T>(IReadOnlyList<T> Items, bool Overflow);
