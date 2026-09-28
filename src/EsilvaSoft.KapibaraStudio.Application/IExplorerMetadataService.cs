using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IExplorerMetadataService
{
    /// <summary>Provides product-language labels for metadata presented by the desktop; optional for headless callers.</summary>
    void SetLocalization(Func<string, string> localize) { }
    Task<TopologyInfo> GetTopologyAsync(ConnectionProfile profile, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<IndexInfo>> GetIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default);
    Task<string> GetCollectionDetailsAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken = default);
}
