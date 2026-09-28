namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

public interface IMetadataInvalidationBus
{
    event EventHandler<MetadataInvalidationEventArgs>? Published;
    void Publish(MetadataInvalidation invalidation);
}
