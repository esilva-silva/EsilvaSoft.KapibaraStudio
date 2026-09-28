namespace EsilvaSoft.KapibaraStudio.Application;

public interface IScriptFileService
{
    Task SaveAsync(string path, string script, CancellationToken cancellationToken = default);

    Task<string> LoadAsync(string path, CancellationToken cancellationToken = default);
}
