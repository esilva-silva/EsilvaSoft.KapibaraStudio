using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

public sealed class ConsoleDatabaseSessionFactory(MongoClientPool? clients = null) : IConsoleDatabaseSessionFactory
{
    private Func<string, string>? _localize;
    public void SetLocalization(Func<string, string> localize) => _localize = localize ?? throw new ArgumentNullException(nameof(localize));

    public IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs) =>
        new ConsoleDatabaseSession(resolvedProfiles, documentLimit, timeoutMs, clients ?? MongoClientPool.Shared, _localize);
}
