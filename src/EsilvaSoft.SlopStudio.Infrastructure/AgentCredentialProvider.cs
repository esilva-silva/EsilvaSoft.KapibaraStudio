using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Resolves a credential only for the caller's immediate use; no value is cached or logged.</summary>
public sealed class AgentCredentialProvider(ISecretStore secrets) : IAgentCredentialProvider
{
    public Task<SecretStoreResult<string>> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        return secrets.GetAsync(reference, cancellationToken);
    }
}
