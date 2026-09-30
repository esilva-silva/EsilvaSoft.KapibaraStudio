using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

internal sealed class RefusingMongoDatabaseExportFileAccess : IMongoDatabaseExportFileAccess
{
    public int Calls { get; private set; }
    public string NormalizePath(string path) => throw Requested();
    public string CreateExportDirectory(string directoryName) => throw Requested();
    public bool DirectoryExists(string path) => throw Requested();
    public bool FileExists(string path) => throw Requested();
    public Stream CreateNewFile(string path) => throw Requested();
    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default) => throw Requested();
    public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default) => throw Requested();

    private AssertionException Requested()
    {
        Calls++;
        return new AssertionException("Validation must not access export or import files.");
    }
}
