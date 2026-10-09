using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IImportCheckpointRepository
{
    Task<IReadOnlyList<ImportCheckpoint>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task CreateAsync(ImportCheckpoint checkpoint, CancellationToken cancellationToken = default);
    Task UpdateAsync(ImportCheckpoint checkpoint, CancellationToken cancellationToken = default);
    Task ResetForRestartAsync(ImportCheckpoint expected, ImportCheckpoint restarted, CancellationToken cancellationToken = default);
    Task CompleteAsync(Guid id, CancellationToken cancellationToken = default);
}
