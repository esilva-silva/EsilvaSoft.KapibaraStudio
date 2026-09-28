using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.McpServer;

/// <summary>
/// Reads one generic credential of the current user from Windows Credential Manager, using the target naming of the
/// IDE's <c>WindowsCredentialSecretStore</c> (<c>EsilvaSoft.SlopStudio/secret/{id:N}/v{version}</c>). Read-only.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsClientTransportCredentialStore : IClientTransportCredentialStore
{
    private const string TargetPrefix = "EsilvaSoft.SlopStudio/secret/";
    private const uint GenericCredentialType = 1;
    private const int MaximumCredentialBlobBytes = 5 * 512;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public Task<string?> ReadAsync(SecretReference reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return Task.Run(() => Read(reference, cancellationToken), CancellationToken.None);
    }

    private static string? Read(SecretReference reference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var target = $"{TargetPrefix}{reference.Id:N}/v{reference.Version}";
        if (!CredReadW(target, GenericCredentialType, 0, out var pointer)) return null;
        try
        {
            var credential = Marshal.PtrToStructure<NativeCredential>(pointer);
            if (credential.Type != GenericCredentialType || credential.CredentialBlobSize is 0 or > MaximumCredentialBlobBytes ||
                credential.CredentialBlob == IntPtr.Zero)
                return null;
            var bytes = new byte[(int)credential.CredentialBlobSize];
            try
            {
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return null;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
        finally
        {
            CredFree(pointer);
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
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string targetName, uint type, uint flags, out IntPtr credential);

    [DllImport("Advapi32.dll", EntryPoint = "CredFree")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void CredFree(IntPtr buffer);
}
