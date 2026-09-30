using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Captures the platform data root once; resolution never creates directories or opens the database.</summary>
public sealed class LocalWorkspacePathResolver : ILocalWorkspacePaths
{
    private readonly LocalWorkspacePaths _paths;

    /// <summary>Resolves paths from the current host at the system boundary.</summary>
    public LocalWorkspacePathResolver() : this(OperatingSystem.IsWindows(),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    internal LocalWorkspacePathResolver(bool isWindows, string localApplicationData, string? xdgDataHome, string home)
    {
        var root = isWindows ? localApplicationData : string.IsNullOrEmpty(xdgDataHome)
            ? Path.Combine(home, ".local", "share") : xdgDataHome;
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new InvalidOperationException("Não foi possível resolver um diretório absoluto de dados para o workspace.");
        _paths = new LocalWorkspacePaths(Path.Combine(root, "EsilvaSoft", "KapibaraStudio", "workspace.db"));
    }

    /// <inheritdoc />
    public string GetDatabasePath() => _paths.GetDatabasePath();
    /// <inheritdoc />
    public string GetModelsDirectory() => _paths.GetModelsDirectory();
    /// <inheritdoc />
    public string GetUpdatesDirectory() => _paths.GetUpdatesDirectory();
    /// <inheritdoc />
    public string GetExportsDirectory() => _paths.GetExportsDirectory();
}
