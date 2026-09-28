using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IAuditRepository
{
    Task<IReadOnlyList<AuditEntry>> GetRecentAuditAsync(int maximum = 100, CancellationToken cancellationToken = default);

    Task SaveAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}
