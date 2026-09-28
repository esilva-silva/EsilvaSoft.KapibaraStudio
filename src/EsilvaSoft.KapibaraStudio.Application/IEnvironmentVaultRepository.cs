using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IEnvironmentVaultRepository
{
    EnvironmentVault LoadEnvironments();
    void SaveEnvironments(EnvironmentVault vault);
}
