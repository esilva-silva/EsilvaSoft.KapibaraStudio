using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Reads the current host platform for platform-specific adapters.</summary>
public sealed class LocalHostPlatformSnapshot : IHostPlatformSnapshot
{
    public bool IsWindows { get; } = OperatingSystem.IsWindows();
    public bool IsLinux { get; } = OperatingSystem.IsLinux();
}
