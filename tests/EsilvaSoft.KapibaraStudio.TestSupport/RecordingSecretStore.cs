using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Testing;

    /// <summary>Vault double that records calls and can fail with a typed code or an exception.</summary>
internal sealed class RecordingSecretStore : ISecretStore
    {
        public Dictionary<SecretReference, string> Values { get; } = [];

        public int Calls { get; private set; }

        public List<SecretReference> Reads { get; } = [];

        public SecretStoreFailureCode? Failure { get; set; }

        public Exception? Throw { get; set; }

        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.Available));

        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            Calls++;
            Reads.Add(reference);
            cancellationToken.ThrowIfCancellationRequested();
            if (Throw is { } exception) return Task.FromException<SecretStoreResult<string>>(exception);
            return Task.FromResult(Values.TryGetValue(reference, out var value)
                ? SecretStoreResults.Success(value)
                : SecretStoreResults.Failed<string>(SecretStoreFailureCode.NotFound));
        }

        public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Throw is { } exception) return Task.FromException<SecretStoreOperationResult>(exception);
            if (Failure is { } failure) return Task.FromResult(SecretStoreOperationResult.Failed(failure));
            Values[reference] = secret;
            return Task.FromResult(SecretStoreOperationResult.Success());
        }

        public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            if (Throw is { } exception) return Task.FromException<SecretStoreOperationResult>(exception);
            return Task.FromResult(Values.Remove(reference)
                ? SecretStoreOperationResult.Success()
                : SecretStoreOperationResult.Failed(SecretStoreFailureCode.NotFound));
        }
    }
