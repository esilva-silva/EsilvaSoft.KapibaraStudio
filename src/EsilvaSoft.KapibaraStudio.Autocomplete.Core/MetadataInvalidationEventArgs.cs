namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

public sealed class MetadataInvalidationEventArgs(MetadataInvalidation invalidation) : EventArgs
{
    public MetadataInvalidation Invalidation { get; } = invalidation;
}
