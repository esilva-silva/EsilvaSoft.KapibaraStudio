using EsilvaSoft.KapibaraStudio.Application.Agents;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

public sealed class LocalAgentWorkspacePathProbe : IAgentWorkspacePathProbe
{
    public bool DirectoryExists(string fullPath) => Directory.Exists(fullPath);
    public bool FileExists(string fullPath) => File.Exists(fullPath);
    public bool TraversesLink(string fullPath, string? workspaceRoot) => workspaceRoot is null
        ? TraversesLinkFromVolumeRoot(fullPath) : TraversesLinkInside(fullPath, workspaceRoot);
    private static bool TraversesLinkInside(string fullPath, string root)
    {
        try
        {
            for (var current = fullPath; AgentWorkspacePaths.IsStrictlyInside(current, root); current = Path.GetDirectoryName(current) ?? root)
            {
                FileSystemInfo info = File.Exists(current) ? new FileInfo(current) : new DirectoryInfo(current);
                if (info.LinkTarget is not null)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }

    private static bool TraversesLinkFromVolumeRoot(string fullPath)
    {
        try
        {
            for (var current = fullPath; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current)!)
            {
                FileSystemInfo info = File.Exists(current) ? new FileInfo(current) : new DirectoryInfo(current);
                if (info.LinkTarget is not null)
                {
                    return true;
                }

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
                {
                    break;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return true;
        }
    }
}
