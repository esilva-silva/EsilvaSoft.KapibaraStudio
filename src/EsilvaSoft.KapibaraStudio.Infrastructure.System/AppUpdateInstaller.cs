using System.Runtime.InteropServices;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>
/// Swaps a staged update onto disk by rename after the application exits and detects whether the current
/// installation is eligible to replace itself. Windows allows renaming a running executable but not
/// overwriting it; on Linux the running process keeps the old inode.
/// </summary>
internal static class AppUpdateInstaller
{
    public const string ExecutableBaseName = "EsilvaSoft.SlopStudio.Desktop";
    internal const string KapibaraExecutableBaseName = "EsilvaSoft.KapibaraStudio.Desktop";
    private static readonly string[] ExecutableBaseNames = [KapibaraExecutableBaseName, ExecutableBaseName];
    internal const string PendingFileName = "pending.json";
    private const UnixFileMode ExecutableMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
    private static readonly JsonSerializerOptions PendingJson = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    internal static bool ApplyPending(AppUpdateInstallation options)
    {
        try
        {
            using var stagingLease = AcquireStagingLease(options.UpdatesDirectory);
            return ApplyPendingCore(options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool ApplyPendingCore(AppUpdateInstallation options)
    {
        PendingAppUpdate? pending;
        bool valid;
        try
        {
            // Exclusive handle: a second instance closing at the same time skips instead of racing the renames.
            using var stream = new FileStream(Path.Combine(options.UpdatesDirectory, PendingFileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            pending = Deserialize(stream);
            valid = pending is not null && IsValid(pending, options);
            if (valid && pending is not null)
            {
                try { ReplaceFiles(pending.PayloadDirectory, options.TargetDirectory, pending.ExecutableName); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
                {
                    stream.SetLength(0);
                    JsonSerializer.Serialize(stream, pending with { LastError = ex.Message }, PendingJson);
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
        Discard(options, pending);
        return valid;
    }

    internal static void ReplaceFiles(string payloadDirectory, string targetDirectory, string executableName)
    {
        // Other files first; swapping the executable is the committing step.
        var files = Directory.GetFiles(payloadDirectory, "*", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(payloadDirectory, file))
            .OrderBy(relative => string.Equals(relative, executableName, PathComparison) ? 1 : 0)
            .ThenBy(relative => relative, StringComparer.Ordinal)
            .ToArray();
        if (!files.Contains(executableName, PathComparer)) throw new InvalidDataException("A atualização preparada não contém o executável.");
        var replaced = new List<(string Target, string? Backup)>();
        try
        {
            foreach (var relative in files)
            {
                var target = Path.Combine(targetDirectory, relative);
                var staged = target + ".new";
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(Path.Combine(payloadDirectory, relative), staged, overwrite: true);
                if (!OperatingSystem.IsWindows())
                {
                    // Preserve archive modes for child runtimes/proxies, including a pre-existing staging file.
                    File.SetUnixFileMode(staged, File.GetUnixFileMode(Path.Combine(payloadDirectory, relative)));
                    if (string.Equals(relative, executableName, StringComparison.Ordinal)
                        || ExecutableBaseNames.Contains(relative, StringComparer.Ordinal))
                        File.SetUnixFileMode(staged, ExecutableMode);
                }
                string? backup = null;
                if (File.Exists(target))
                {
                    backup = FreeBackupPath(target);
                    File.Move(target, backup);
                }
                replaced.Add((target, backup));
                File.Move(staged, target);
            }
        }
        catch
        {
            for (var index = replaced.Count - 1; index >= 0; index--)
            {
                var (target, backup) = replaced[index];
                try
                {
                    if (File.Exists(target)) File.Delete(target);
                    if (backup is not null) File.Move(backup, target);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            foreach (var relative in files) TryDelete(Path.Combine(targetDirectory, relative) + ".new");
            throw;
        }
    }

    internal static void Cleanup(AppUpdateInstallation options)
    {
        using var stagingLease = AcquireStagingLease(options.UpdatesDirectory);
        foreach (var baseName in ExecutableBaseNames)
        foreach (var file in Directory.EnumerateFiles(options.TargetDirectory, baseName + "*"))
            if (file.EndsWith(".old", StringComparison.Ordinal) || file.EndsWith(".new", StringComparison.Ordinal)) TryDelete(file);
        if (!Directory.Exists(options.UpdatesDirectory)) return;
        var pending = ReadValidPending(options);
        if (pending is null) TryDelete(Path.Combine(options.UpdatesDirectory, PendingFileName));
        var keep = pending is null ? null : Path.GetDirectoryName(Path.GetFullPath(pending.PayloadDirectory));
        foreach (var directory in Directory.EnumerateDirectories(options.UpdatesDirectory))
            if (keep is null || !SamePath(directory, keep)) TryDeleteDirectory(directory);
    }

    internal static void WritePending(string updatesDirectory, PendingAppUpdate pending)
    {
        Directory.CreateDirectory(updatesDirectory);
        var path = Path.Combine(updatesDirectory, PendingFileName);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(pending, PendingJson));
        File.Move(path + ".tmp", path, overwrite: true);
    }

    internal static FileStream AcquireStagingLease(string updatesDirectory)
    {
        Directory.CreateDirectory(updatesDirectory);
        return new FileStream(Path.Combine(updatesDirectory, ".staging.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None, 1, FileOptions.DeleteOnClose);
    }

    internal static AppUpdateAvailability DetectAvailability(string? processPath, AppVersion version, string? rid)
    {
        if (Environment.GetEnvironmentVariable("SLOPSTUDIO_DISABLE_UPDATES") is "1" or "true" || rid is null || processPath is null
            || version is { Major: 0, Minor: 0, Patch: 0 }) return AppUpdateAvailability.Disabled;
        var directory = Path.GetDirectoryName(processPath)!;
        // Only the published single-file executable replaces itself; build output keeps the assembly next to the host.
        var executable = Path.GetFileName(processPath);
        var suffix = rid.StartsWith("win-", StringComparison.Ordinal) ? ".exe" : "";
        if (!ExecutableBaseNames.Any(name => string.Equals(executable, name + suffix, StringComparison.Ordinal))
            || ExecutableBaseNames.Any(name => File.Exists(Path.Combine(directory, name + ".dll")))) return AppUpdateAvailability.Disabled;
        return CanWrite(directory) ? AppUpdateAvailability.Supported : AppUpdateAvailability.ManualOnly;
    }

    internal static void PrepareExecutableAliases(string payloadDirectory, AppUpdateInstallation options)
    {
        var suffix = options.Rid.StartsWith("win-", StringComparison.Ordinal) ? ".exe" : "";
        var names = ExecutableBaseNames.Select(name => name + suffix).ToArray();
        if (!names.Contains(options.ExecutableName, PathComparer))
            throw new InvalidDataException("O executável desta instalação não é compatível com a atualização.");
        var incoming = names.FirstOrDefault(name => File.Exists(Path.Combine(payloadDirectory, name)))
            ?? throw new InvalidDataException("O pacote não contém o executável esperado.");
        // Keep existing shortcuts and the restart path valid across either direction of the rename.
        // Refresh other installed aliases too, so they cannot launch a stale version after a later update.
        foreach (var name in names)
            if (!string.Equals(name, incoming, PathComparison)
                && (string.Equals(name, options.ExecutableName, PathComparison)
                    || File.Exists(Path.Combine(options.TargetDirectory, name))
                    || File.Exists(Path.Combine(payloadDirectory, name))))
                File.Copy(Path.Combine(payloadDirectory, incoming), Path.Combine(payloadDirectory, name), overwrite: true);
    }

    internal static string? CurrentRid()
    {
        var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : null;
        var architecture = RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => null };
        return os is null || architecture is null ? null : $"{os}-{architecture}";
    }

    internal static PendingAppUpdate? ReadValidPending(AppUpdateInstallation options)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(options.UpdatesDirectory, PendingFileName), FileMode.Open, FileAccess.Read, FileShare.Read);
            return Deserialize(stream) is { } pending && IsValid(pending, options) ? pending : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static PendingAppUpdate? Deserialize(Stream stream)
    {
        try { return JsonSerializer.Deserialize<PendingAppUpdate>(stream, PendingJson); }
        catch (JsonException) { return null; }
    }

    private static bool IsValid(PendingAppUpdate pending, AppUpdateInstallation options) =>
        pending is { Version: not null, PayloadDirectory: not null, TargetDirectory: not null, ExecutableName: not null }
        && AppVersion.TryParse(pending.Version, out var version) && version > options.CurrentVersion
        && SamePath(pending.TargetDirectory, options.TargetDirectory)
        && IsUnder(pending.PayloadDirectory, options.UpdatesDirectory)
        && string.Equals(pending.ExecutableName, options.ExecutableName, PathComparison)
        && File.Exists(Path.Combine(pending.PayloadDirectory, pending.ExecutableName));

    private static void Discard(AppUpdateInstallation options, PendingAppUpdate? pending)
    {
        TryDelete(Path.Combine(options.UpdatesDirectory, PendingFileName));
        if (pending?.PayloadDirectory is { } payload && Path.GetDirectoryName(Path.GetFullPath(payload)) is { } versionDirectory
            && IsUnder(versionDirectory, options.UpdatesDirectory)) TryDeleteDirectory(versionDirectory);
    }

    private static string FreeBackupPath(string target)
    {
        var backup = target + ".old";
        try
        {
            File.Delete(backup);
            return backup;
        }
        // A previous instance that is still exiting keeps its renamed image locked.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return $"{target}.{Guid.NewGuid():N}.old"; }
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            using (new FileStream(Path.Combine(directory, $".slopstudio-write-{Guid.NewGuid():N}.tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    internal static bool SamePath(string left, string right) => string.Equals(NormalizePath(left), NormalizePath(right), PathComparison);
    private static bool IsUnder(string child, string parent) => NormalizePath(child).StartsWith(NormalizePath(parent) + Path.DirectorySeparatorChar, PathComparison);

    internal static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    internal static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
