using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.SystemAdapters;

/// <summary>Disk adapter for exclusive, verified update staging and application lifecycle hooks.</summary>
public sealed class LocalAppUpdateStorage : IAppUpdateStorage
{
    public StagedAppUpdate? GetStagedUpdate(AppUpdateInstallation installation) =>
        installation.Availability == AppUpdateAvailability.Disabled || AppUpdateInstaller.ReadValidPending(installation) is not { } pending
            ? null : new StagedAppUpdate(AppVersion.Parse(pending.Version), pending.LastError);

    public IAppUpdateStaging BeginStaging(AppUpdateInstallation installation, AppUpdateRelease release, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installation);
        ArgumentNullException.ThrowIfNull(release);
        cancellationToken.ThrowIfCancellationRequested();
        if (Path.GetFileName(release.AssetName) != release.AssetName || release.AssetName is "" or "." or ".."
            || release.AssetName.Contains('\\') || release.AssetName.Contains('/'))
            throw new InvalidDataException("O nome do pacote de atualização é inválido.");
        var lease = AppUpdateInstaller.AcquireStagingLease(installation.UpdatesDirectory);
        try { return new Staging(installation, release, lease); }
        catch { lease.Dispose(); throw; }
    }

    public static AppUpdateInstallation DetectInstallation(string updatesDirectory)
    {
        var processPath = Environment.ProcessPath;
        var informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = AppVersion.TryParse(informational, out var parsed) ? parsed : AppVersion.Parse("0.0.0-local");
        var rid = AppUpdateInstaller.CurrentRid();
        return new(AppUpdateInstaller.DetectAvailability(processPath, version, rid), version, rid ?? "",
            processPath is null ? AppContext.BaseDirectory : Path.GetDirectoryName(processPath)!,
            processPath is null ? "" : Path.GetFileName(processPath), updatesDirectory);
    }

    public static void ApplyPendingOnExit(AppUpdateInstallation installation, bool restart)
    {
        try
        {
            if (installation.Availability != AppUpdateAvailability.Supported) return;
            AppUpdateInstaller.ApplyPending(installation);
            if (restart)
                Process.Start(new ProcessStartInfo(Path.Combine(installation.TargetDirectory, installation.ExecutableName))
                    { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    public static void CleanupAfterStart(AppUpdateInstallation installation)
    {
        try
        {
            if (installation.Availability == AppUpdateAvailability.Supported) AppUpdateInstaller.Cleanup(installation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed class Staging : IAppUpdateStaging
    {
        private readonly AppUpdateInstallation _installation;
        private readonly AppUpdateRelease _release;
        private readonly FileStream _lease;
        private readonly string _versionDirectory;
        private readonly string _package;
        private bool _committed;
        private bool _disposed;

        public Staging(AppUpdateInstallation installation, AppUpdateRelease release, FileStream lease)
        {
            _installation = installation;
            _release = release;
            _lease = lease;
            _versionDirectory = Path.Combine(installation.UpdatesDirectory, release.Version.ToString());
            _package = Path.Combine(_versionDirectory, release.AssetName);
            if (AppUpdateInstaller.ReadValidPending(installation) is { } previous && AppVersion.Parse(previous.Version) == release.Version)
                AppUpdateInstaller.TryDelete(Path.Combine(installation.UpdatesDirectory, AppUpdateInstaller.PendingFileName));
            AppUpdateInstaller.TryDeleteDirectory(_versionDirectory);
            Directory.CreateDirectory(_versionDirectory);
        }

        public Stream CreatePackageStream()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new FileStream(_package + ".partial", FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        }

        public void Commit(CancellationToken cancellationToken)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_committed) throw new InvalidOperationException("A atualização já foi preparada.");
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(_package + ".partial", _package);
            var payload = Path.Combine(_versionDirectory, "payload");
            Extract(_package, payload);
            cancellationToken.ThrowIfCancellationRequested();
            AppUpdateInstaller.PrepareExecutableAliases(payload, _installation);
            File.Delete(_package);
            AppUpdateInstaller.WritePending(_installation.UpdatesDirectory,
                new PendingAppUpdate(_release.Version.ToString(), payload, _installation.TargetDirectory, _installation.ExecutableName));
            _committed = true;
            foreach (var other in Directory.EnumerateDirectories(_installation.UpdatesDirectory))
                if (!AppUpdateInstaller.SamePath(other, _versionDirectory)) AppUpdateInstaller.TryDeleteDirectory(other);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { if (!_committed) AppUpdateInstaller.TryDeleteDirectory(_versionDirectory); }
            finally { _lease.Dispose(); }
        }

        private static void Extract(string package, string destination)
        {
            Directory.CreateDirectory(destination);
            if (package.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(package, destination);
                return;
            }
            using var file = File.OpenRead(package);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            TarFile.ExtractToDirectory(gzip, destination, overwriteFiles: false);
        }
    }
}
