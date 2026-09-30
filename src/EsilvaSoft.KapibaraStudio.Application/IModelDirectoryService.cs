namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Directory operations needed by the local model preferences, outside the UI.</summary>
public interface IModelDirectoryService
{
    bool Exists(string path);
    string EnsureExists(string path);
}
