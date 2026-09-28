using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IWorkspaceSessionRepository
{
    Task<WorkspaceSession> LoadSessionAsync(CancellationToken cancellationToken = default);
    Task SaveSessionAsync(WorkspaceSession session, CancellationToken cancellationToken = default);
}
