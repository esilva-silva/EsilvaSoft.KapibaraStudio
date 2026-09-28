using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

public interface IAppUpdateService
{
    AppUpdateAvailability Availability { get; }
    AppVersion CurrentVersion { get; }

    /// <summary>Update downloaded in this or a previous session and not applied yet.</summary>
    StagedAppUpdate? GetStagedUpdate();

    /// <summary>Returns null when there is no newer release or the feed is unreachable.</summary>
    Task<AppUpdateRelease?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads, verifies and stages the package; it is installed after the application exits.</summary>
    Task<StagedAppUpdate> DownloadAsync(AppUpdateRelease release, ApplicationOperationScope operation);
}
