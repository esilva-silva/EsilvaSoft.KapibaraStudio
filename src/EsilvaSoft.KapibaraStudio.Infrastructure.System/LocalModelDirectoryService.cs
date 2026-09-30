using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

public sealed class LocalModelDirectoryService : IModelDirectoryService
{
    public bool Exists(string path) => Directory.Exists(path);

    public string EnsureExists(string path) => Directory.CreateDirectory(path).FullName;
}
