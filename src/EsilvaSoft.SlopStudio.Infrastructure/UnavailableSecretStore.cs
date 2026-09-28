using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Explicit typed failure when the current operating system has no approved secret backend.</summary>
internal sealed class UnavailableSecretStore : ISecretStore
{
    public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.UnsupportedPlatform));
    }

    public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SecretStoreResults.Failed<string>(SecretStoreFailureCode.Unavailable));
    }

    public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable));
    }

    public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable));
    }
}
