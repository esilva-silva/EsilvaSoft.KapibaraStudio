using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IProfilerCaptureRepository
{
    Task<IReadOnlyList<ProfilerCaptureTicket>> GetPendingAsync(CancellationToken cancellationToken = default);
    Task CreateAsync(ProfilerCaptureTicket ticket, CancellationToken cancellationToken = default);
    Task UpdateAsync(ProfilerCaptureTicket ticket, CancellationToken cancellationToken = default);
    Task CompleteAsync(Guid id, CancellationToken cancellationToken = default);
}
