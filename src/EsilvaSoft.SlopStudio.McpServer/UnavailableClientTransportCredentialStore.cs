using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.McpServer;

/// <summary>
/// Platforms without a homologated proxy-side reader (Linux Secret Service is still pending): no proof, so the broker
/// is never contacted with a credential and every data call reports <c>AuthenticationRequired</c>. No plaintext fallback.
/// </summary>
internal sealed class UnavailableClientTransportCredentialStore : IClientTransportCredentialStore
{
    public Task<string?> ReadAsync(SecretReference reference, CancellationToken cancellationToken) =>
        Task.FromResult<string?>(null);
}
