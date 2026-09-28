using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IConsoleDatabaseSessionFactory
{
    /// <summary>Optional UI localizer for validation messages emitted by the database proxy.</summary>
    void SetLocalization(Func<string, string> localize) { }

    IConsoleDatabaseSession Create(IReadOnlyList<ConnectionProfile> resolvedProfiles, int documentLimit, int timeoutMs);
}
