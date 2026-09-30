using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Read-only access to the broker proof in the current user's Windows Credential Manager.</summary>
/// <remarks>It shares the versioned target format used by the IDE secret store and never enumerates credentials.</remarks>
public sealed class WindowsClientTransportCredentialStore : IClientTransportCredentialStore
{
    private const string TargetPrefix = "EsilvaSoft.KapibaraStudio/secret/";
    private const int MaximumCredentialBlobBytes = 5 * 512;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly IWindowsCredentialManagerNative _native;
    private readonly Func<bool> _isWindows;

    public WindowsClientTransportCredentialStore() : this(new WindowsCredentialManagerNative(), OperatingSystem.IsWindows)
    {
    }

    internal WindowsClientTransportCredentialStore(IWindowsCredentialManagerNative native, Func<bool> isWindows)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _isWindows = isWindows ?? throw new ArgumentNullException(nameof(isWindows));
    }

    public Task<string?> ReadAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return Task.Run(() => Read(reference, cancellationToken), CancellationToken.None);
    }

    private string? Read(SecretReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isWindows()) return null;

        byte[]? bytes = null;
        var error = _native.Read($"{TargetPrefix}{reference.Id:N}/v{reference.Version}", out bytes);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (error != 0 || bytes is null || bytes.Length is 0 or > MaximumCredentialBlobBytes) return null;
            try
            {
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
