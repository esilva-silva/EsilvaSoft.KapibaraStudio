using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Infrastructure;

/// <summary>Windows Credential Manager adapter for generic credentials scoped to the current user.</summary>
public sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const int MaximumCredentialBlobBytes = 5 * 512;
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidData = 13;
    private const int ErrorNotFound = 1168;
    private const int ErrorNoSuchLogonSession = 1312;
    private const string TargetPrefix = "EsilvaSoft.SlopStudio/secret/";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly IWindowsCredentialManagerNative _native;
    private readonly Func<bool> _isWindows;

    public WindowsCredentialSecretStore() : this(new WindowsCredentialManagerNative(), OperatingSystem.IsWindows)
    {
    }

    internal WindowsCredentialSecretStore(IWindowsCredentialManagerNative native, Func<bool> isWindows)
    {
        _native = native ?? throw new ArgumentNullException(nameof(native));
        _isWindows = isWindows ?? throw new ArgumentNullException(nameof(isWindows));
    }

    /// <summary>
    /// Reads a target with a fresh nonce. Available means Credential Manager answered with not-found;
    /// it does not prove that writes are permitted or that any existing credential is unlocked.
    /// </summary>
    public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => GetAvailability(cancellationToken), CancellationToken.None);

    public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return Task.Run(() => Get(reference, cancellationToken), CancellationToken.None);
    }

    public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(secret);
        return Task.Run(() => Set(reference, secret, cancellationToken), CancellationToken.None);
    }

    public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return Task.Run(() => Delete(reference, cancellationToken), CancellationToken.None);
    }

    private SecretStoreResult<SecretStoreAvailability> GetAvailability(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isWindows())
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SecretStoreResults.Success(SecretStoreAvailability.UnsupportedPlatform);
        }

        var target = TargetPrefix + "probe/" + Guid.NewGuid().ToString("N");
        byte[]? bytes = null;
        var error = InvokeNative(() => _native.Read(target, out bytes));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (error == ErrorNotFound)
            {
                return SecretStoreResults.Success(SecretStoreAvailability.Available);
            }

            if (error == 0)
            {
                // A nonce collision must never expose or alter an existing credential.
                return SecretStoreResults.Failed<SecretStoreAvailability>(SecretStoreFailureCode.Unavailable);
            }

            return AvailabilityFailure(error);
        }
        finally
        {
            if (bytes is not null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    private SecretStoreResult<string> Get(SecretReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isWindows())
        {
            return SecretStoreResults.Failed<string>(SecretStoreFailureCode.Unavailable);
        }

        byte[]? bytes = null;
        var error = InvokeNative(() => _native.Read(GetTargetName(reference), out bytes));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (error != 0)
            {
                return SecretStoreResults.Failed<string>(MapFailure(error));
            }

            if (bytes is null || bytes.Length > MaximumCredentialBlobBytes)
            {
                return SecretStoreResults.Failed<string>(SecretStoreFailureCode.Corrupt);
            }

            try
            {
                return SecretStoreResults.Success(StrictUtf8.GetString(bytes));
            }
            catch (DecoderFallbackException)
            {
                return SecretStoreResults.Failed<string>(SecretStoreFailureCode.Corrupt);
            }
        }
        finally
        {
            if (bytes is not null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    private SecretStoreOperationResult Set(SecretReference reference, string secret, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isWindows())
        {
            return SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable);
        }

        int byteCount;
        try
        {
            byteCount = StrictUtf8.GetByteCount(secret);
        }
        catch (EncoderFallbackException)
        {
            // Exception details can include malformed secret input.
            return SecretStoreOperationResult.Failed(SecretStoreFailureCode.Corrupt);
        }

        if (byteCount > MaximumCredentialBlobBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(secret), "O Credential Manager aceita até 2.560 bytes por segredo genérico.");
        }

        var bytes = new byte[byteCount];
        try
        {
            try
            {
                StrictUtf8.GetBytes(secret.AsSpan(), bytes);
            }
            catch (EncoderFallbackException)
            {
                return SecretStoreOperationResult.Failed(SecretStoreFailureCode.Corrupt);
            }

            var error = InvokeNative(() => _native.Write(GetTargetName(reference), bytes));
            cancellationToken.ThrowIfCancellationRequested();
            return error == 0
                ? SecretStoreOperationResult.Success()
                : SecretStoreOperationResult.Failed(MapFailure(error));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private SecretStoreOperationResult Delete(SecretReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isWindows())
        {
            return SecretStoreOperationResult.Failed(SecretStoreFailureCode.Unavailable);
        }

        var error = InvokeNative(() => _native.Delete(GetTargetName(reference)));
        cancellationToken.ThrowIfCancellationRequested();
        return error == 0
            ? SecretStoreOperationResult.Success()
            : SecretStoreOperationResult.Failed(MapFailure(error));
    }

    private static string GetTargetName(SecretReference reference) =>
        $"{TargetPrefix}{reference.Id:N}/v{reference.Version}";

    private static SecretStoreFailureCode MapFailure(int error) => error switch
    {
        ErrorNotFound => SecretStoreFailureCode.NotFound,
        ErrorAccessDenied => SecretStoreFailureCode.Denied,
        ErrorNoSuchLogonSession => SecretStoreFailureCode.Unavailable,
        ErrorInvalidData => SecretStoreFailureCode.Corrupt,
        _ => SecretStoreFailureCode.Unavailable
    };

    private static SecretStoreResult<SecretStoreAvailability> AvailabilityFailure(int error) =>
        error == ErrorNoSuchLogonSession
            ? SecretStoreResults.Success(SecretStoreAvailability.Unavailable)
            : SecretStoreResults.Failed<SecretStoreAvailability>(MapFailure(error));

    private static int InvokeNative(Func<int> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or
                                           BadImageFormatException or MarshalDirectiveException or ExternalException or
                                           ArgumentException or OverflowException)
        {
            // Do not propagate native-loader or interop exception text to application callers.
            return exception switch
            {
                ExternalException external when external.ErrorCode != 0 => external.ErrorCode,
                ArgumentException or OverflowException => ErrorInvalidData,
                _ => ErrorNoSuchLogonSession
            };
        }
    }
}

internal interface IWindowsCredentialManagerNative
{
    int Read(string targetName, out byte[]? secret);
    int Write(string targetName, byte[] secret);
    int Delete(string targetName);
}

internal sealed class WindowsCredentialManagerNative : IWindowsCredentialManagerNative
{
    private const uint GenericCredentialType = 1;
    private const uint LocalMachinePersistence = 2;
    private const int ErrorInvalidData = 13;
    private const int ErrorNotFound = 1168;
    private const int MaximumCredentialBlobBytes = 5 * 512;

    public int Read(string targetName, out byte[]? secret)
    {
        secret = null;
        if (!CredReadW(targetName, GenericCredentialType, 0, out var credentialPointer))
        {
            return Marshal.GetLastWin32Error();
        }

        NativeCredential credential = default;
        var credentialRead = false;
        try
        {
            credential = Marshal.PtrToStructure<NativeCredential>(credentialPointer);
            credentialRead = true;
            if (credential.Type != GenericCredentialType || credential.CredentialBlobSize > MaximumCredentialBlobBytes ||
                (credential.CredentialBlobSize > 0 && credential.CredentialBlob == IntPtr.Zero))
            {
                return ErrorInvalidData;
            }

            var bytes = new byte[checked((int)credential.CredentialBlobSize)];
            try
            {
                if (bytes.Length > 0)
                {
                    Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                }

                secret = bytes;
                return 0;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(bytes);
                throw;
            }
        }
        finally
        {
            if (credentialRead && credential.CredentialBlob != IntPtr.Zero &&
                credential.CredentialBlobSize <= MaximumCredentialBlobBytes)
            {
                ZeroNativeBuffer(credential.CredentialBlob, (int)credential.CredentialBlobSize);
            }

            if (credentialPointer != IntPtr.Zero)
            {
                CredFree(credentialPointer);
            }
        }
    }

    public int Write(string targetName, byte[] secret)
    {
        IntPtr targetPointer = IntPtr.Zero;
        IntPtr blobPointer = IntPtr.Zero;
        try
        {
            targetPointer = Marshal.StringToHGlobalUni(targetName);
            if (secret.Length > 0)
            {
                blobPointer = Marshal.AllocHGlobal(secret.Length);
                Marshal.Copy(secret, 0, blobPointer, secret.Length);
            }

            var credential = new NativeCredential
            {
                Type = GenericCredentialType,
                TargetName = targetPointer,
                CredentialBlobSize = checked((uint)secret.Length),
                CredentialBlob = blobPointer,
                Persist = LocalMachinePersistence
            };

            return CredWriteW(ref credential, 0) ? 0 : Marshal.GetLastWin32Error();
        }
        finally
        {
            ZeroAndFree(blobPointer, secret.Length);
            if (targetPointer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(targetPointer);
            }
        }
    }

    public int Delete(string targetName) => CredDeleteW(targetName, GenericCredentialType, 0)
        ? 0
        : Marshal.GetLastWin32Error();

    private static void ZeroAndFree(IntPtr pointer, int length)
    {
        if (pointer == IntPtr.Zero)
        {
            return;
        }

        ZeroNativeBuffer(pointer, length);
        Marshal.FreeHGlobal(pointer);
    }

    private static void ZeroNativeBuffer(IntPtr pointer, int length)
    {
        for (var index = 0; index < length; index++)
        {
            Marshal.WriteByte(pointer, index, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("Advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string targetName, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref NativeCredential credential, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string targetName, uint type, uint flags);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}
