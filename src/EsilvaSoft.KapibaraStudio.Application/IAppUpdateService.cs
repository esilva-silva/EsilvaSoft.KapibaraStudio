using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IAppUpdateService
{
    AppUpdateAvailability Availability { get; }
    AppVersion CurrentVersion { get; }

    /// <summary>Runtime identifier of the running installation (for example <c>linux-x64</c>).</summary>
    string Rid { get; }

    /// <summary>Package manager that owns the installation; set only when <see cref="Availability"/> is PackageManaged.</summary>
    LinuxPackageManager? PackageManager { get; }

    /// <summary>Update downloaded in this or a previous session and not applied yet.</summary>
    StagedAppUpdate? GetStagedUpdate();

    /// <summary>Returns null when there is no newer release or the feed is unreachable.</summary>
    Task<AppUpdateRelease?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads, verifies and stages the package; it is installed after the application exits.</summary>
    Task<StagedAppUpdate> DownloadAsync(AppUpdateRelease release, ApplicationOperationScope operation);
}
