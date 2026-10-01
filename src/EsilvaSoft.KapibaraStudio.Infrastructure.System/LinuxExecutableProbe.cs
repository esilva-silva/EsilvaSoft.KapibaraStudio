namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Executable selection with injectable metadata probes; never starts the candidate.</summary>
internal static class LinuxExecutableProbe
{
    internal static bool IsExecutable(string path, bool isLinux, Func<string, bool> exists,
        Func<string, UnixFileMode> mode)
    {
        if (!isLinux || !Path.IsPathFullyQualified(path)) return false;
        try
        {
            return exists(path) && (mode(path) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    internal static bool IsExecutable(string path) => IsExecutable(path, OperatingSystem.IsLinux(), File.Exists,
        candidate => OperatingSystem.IsLinux() ? File.GetUnixFileMode(candidate) : (UnixFileMode)0);
}
