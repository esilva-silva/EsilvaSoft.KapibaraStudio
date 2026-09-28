using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.UnitTests;

internal sealed class InMemoryProfileSecretStore : ISecretStore
{
    private readonly object _gate = new();
    public Dictionary<SecretReference, string> Values { get; } = [];
    public bool DenySet { get; set; }
    public bool DenyDelete { get; set; }
    public bool WrongReadback { get; set; }
    public Func<Task>? AfterSet { get; set; }
    /// <summary>Runs after the value was looked up and before the result is returned.</summary>
    public Func<Task>? AfterGet { get; set; }

    public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.Available));

    public async Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SecretStoreResult<string> result;
        lock (_gate)
            result = Values.TryGetValue(reference, out var value)
                ? SecretStoreResults.Success(WrongReadback ? "different" : value)
                : SecretStoreResults.Failed<string>(SecretStoreFailureCode.NotFound);
        if (AfterGet is not null) await AfterGet();
        return result;
    }

    public async Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DenySet) return SecretStoreOperationResult.Failed(SecretStoreFailureCode.Denied);
        lock (_gate) Values[reference] = secret;
        if (AfterSet is not null) await AfterSet();
        return SecretStoreOperationResult.Success();
    }

    public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (DenyDelete) return Task.FromResult(SecretStoreOperationResult.Failed(SecretStoreFailureCode.Denied));
        lock (_gate) Values.Remove(reference);
        return Task.FromResult(SecretStoreOperationResult.Success());
    }

    public int Count { get { lock (_gate) return Values.Count; } }
}
