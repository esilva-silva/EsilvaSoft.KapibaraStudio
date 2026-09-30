using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Fails closed when no approved client-side proof reader is available.</summary>
public sealed class UnavailableClientTransportCredentialStore : IClientTransportCredentialStore
{
    public Task<string?> ReadAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<string?>(null);
    }
}
