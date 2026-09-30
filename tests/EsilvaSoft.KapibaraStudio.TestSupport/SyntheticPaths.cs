namespace EsilvaSoft.KapibaraStudio.Testing;

/// <summary>Produces platform-shaped paths for tests that only compare or parse a path and must never touch disk.</summary>
internal static class SyntheticPaths
{
    private static readonly string Root = Path.DirectorySeparatorChar == '\\' ? @"C:\kapibara-tests" : "/kapibara-tests";

    public static string Combine(params string[] segments) => Path.Combine([Root, .. segments]);

    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path, Root));
}
