using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Filesystem boundary for locating the development checkout's log directory.</summary>
public sealed class LocalDiagnosticLogDirectoryResolver(string? baseDirectory = null) : IDiagnosticLogDirectoryResolver
{
    private readonly string _baseDirectory = baseDirectory ?? AppContext.BaseDirectory;

    /// <inheritdoc />
    public string Resolve()
    {
        for (var directory = new DirectoryInfo(_baseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EsilvaSoft.KapibaraStudio.slnx")))
                return Path.Combine(directory.FullName, "logs");
        }
        return Path.Combine(_baseDirectory, "logs");
    }
}
