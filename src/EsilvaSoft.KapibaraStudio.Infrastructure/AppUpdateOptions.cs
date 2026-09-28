using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure;

/// <summary>Where the running installation lives and whether it may replace itself.</summary>
public sealed record AppUpdateOptions(AppUpdateAvailability Availability, AppVersion CurrentVersion, string Rid, string TargetDirectory,
    string ExecutableName, string UpdatesDirectory, Uri ReleasesApi)
{
    public static Uri GitHubReleasesApi { get; } = new("https://api.github.com/repos/esilva-silva/EsilvaSoft.KapibaraStudio/releases?per_page=20");
    public static Uri LegacyGitHubReleasesApi { get; } = new("https://api.github.com/repos/esilva-silva/EsilvaSoft.SlopStudio/releases?per_page=20");
    public Uri? FallbackReleasesApi { get; init; }

    public static AppUpdateOptions FromProcess()
    {
        var processPath = Environment.ProcessPath;
        var informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = AppVersion.TryParse(informational, out var parsed) ? parsed : AppVersion.Parse("0.0.0-local");
        var rid = AppUpdateInstaller.CurrentRid();
        return new(AppUpdateInstaller.DetectAvailability(processPath, version, rid), version, rid ?? "",
            processPath is null ? AppContext.BaseDirectory : Path.GetDirectoryName(processPath)!,
            processPath is null ? "" : Path.GetFileName(processPath), LocalWorkspacePaths.GetUpdatesDirectory(), GitHubReleasesApi)
        { FallbackReleasesApi = LegacyGitHubReleasesApi };
    }
}
