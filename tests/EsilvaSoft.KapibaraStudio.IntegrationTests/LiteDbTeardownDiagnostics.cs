using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Opt-in observation of the first failed synthetic-directory deletion. Never retries or releases locks.</summary>
internal static class LiteDbTeardownDiagnostics
{
    internal const string OptInVariable = "KAPIBARA_TEST_LITEDB_TEARDOWN_DIAGNOSTICS";
    internal const string LogFileName = "workspace-log.db.tmp";

    internal sealed record ProcessIdentity(uint ProcessId, long StartTimeFileTime);
    internal sealed record Snapshot(DateTimeOffset CapturedAtUtc, string TestCase, string DirectoryPath,
        string? FilePath, int HResult, int ObserverProcessId, string Status, uint? NativeError,
        IReadOnlyList<ProcessIdentity> ResourceUsers);

    internal static void DeleteDirectory(string directory)
    {
        var failureObserved = false;
        DeleteDirectory(directory, ref failureObserved);
    }

    internal static void DeleteDirectory(string directory, ref bool failureObserved)
    {
        try { Directory.Delete(directory, recursive: true); }
        catch (IOException error)
        {
            ObserveFirstFailure(directory, error, ref failureObserved,
                string.Equals(Environment.GetEnvironmentVariable(OptInVariable), "1", StringComparison.Ordinal),
                snapshot => TestContext.Progress.WriteLine("F5-L00 teardown: " + JsonSerializer.Serialize(snapshot)));
            throw;
        }
    }

    // Explicit flag and sink keep the diagnostic contract testable without changing process-wide environment.
    internal static void ObserveFirstFailure(string directory, IOException error, ref bool failureObserved,
        bool enabled, Action<Snapshot> sink)
    {
        if (failureObserved || !OperatingSystem.IsWindows() || (error.HResult & 0xffff) is not (32 or 33)) return;
        failureObserved = true;
        if (!enabled) return;
        try { sink(Capture(directory, error)); }
        catch (Exception)
        {
            // Diagnostics must never replace the original cleanup exception, even if the output sink fails.
        }
    }

    internal static Snapshot Capture(string directory, IOException error)
    {
        var capturedAt = DateTimeOffset.UtcNow;
        var testCase = TestContext.CurrentContext.Test.FullName;
        var target = Path.GetFullPath(directory);
        Snapshot Result(string status, string? file = null, uint? nativeError = null,
            IReadOnlyList<ProcessIdentity>? users = null) =>
            new(capturedAt, testCase, target, file, error.HResult, Environment.ProcessId, status, nativeError, users ?? []);

        try
        {
            var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KapibaraStudio.Tests"));
            if (!string.Equals(Path.GetDirectoryName(target), parent, StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(target)
                || (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0
                || (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                return Result("PathRejected");

            // Do not parse the localized exception or register a directory/arbitrary user file.
            var file = Directory.EnumerateFiles(target, "*", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(candidate => string.Equals(Path.GetFileName(candidate), LogFileName,
                    StringComparison.OrdinalIgnoreCase));
            if (file is null) return Result("FileNoLongerPresent");
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) return Result("PathRejected");
            if (!OperatingSystem.IsWindows()) return Result("WindowsOnly", file);

            var started = Native.RmStartSession(out var session, 0, new char[33]);
            if (started != 0) return Result("StartSessionFailed", file, started);
            try
            {
                var registered = Native.RmRegisterResources(session, 1, [file], 0, IntPtr.Zero, 0, IntPtr.Zero);
                if (registered != 0) return Result("RegisterResourceFailed", file, registered);
                // One bounded snapshot: ERROR_MORE_DATA is reported, never queried repeatedly.
                var entries = new Native.ProcessInfo[64];
                uint count = (uint)entries.Length;
                var listed = Native.RmGetList(session, out _, ref count, entries, out _);
                if (listed != 0) return Result("GetListFailed", file, listed);
                var users = entries.Take((int)count).Select(entry => new ProcessIdentity(entry.Process.ProcessId,
                    ((long)entry.Process.StartTime.dwHighDateTime << 32) | (uint)entry.Process.StartTime.dwLowDateTime)).ToArray();
                return Result(users.Length == 0 ? "NoResourceUsersReported" : "ResourceUsersReported", file, 0, users);
            }
            finally { _ = Native.RmEndSession(session); }
        }
        catch (Exception exception)
        {
            // Fixed status/code only: no file contents, process command lines, or exception text.
            return Result("DiagnosticUnavailable", nativeError: unchecked((uint)exception.HResult));
        }
    }

    [SuppressMessage("Interoperability", "SYSLIB1054:Use LibraryImportAttribute instead of DllImportAttribute",
        Justification = "Test-only Restart Manager structures contain fixed Unicode strings and an output array.")]
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct UniqueProcess
        {
            internal uint ProcessId;
            internal System.Runtime.InteropServices.ComTypes.FILETIME StartTime;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct ProcessInfo
        {
            internal UniqueProcess Process;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string AppName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string ServiceShortName;
            internal int ApplicationType;
            internal uint AppStatus;
            internal uint SessionId;
            [MarshalAs(UnmanagedType.Bool)] internal bool Restartable;
        }

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint RmStartSession(out uint sessionHandle, uint flags, [Out] char[] sessionKey);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint RmRegisterResources(uint sessionHandle, uint fileCount, string[] fileNames,
            uint applicationCount, IntPtr applications, uint serviceCount, IntPtr serviceNames);

        [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint RmGetList(uint sessionHandle, out uint needed, ref uint count,
            [In, Out] ProcessInfo[] affectedApps, out uint rebootReasons);

        [DllImport("rstrtmgr.dll", ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        internal static extern uint RmEndSession(uint sessionHandle);
    }
}
