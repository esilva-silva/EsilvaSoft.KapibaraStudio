using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Selects the approved credential store from the captured host platform.</summary>
public static class AgentSecretStoreFactory
{
    public static ISecretStore Create(IHostPlatformSnapshot platform)
    {
        ArgumentNullException.ThrowIfNull(platform);
        return platform.IsWindows ? new WindowsCredentialSecretStore() :
            platform.IsLinux ? new LinuxSecretServiceSecretStore() :
            new UnavailableSecretStore();
    }
}
