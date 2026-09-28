namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

public sealed record MetadataKey(ConnectionIdentity Connection, MetadataScope Scope, string Database = "", string Collection = "")
{
    /// <summary>Collection-scoped values are bounded by the per-connection LRU.</summary>
    public bool IsCollectionScoped => Scope is MetadataScope.Definition or MetadataScope.Indexes or MetadataScope.SampledSchema;
}
