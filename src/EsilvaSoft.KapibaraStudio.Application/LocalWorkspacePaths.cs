namespace EsilvaSoft.KapibaraStudio.Application;

public static class LocalWorkspacePaths
{
    public static string GetModelsDirectory() => Path.Combine(Path.GetDirectoryName(GetDatabasePath())!, "Models");
    public static string GetUpdatesDirectory() => Path.Combine(Path.GetDirectoryName(GetDatabasePath())!, "updates");
    public static string GetDatabasePath()
    {
        var root = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("XDG_DATA_HOME")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local",
                    "share");

        return Path.Combine(root, "EsilvaSoft", "KapibaraStudio", "workspace.db");
    }

    public static string GetExportsDirectory()
    {
        var workspaceFile = GetDatabasePath();
        return Path.Combine(Path.GetDirectoryName(workspaceFile)!, "exports");
    }
}
