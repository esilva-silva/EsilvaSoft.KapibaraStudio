using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Desktop.Agents;

/// <summary>
/// Production <see cref="IAgentApiKeyStore"/> (P7-L06-HOST). Writes and removes provider API keys only in the OS vault
/// (<see cref="ISecretStore"/>), in the opaque slot the composition root assigned to each provider ID — the same
/// <see cref="SecretReference"/> the adapter later resolves through <see cref="IAgentCredentialProvider"/>. It is
/// provider-neutral: the slot map is data supplied by the composition root, never a branch on a brand.
/// </summary>
/// <remarks>
/// The key is never returned, logged, cached or placed in an exception, status or LiteDB; there is no plaintext
/// fallback. Every internal copy of the caller's buffer is cleared before returning. Limitation: <see cref="ISecretStore"/>
/// takes a <see cref="string"/>, so one immutable string copy exists for the duration of the vault call and cannot be
/// zeroed (it is left to the GC); the caller still owns and clears its <c>char[]</c>.
/// </remarks>
public sealed class DesktopAgentApiKeyStore : IAgentApiKeyStore
{
    /// <summary>Same upper bound the adapters accept when resolving a key.</summary>
    public const int MaximumKeyLength = 1024;

    private readonly ISecretStore _secrets;
    private readonly Dictionary<string, SecretReference> _slots;

    public DesktopAgentApiKeyStore(ISecretStore secrets, IReadOnlyDictionary<string, SecretReference> slots)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(slots);
        _secrets = secrets;
        _slots = new Dictionary<string, SecretReference>(slots, StringComparer.Ordinal);
    }

    public async Task<AgentCredentialSetupOutcome> SaveApiKeyAsync(
        string providerId, char[] apiKey, CancellationToken cancellationToken)
    {
        if (providerId is null || !_slots.TryGetValue(providerId, out var slot))
        {
            return AgentCredentialSetupOutcome.UnknownProvider;
        }

        if (!IsAcceptable(apiKey))
        {
            return AgentCredentialSetupOutcome.Rejected;
        }

        cancellationToken.ThrowIfCancellationRequested();
        SecretStoreOperationResult result;
        try
        {
            result = await _secrets.SetAsync(slot, new string(apiKey), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // The exception text could carry vault internals; only the fixed outcome leaves this type.
            return AgentCredentialSetupOutcome.Failed;
        }

        return result.IsSuccess ? AgentCredentialSetupOutcome.Saved : Map(result.Failure!.Code);
    }

    public async Task<AgentCredentialSetupOutcome> RemoveApiKeyAsync(string providerId, CancellationToken cancellationToken)
    {
        if (providerId is null || !_slots.TryGetValue(providerId, out var slot))
        {
            return AgentCredentialSetupOutcome.UnknownProvider;
        }

        cancellationToken.ThrowIfCancellationRequested();
        SecretStoreOperationResult result;
        try
        {
            result = await _secrets.DeleteAsync(slot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AgentCredentialSetupOutcome.Failed;
        }

        // Removing a key that is already absent is the state the user asked for.
        return result.IsSuccess || result.Failure!.Code == SecretStoreFailureCode.NotFound
            ? AgentCredentialSetupOutcome.Removed
            : Map(result.Failure.Code);
    }

    public async Task<AgentProviderAuthState> GetApiKeyStateAsync(string providerId, CancellationToken cancellationToken)
    {
        if (providerId is null || !_slots.TryGetValue(providerId, out var slot))
        {
            return AgentProviderAuthState.Unknown;
        }

        SecretStoreResult<string> result;
        try
        {
            result = await _secrets.GetAsync(slot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AgentProviderAuthState.VaultUnavailable;
        }

        // Only presence is reported; the value is dropped immediately and never copied.
        return result.IsSuccess
            ? AgentProviderAuthState.Configured
            : result.Failure!.Code switch
            {
                SecretStoreFailureCode.NotFound => AgentProviderAuthState.NotConfigured,
                SecretStoreFailureCode.Corrupt => AgentProviderAuthState.Invalid,
                _ => AgentProviderAuthState.VaultUnavailable,
            };
    }

    /// <summary>Non-empty, bounded, and without whitespace or control characters (a pasted line break is refused).</summary>
    internal static bool IsAcceptable(char[]? apiKey)
    {
        if (apiKey is null || apiKey.Length is 0 or > MaximumKeyLength)
        {
            return false;
        }

        foreach (var character in apiKey)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character) || char.IsSurrogate(character))
            {
                return false;
            }
        }

        return true;
    }

    private static AgentCredentialSetupOutcome Map(SecretStoreFailureCode code) => code switch
    {
        SecretStoreFailureCode.Unavailable or SecretStoreFailureCode.Locked or SecretStoreFailureCode.Denied =>
            AgentCredentialSetupOutcome.VaultUnavailable,
        SecretStoreFailureCode.Cancelled => AgentCredentialSetupOutcome.Cancelled,
        _ => AgentCredentialSetupOutcome.Failed,
    };
}
