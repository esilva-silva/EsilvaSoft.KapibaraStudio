using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SlopStudio.Spikes.WindowsCredentials
{
    public sealed class ProbeResult
    {
        public string Scenario { get; set; }
        public string SyntheticTarget { get; set; }
        public string Status { get; set; } = "NotStarted";
        public int NativeError { get; set; }
        public bool Written { get; set; }
        public bool ReadBackMatches { get; set; }
        public bool MetadataMatches { get; set; }
        public bool Deleted { get; set; }
        public bool ConfirmedAbsent { get; set; }
        public int AbsenceNativeError { get; set; }
        public string CleanupStatus { get; set; } = "NotRequired";
        public int CleanupNativeError { get; set; }
    }

    public static class CredentialManagerProbe
    {
        private const uint Generic = 1;
        private const uint LocalMachinePersistence = 2; // Mesmo usuário nesta máquina, não todos os usuários.
        private const int NotFound = 1168;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Credential
        {
            public uint Flags;
            public uint Type;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
            [MarshalAs(UnmanagedType.LPWStr)] public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            [MarshalAs(UnmanagedType.LPWStr)] public string TargetAlias;
            [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredWrite(ref Credential credential, uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CredDelete(string target, uint type, uint flags);

        [DllImport("advapi32.dll", ExactSpelling = true)]
        private static extern void CredFree(IntPtr buffer);

        // Nenhum target, valor, conta ou credencial externa é aceito como argumento.
        public static ProbeResult Run(string scenario)
        {
            if (scenario != "Success" && scenario != "FailureAfterWrite" && scenario != "FailureAfterRead")
                throw new ArgumentException("Cenário de teste inválido.", nameof(scenario));

            var result = new ProbeResult
            {
                Scenario = scenario,
                SyntheticTarget = "EsilvaSoft.SlopStudio.Spike.Phase07.Synthetic/" + Guid.NewGuid().ToString("N")
            };
            byte[] marker = RandomNumberGenerator.GetBytes(32);
            IntPtr unmanagedMarker = IntPtr.Zero;
            try
            {
                unmanagedMarker = Marshal.AllocHGlobal(marker.Length);
                Marshal.Copy(marker, 0, unmanagedMarker, marker.Length);
                var credential = new Credential
                {
                    Type = Generic,
                    TargetName = result.SyntheticTarget,
                    Comment = "Valor sintético temporário do spike Fase 7; sem credencial real.",
                    CredentialBlobSize = (uint)marker.Length,
                    CredentialBlob = unmanagedMarker,
                    Persist = LocalMachinePersistence,
                    UserName = "synthetic-spike-only"
                };

                if (!CredWrite(ref credential, 0))
                {
                    result.NativeError = Marshal.GetLastWin32Error();
                    result.Status = Sanitize(result.NativeError);
                    return result;
                }
                result.Written = true;
                if (scenario == "FailureAfterWrite") throw new InjectedFailureException();

                // A primeira leitura ocorre somente após gravar nosso target aleatório com sucesso.
                if (!CredRead(result.SyntheticTarget, Generic, 0, out var readPointer))
                {
                    result.NativeError = Marshal.GetLastWin32Error();
                    result.Status = Sanitize(result.NativeError);
                    return result;
                }
                try
                {
                    var read = Marshal.PtrToStructure<Credential>(readPointer);
                    result.MetadataMatches = read.Type == Generic && read.Persist == LocalMachinePersistence
                        && read.TargetName == result.SyntheticTarget && read.UserName == "synthetic-spike-only";
                    if (read.CredentialBlobSize != marker.Length || read.CredentialBlob == IntPtr.Zero)
                        throw new InvalidOperationException();
                    byte[] recovered = new byte[marker.Length];
                    try
                    {
                        Marshal.Copy(read.CredentialBlob, recovered, 0, recovered.Length);
                        result.ReadBackMatches = CryptographicOperations.FixedTimeEquals(marker, recovered);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(recovered);
                        ZeroUnmanaged(read.CredentialBlob, marker.Length);
                    }
                }
                finally { CredFree(readPointer); }

                if (!result.ReadBackMatches || !result.MetadataMatches) throw new InvalidOperationException();
                if (scenario == "FailureAfterRead") throw new InjectedFailureException();
                result.Status = "Success";
            }
            catch (InjectedFailureException) { result.Status = "InjectedFailure"; }
            catch (DllNotFoundException) { result.Status = "Unavailable"; }
            catch (Exception) { result.Status = "ManagedFailure"; } // Sem mensagem, stack ou blob.
            finally
            {
                // Limpeza também acontece em retornos antecipados e falhas induzidas/gerenciadas.
                try
                {
                    if (result.Written) Cleanup(result);
                }
                catch (Exception) { result.CleanupStatus = "ManagedCleanupFailure"; }
                finally
                {
                    CryptographicOperations.ZeroMemory(marker);
                    if (unmanagedMarker != IntPtr.Zero)
                    {
                        ZeroUnmanaged(unmanagedMarker, marker.Length);
                        Marshal.FreeHGlobal(unmanagedMarker);
                    }
                }
            }
            return result;
        }

        private static void Cleanup(ProbeResult result)
        {
            result.Deleted = CredDelete(result.SyntheticTarget, Generic, 0);
            if (!result.Deleted)
            {
                result.CleanupNativeError = Marshal.GetLastWin32Error();
                result.CleanupStatus = Sanitize(result.CleanupNativeError);
            }

            // Consulta somente o target recém-excluído, sem enumeração do cofre.
            if (CredRead(result.SyntheticTarget, Generic, 0, out var residual))
            {
                CredFree(residual);
                result.CleanupStatus = "ResidualSyntheticCredential";
                return;
            }
            result.AbsenceNativeError = Marshal.GetLastWin32Error();
            result.ConfirmedAbsent = result.AbsenceNativeError == NotFound;
            if (result.ConfirmedAbsent && (result.Deleted || result.CleanupNativeError == NotFound))
                result.CleanupStatus = "Success";
            else if (!result.ConfirmedAbsent)
                result.CleanupStatus = Sanitize(result.AbsenceNativeError);
        }

        private static void ZeroUnmanaged(IntPtr pointer, int length)
        {
            for (int index = 0; index < length; index++) Marshal.WriteByte(pointer, index, 0);
        }

        private static string Sanitize(int error)
        {
            switch (error)
            {
                case 5: return "Denied";
                case 1168: return "NotFound";
                case 1312: return "NoLogonSession";
                case 87: return "InvalidParameter";
                case 1004: return "InvalidFlags";
                default: return "NativeFailure";
            }
        }

        private sealed class InjectedFailureException : Exception { }
    }
}
