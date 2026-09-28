namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

public sealed class MetadataInvalidationBus : IMetadataInvalidationBus
{
    public event EventHandler<MetadataInvalidationEventArgs>? Published;

    public void Publish(MetadataInvalidation invalidation)
    {
        ArgumentNullException.ThrowIfNull(invalidation);
        Published?.Invoke(this, new(invalidation));
    }
}
