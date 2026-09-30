namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Resolved local workspace locations captured by the composition root.</summary>
public interface ILocalWorkspacePaths
{
    /// <summary>Gets the file opened by the single local database owner.</summary>
    string GetDatabasePath();
    /// <summary>Gets the models directory belonging to this workspace.</summary>
    string GetModelsDirectory();
    /// <summary>Gets the update staging directory belonging to this workspace.</summary>
    string GetUpdatesDirectory();
    /// <summary>Gets the exports directory belonging to this workspace.</summary>
    string GetExportsDirectory();
}
