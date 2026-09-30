namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Captures the host platform at the operating-system adapter boundary.</summary>
public interface IHostPlatformSnapshot
{
    bool IsWindows { get; }
    bool IsLinux { get; }
}
