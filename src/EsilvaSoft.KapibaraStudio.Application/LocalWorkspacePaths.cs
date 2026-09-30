namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Pure path layout for an explicitly supplied workspace database; never consults the host.</summary>
public sealed class LocalWorkspacePaths : ILocalWorkspacePaths
{
    private readonly string _databasePath;
    private readonly string _dataDirectory;

    /// <summary>Creates the layout from a resolved database path.</summary>
    /// <param name="databasePath">Database location captured by the composition root.</param>
    public LocalWorkspacePaths(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _dataDirectory = Path.GetDirectoryName(databasePath)
            ?? throw new ArgumentException("O workspace precisa declarar seu diretório de dados.", nameof(databasePath));
        if (string.IsNullOrWhiteSpace(_dataDirectory))
            throw new ArgumentException("O workspace precisa declarar seu diretório de dados.", nameof(databasePath));
        _databasePath = databasePath;
    }

    /// <inheritdoc />
    public string GetModelsDirectory() => Path.Combine(_dataDirectory, "Models");
    /// <inheritdoc />
    public string GetUpdatesDirectory() => Path.Combine(_dataDirectory, "updates");
    /// <inheritdoc />
    public string GetDatabasePath() => _databasePath;
    /// <inheritdoc />
    public string GetExportsDirectory() => Path.Combine(_dataDirectory, "exports");
}
