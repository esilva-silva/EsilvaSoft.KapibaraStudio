using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface ISavedQueryRepository
{
    Task<IReadOnlyList<SavedQuery>> GetAllAsync(Guid? profileId, CancellationToken cancellationToken = default);

    Task SaveAsync(SavedQuery query, CancellationToken cancellationToken = default);

    Task DeleteSavedAsync(Guid id, CancellationToken cancellationToken = default);
}
