namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Mcp;

/// <summary>Owns an isolated folder and the single file path used by the LiteDB owner under test.</summary>
internal sealed class McpTemporaryWorkspace : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("slop-mcp-").FullName;

    public string DatabasePath => Path.Combine(_directory, "workspace.db");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
