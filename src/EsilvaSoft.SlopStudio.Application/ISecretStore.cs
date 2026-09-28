using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Application;

/// <summary>Stores provider credentials in an approved operating-system store.</summary>
/// <remarks>
/// A caller-cancelled token is reported with <see cref="OperationCanceledException"/>. A user dismissing a
/// backend unlock prompt is returned as <see cref="SecretStoreFailureCode.Cancelled"/> instead.
/// </remarks>
public interface ISecretStore
{
    Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default);

    Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default);

    Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default);

    Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default);
}
