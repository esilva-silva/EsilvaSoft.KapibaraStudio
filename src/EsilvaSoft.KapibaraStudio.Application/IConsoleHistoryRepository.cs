using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IConsoleHistoryRepository
{
    Task SaveConsoleHistoryAsync(ConsoleHistoryEntry entry, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ConsoleHistoryEntry>> GetConsoleHistoryAsync(int maximum = 100, CancellationToken cancellationToken = default);
}
