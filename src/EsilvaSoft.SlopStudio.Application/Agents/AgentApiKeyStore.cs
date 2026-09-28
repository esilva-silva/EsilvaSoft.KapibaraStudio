using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// <see cref="IAgentApiKeyStore"/> over the OS vault. Each provider has a fixed opaque slot supplied by trusted
/// composition (the same <see cref="SecretReference"/> its adapter resolves); unknown providers are refused. Writes and
/// removals are serialized so an interleaved save/remove cannot leave an ambiguous report. The key only becomes a string
/// at the vault boundary required by <see cref="ISecretStore"/>; it is never logged, returned or kept in a field.
/// </summary>
public sealed class AgentApiKeyStore : IAgentApiKeyStore, IDisposable
{
    public const int MaximumApiKeyLength = 512;

    private readonly ISecretStore _store;
    private readonly Dictionary<string, SecretReference> _slots;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public AgentApiKeyStore(ISecretStore store, IReadOnlyDictionary<string, SecretReference> slots)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(slots);
        _store = store;
        _slots = new Dictionary<string, SecretReference>(StringComparer.Ordinal);
        foreach (var (providerId, reference) in slots)
        {
            if (!AgentProviderDescriptor.IsValidProviderId(providerId) || reference is null)
            {
                throw new ArgumentException("API-key slot is invalid.", nameof(slots));
            }

            _slots.Add(providerId, reference);
        }

        if (_slots.Values.Distinct().Count() != _slots.Count)
        {
            throw new ArgumentException("Providers cannot share an API-key slot.", nameof(slots));
        }
    }

    public async Task<AgentCredentialSetupOutcome> SaveApiKeyAsync(
        string providerId, char[] apiKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        if (providerId is null || !_slots.TryGetValue(providerId, out var reference))
        {
            return AgentCredentialSetupOutcome.UnknownProvider;
        }

        if (!IsAcceptableKey(apiKey))
        {
            return AgentCredentialSetupOutcome.Rejected;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _store.SetAsync(reference, new string(apiKey), cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? AgentCredentialSetupOutcome.Saved : MapWriteFailure(result.Failure!.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Exception text could carry vault internals; only a fixed outcome leaves.
            return AgentCredentialSetupOutcome.Failed;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<AgentCredentialSetupOutcome> RemoveApiKeyAsync(string providerId, CancellationToken cancellationToken)
    {
        if (providerId is null || !_slots.TryGetValue(providerId, out var reference))
        {
            return AgentCredentialSetupOutcome.UnknownProvider;
        }

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await _store.DeleteAsync(reference, cancellationToken).ConfigureAwait(false);
            // Removing an absent key is idempotent: the slot ends empty either way.
            return result.IsSuccess || result.Failure!.Code == SecretStoreFailureCode.NotFound
                ? AgentCredentialSetupOutcome.Removed
                : MapWriteFailure(result.Failure.Code);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AgentCredentialSetupOutcome.Failed;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task<AgentProviderAuthState> GetApiKeyStateAsync(string providerId, CancellationToken cancellationToken)
    {
        if (providerId is null || !_slots.TryGetValue(providerId, out var reference))
        {
            return AgentProviderAuthState.Unknown;
        }

        try
        {
            // The value is discarded at once; only its presence is reported.
            var result = await _store.GetAsync(reference, cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                return AgentProviderAuthState.Configured;
            }

            return result.Failure!.Code switch
            {
                SecretStoreFailureCode.NotFound => AgentProviderAuthState.NotConfigured,
                SecretStoreFailureCode.Corrupt => AgentProviderAuthState.Invalid,
                SecretStoreFailureCode.Cancelled => AgentProviderAuthState.Unknown,
                _ => AgentProviderAuthState.VaultUnavailable,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AgentProviderAuthState.VaultUnavailable;
        }
    }

    public void Dispose() => _writeGate.Dispose();

    /// <summary>Printable, non-whitespace, bounded. Nothing is trimmed silently: a pasted newline is rejected.</summary>
    public static bool IsAcceptableKey(ReadOnlySpan<char> apiKey)
    {
        if (apiKey.IsEmpty || apiKey.Length > MaximumApiKeyLength)
        {
            return false;
        }

        foreach (var c in apiKey)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c) || char.IsSurrogate(c))
            {
                return false;
            }
        }

        return true;
    }

    private static AgentCredentialSetupOutcome MapWriteFailure(SecretStoreFailureCode code) => code switch
    {
        SecretStoreFailureCode.Cancelled => AgentCredentialSetupOutcome.Cancelled,
        SecretStoreFailureCode.Unavailable or SecretStoreFailureCode.Locked or SecretStoreFailureCode.Denied =>
            AgentCredentialSetupOutcome.VaultUnavailable,
        _ => AgentCredentialSetupOutcome.Failed,
    };
}
