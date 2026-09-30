using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Installation coordinates captured before staging or replacing an update.</summary>
public record AppUpdateInstallation(AppUpdateAvailability Availability, AppVersion CurrentVersion, string Rid,
    string TargetDirectory, string ExecutableName, string UpdatesDirectory);

/// <summary>Owns the disk lifecycle of update packages, pending state and replacement.</summary>
public interface IAppUpdateStorage
{
    StagedAppUpdate? GetStagedUpdate(AppUpdateInstallation installation);
    IAppUpdateStaging BeginStaging(AppUpdateInstallation installation, AppUpdateRelease release, CancellationToken cancellationToken);
}

/// <summary>
/// Exclusive staging transaction. Disposing an uncommitted transaction discards its package and payload.
/// The consumer verifies the downloaded checksum before committing.
/// </summary>
public interface IAppUpdateStaging : IDisposable
{
    Stream CreatePackageStream();
    void Commit(CancellationToken cancellationToken);
}
