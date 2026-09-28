namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Keeps connection passwords outside of the workspace database.</summary>
public interface IConnectionSecretStore
{
    void SetPassword(Guid profileId, string password);
    string? GetPassword(Guid profileId);
    void Remove(Guid profileId);
}
