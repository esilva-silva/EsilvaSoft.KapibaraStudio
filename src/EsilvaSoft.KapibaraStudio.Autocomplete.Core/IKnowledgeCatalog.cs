namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

public interface IKnowledgeCatalog
{
    CatalogResult Query(CatalogQuery query, CancellationToken cancellationToken = default);
}
