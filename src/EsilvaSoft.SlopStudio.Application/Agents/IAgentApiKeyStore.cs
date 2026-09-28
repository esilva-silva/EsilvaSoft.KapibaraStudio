using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Explicit write/removal of a provider API key in the OS vault (<see cref="ISecretStore"/>). It is the only write path
/// for provider keys; adapters read them through <see cref="IAgentCredentialProvider"/>. Implementations never return,
/// log or echo the key, never persist it outside the vault and have no plaintext fallback. The caller owns
/// <c>apiKey</c> and clears it after the call. Storing a key never consents to sending data.
/// </summary>
public interface IAgentApiKeyStore
{
    Task<AgentCredentialSetupOutcome> SaveApiKeyAsync(string providerId, char[] apiKey, CancellationToken cancellationToken);

    Task<AgentCredentialSetupOutcome> RemoveApiKeyAsync(string providerId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a key exists, without returning it. Reading may trigger a vault unlock prompt, so callers invoke it only
    /// on an explicit user action, never when merely listing providers.
    /// </summary>
    Task<AgentProviderAuthState> GetApiKeyStateAsync(string providerId, CancellationToken cancellationToken);
}
