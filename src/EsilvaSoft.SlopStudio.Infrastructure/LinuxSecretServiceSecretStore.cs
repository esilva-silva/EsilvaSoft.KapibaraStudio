using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;
using Tmds.DBus.Protocol;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Provider credentials in the existing Linux session Secret Service.</summary>
/// <remarks>
/// Each operation owns its connection and encrypted session. No file or memory fallback is selected.
/// A caller cancellation closes that connection; a remote write may already have committed.
/// Set replaces the same reference; Delete returns NotFound when already absent, like Windows.
/// </remarks>
public sealed class LinuxSecretServiceSecretStore : ISecretStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly Func<CancellationToken, ISecretServiceWire> _connect;
    private readonly Func<bool> _isLinux;
    private readonly TimeSpan _timeout;

    public LinuxSecretServiceSecretStore() : this(token => new SecretServiceDbusWire(token), OperatingSystem.IsLinux, TimeSpan.FromMinutes(2)) { }

    internal LinuxSecretServiceSecretStore(Func<CancellationToken, ISecretServiceWire> connect, Func<bool> isLinux, TimeSpan timeout)
    {
        _connect = connect;
        _isLinux = isLinux;
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        _timeout = timeout;
    }

    public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return !_isLinux()
            ? Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.UnsupportedPlatform))
            : RunAsync(async (wire, token) =>
            {
                // A read-only probe never unlocks or creates an item/collection.
                await wire.ReadDefaultCollectionAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return SecretStoreAvailability.Available;
            }, cancellationToken);
    }

    public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return RunAsync(async (wire, token) =>
        {
            var item = await FindItemAsync(wire, reference).ConfigureAwait(false)
                ?? throw new SecretServiceException(SecretStoreFailureCode.NotFound);
            await EnsureUnlockedAsync(wire, item, collection: false).ConfigureAwait(false);
            using var crypto = new SecretServiceCryptography();
            var session = await OpenSessionAsync(wire, crypto).ConfigureAwait(false);
            using var secret = await wire.GetSecretAsync(item, session).ConfigureAwait(false);
            var plaintext = crypto.Decrypt(session, secret);
            try
            {
                token.ThrowIfCancellationRequested();
                if (plaintext.Length > SecretServiceCryptography.MaximumSecretBytes)
                {
                    throw new SecretServiceException(SecretStoreFailureCode.Corrupt);
                }

                return StrictUtf8.GetString(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }, cancellationToken);
    }

    public async Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(secret);
        cancellationToken.ThrowIfCancellationRequested();
        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(secret);
        }
        catch (EncoderFallbackException)
        {
            // Encoder exception text includes offending characters; do not propagate it.
            return SecretStoreOperationResult.Failed(SecretStoreFailureCode.Corrupt);
        }

        if (byteCount > SecretServiceCryptography.MaximumSecretBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(secret), "O cofre aceita até 2.560 bytes por credencial de provider.");
        }

        var result = await RunAsync(async (wire, token) =>
        {
            var item = await FindItemAsync(wire, reference).ConfigureAwait(false);
            var collection = item is null ? await wire.ReadDefaultCollectionAsync().ConfigureAwait(false) : null;
            if (collection == "/")
            {
                // The user must configure a keyring. Do not silently create a new unprotected collection.
                throw new SecretServiceException(SecretStoreFailureCode.NotFound);
            }

            await EnsureUnlockedAsync(wire, item ?? collection!, collection: item is null).ConfigureAwait(false);
            using var crypto = new SecretServiceCryptography();
            var session = await OpenSessionAsync(wire, crypto).ConfigureAwait(false);
            var plaintext = StrictUtf8.GetBytes(secret);
            try
            {
                token.ThrowIfCancellationRequested();
                using var encrypted = crypto.Encrypt(session, plaintext);
                // Plaintext never enters the D-Bus library's pooled messages.
                CryptographicOperations.ZeroMemory(plaintext);
                if (item is not null)
                {
                    await wire.SetSecretAsync(item, encrypted).ConfigureAwait(false);
                }
                else
                {
                    var created = await wire.CreateItemAsync(collection!, Attributes(reference), encrypted).ConfigureAwait(false);
                    var createdItem = created.Item;
                    if (created.Prompt != "/")
                    {
                        var completed = await CompletePromptAsync(wire, created.Prompt).ConfigureAwait(false);
                        createdItem = completed.Item;
                    }

                    if (string.IsNullOrEmpty(createdItem) || createdItem == "/")
                    {
                        throw new SecretServiceException(SecretStoreFailureCode.Corrupt);
                    }
                }

                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }, cancellationToken).ConfigureAwait(false);
        return ToOperation(result);
    }

    public async Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var result = await RunAsync(async (wire, _) =>
        {
            var item = await FindItemAsync(wire, reference).ConfigureAwait(false)
                ?? throw new SecretServiceException(SecretStoreFailureCode.NotFound);
            // Delete can itself require a prompt; an unnecessary unlock would prompt twice.
            var prompt = await wire.DeleteAsync(item).ConfigureAwait(false);
            if (prompt != "/")
            {
                await CompletePromptAsync(wire, prompt).ConfigureAwait(false);
            }

            return true;
        }, cancellationToken).ConfigureAwait(false);
        return ToOperation(result);
    }

    private async Task<SecretStoreResult<T>> RunAsync<T>(Func<ISecretServiceWire, CancellationToken, Task<T>> operation, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        if (!_isLinux())
        {
            return SecretStoreResults.Failed<T>(SecretStoreFailureCode.Unavailable);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(caller);
        deadline.CancelAfter(_timeout);
        try
        {
            await using var wire = _connect(deadline.Token);
            await wire.ConnectAsync().ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            var value = await operation(wire, deadline.Token).ConfigureAwait(false);
            deadline.Token.ThrowIfCancellationRequested();
            return SecretStoreResults.Success(value);
        }
        catch (Exception exception) when (IsBackendException(exception))
        {
            // Never attach a native exception as InnerException: its text can contain remote values.
            caller.ThrowIfCancellationRequested();
            return SecretStoreResults.Failed<T>(deadline.IsCancellationRequested
                ? SecretStoreFailureCode.Unavailable : MapFailure(exception));
        }
    }

    private static bool IsBackendException(Exception exception) => exception is SecretServiceException or DBusExceptionBase or
        OperationCanceledException or ObjectDisposedException or IOException or CryptographicException or ArgumentException or
        InvalidOperationException or System.Net.Sockets.SocketException;

    internal static SecretStoreFailureCode MapFailure(Exception exception) => exception switch
    {
        SecretServiceException error => error.Code,
        DBusErrorReplyException error => error.ErrorName switch
        {
            "org.freedesktop.Secret.Error.IsLocked" => SecretStoreFailureCode.Locked,
            "org.freedesktop.Secret.Error.NoSuchObject" or "org.freedesktop.DBus.Error.UnknownObject" => SecretStoreFailureCode.NotFound,
            "org.freedesktop.DBus.Error.AccessDenied" or "org.freedesktop.DBus.Error.AuthFailed" => SecretStoreFailureCode.Denied,
            "org.freedesktop.DBus.Error.InvalidArgs" => SecretStoreFailureCode.Corrupt,
            _ => SecretStoreFailureCode.Unavailable
        },
        CryptographicException or ArgumentException or InvalidOperationException => SecretStoreFailureCode.Corrupt,
        _ => SecretStoreFailureCode.Unavailable
    };

    private static SecretStoreOperationResult ToOperation(SecretStoreResult<bool> result) => result.IsSuccess
        ? SecretStoreOperationResult.Success() : SecretStoreOperationResult.Failed(result.Failure!.Code);

    internal static IReadOnlyDictionary<string, string> Attributes(SecretReference reference) => new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["application"] = "EsilvaSoft.SlopStudio",
        ["scope"] = "agent-provider",
        ["reference"] = reference.Id.ToString("N"),
        ["version"] = reference.Version.ToString(CultureInfo.InvariantCulture)
    };

    private static async Task<string?> FindItemAsync(ISecretServiceWire wire, SecretReference reference)
    {
        var found = await wire.SearchAsync(Attributes(reference)).ConfigureAwait(false);
        // An ambiguous reference must never select or delete an arbitrary item.
        var items = found.Unlocked.Concat(found.Locked).Distinct(StringComparer.Ordinal).ToArray();
        return items.Length switch
        {
            0 => null,
            1 when items[0] != "/" => items[0],
            _ => throw new SecretServiceException(SecretStoreFailureCode.Corrupt)
        };
    }

    private static async Task<string> OpenSessionAsync(ISecretServiceWire wire, SecretServiceCryptography crypto)
    {
        var session = await wire.OpenSessionAsync(crypto.PublicKey).ConfigureAwait(false);
        if (session.Path == "/")
        {
            throw new SecretServiceException(SecretStoreFailureCode.Corrupt);
        }

        crypto.Establish(session.PublicKey);
        return session.Path;
    }

    private static async Task EnsureUnlockedAsync(ISecretServiceWire wire, string path, bool collection)
    {
        if (!await wire.IsLockedAsync(path, collection).ConfigureAwait(false))
        {
            return;
        }

        var unlock = await wire.UnlockAsync(path).ConfigureAwait(false);
        if (unlock.Prompt != "/")
        {
            var completed = await CompletePromptAsync(wire, unlock.Prompt).ConfigureAwait(false);
            if (completed.Unlocked is null)
            {
                throw new SecretServiceException(SecretStoreFailureCode.Corrupt);
            }
        }

        // The property is authoritative even when the result contains a parent collection instead.
        // Relock is returned to the caller; never loop through prompts automatically.
        if (await wire.IsLockedAsync(path, collection).ConfigureAwait(false))
        {
            throw new SecretServiceException(SecretStoreFailureCode.Locked);
        }
    }

    private static async Task<SecretServicePromptResult> CompletePromptAsync(ISecretServiceWire wire, string path)
    {
        var completed = await wire.PromptAsync(path).ConfigureAwait(false);
        if (completed.Dismissed)
        {
            throw new SecretServiceException(SecretStoreFailureCode.Cancelled);
        }

        return completed;
    }
}
