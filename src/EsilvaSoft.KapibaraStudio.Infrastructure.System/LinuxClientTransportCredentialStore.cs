using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Reads only the versioned broker proof from the existing Linux Secret Service, without prompting.</summary>
/// <remarks>No CLI account credentials, files, fallback store, unlock, write or delete operations are accessed.</remarks>
public sealed class LinuxClientTransportCredentialStore : IClientTransportCredentialStore
{
    private readonly LinuxSecretServiceSecretStore _store;

    public LinuxClientTransportCredentialStore() : this(new LinuxSecretServiceSecretStore()) { }

    internal LinuxClientTransportCredentialStore(LinuxSecretServiceSecretStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<string?> ReadAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        var result = await _store.ReadWithoutPromptAsync(reference, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess && result.Value.Length > 0 ? result.Value : null;
    }
}
