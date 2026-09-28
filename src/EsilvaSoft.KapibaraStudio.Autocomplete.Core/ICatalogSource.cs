namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

public interface ICatalogSource
{
    SymbolKinds ProvidedKinds { get; }
    /// <summary>Appends candidates from memory only; never performs I/O on the calling thread.</summary>
    CatalogCompleteness Collect(CatalogQuery query, ICollection<CatalogCandidate> sink, CancellationToken cancellationToken);
}
