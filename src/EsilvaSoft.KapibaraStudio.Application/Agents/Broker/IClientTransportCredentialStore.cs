using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents.Broker;

/// <summary>Read-only access to the current client's IPC proof.</summary>
/// <remarks>The proof is read only after broker version negotiation and is never accepted from argv or configuration.</remarks>
public interface IClientTransportCredentialStore
{
    /// <returns>The proof, or <see langword="null"/> when the store is unavailable or the entry is missing.</returns>
    Task<string?> ReadAsync(SecretReference reference, CancellationToken cancellationToken);
}
