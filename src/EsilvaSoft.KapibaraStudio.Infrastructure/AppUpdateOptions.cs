using EsilvaSoft.KapibaraStudio.SystemAdapters;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Where the running installation lives and whether it may replace itself.</summary>
public sealed record AppUpdateOptions(AppUpdateAvailability Availability, AppVersion CurrentVersion, string Rid, string TargetDirectory,
    string ExecutableName, string UpdatesDirectory, Uri ReleasesApi)
    : AppUpdateInstallation(Availability, CurrentVersion, Rid, TargetDirectory, ExecutableName, UpdatesDirectory)
{
    public static Uri GitHubReleasesApi { get; } = new("https://api.github.com/repos/esilva-silva/EsilvaSoft.KapibaraStudio/releases?per_page=20");

    public static AppUpdateOptions FromProcess(ILocalWorkspacePaths workspacePaths)
    {
        ArgumentNullException.ThrowIfNull(workspacePaths);
        var installation = LocalAppUpdateStorage.DetectInstallation(workspacePaths.GetUpdatesDirectory());
        return new(installation.Availability, installation.CurrentVersion, installation.Rid, installation.TargetDirectory,
            installation.ExecutableName, installation.UpdatesDirectory, GitHubReleasesApi);
    }
}
