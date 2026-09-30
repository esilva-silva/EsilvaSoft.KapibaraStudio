namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Exclusive test directory with cleanup restricted to its fixed synthetic parent.</summary>
internal sealed class SyntheticDirectory : IDisposable
{
    private readonly string _parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "kapibara-integration"));
    public string Path { get; }

    public SyntheticDirectory()
    {
        Path = System.IO.Path.Combine(_parent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        var normalized = System.IO.Path.GetFullPath(Path);
        if (!string.Equals(System.IO.Path.GetDirectoryName(normalized), _parent,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("A limpeza deve ficar na pasta sintética de integração.");
        if (Directory.Exists(normalized)) Directory.Delete(normalized, recursive: true);
    }
}
