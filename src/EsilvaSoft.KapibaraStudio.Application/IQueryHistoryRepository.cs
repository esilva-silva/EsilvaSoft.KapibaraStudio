using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IQueryHistoryRepository
{
    Task<IReadOnlyList<QueryHistoryEntry>> GetRecentAsync(Guid? profileId, int maximum = 50, CancellationToken cancellationToken = default);

    Task SaveAsync(QueryHistoryEntry entry, CancellationToken cancellationToken = default);
}
