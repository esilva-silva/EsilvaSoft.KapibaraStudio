namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Optional provider-owned deletion for session state persisted outside the application store.</summary>
public interface IAgentProviderSessionCleanup
{
    Task DeleteProviderSessionAsync(string providerSessionId, CancellationToken cancellationToken);
}
