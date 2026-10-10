using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>
/// Cross-process, non-blocking lease for a model load. The persistent lock file is
/// intentionally never deleted: the operating system releases the exclusive file
/// handle after a process crash, so a later process can reuse the same file safely.
/// </summary>
internal sealed class GpuModelLoadLock : IDisposable
{
    private const string LockFileName = "kapilab-model-load.lock";
    public static string DefaultLockDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EsilvaSoft.KapibaraStudio", "KapiLab", "locks");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private FileStream? _stream;

    private GpuModelLoadLock(FileStream stream, GpuModelLoadLockOwner owner)
    {
        _stream = stream;
        Owner = owner;
    }

    public GpuModelLoadLockOwner Owner { get; }

    /// <summary>Acquires the user-profile-wide model-load lease without waiting.</summary>
    /// <exception cref="GpuModelLoadLockUnavailableException">Another process owns it.</exception>
    public static GpuModelLoadLock Acquire(string lockDirectory, string owner, string runId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var directory = Path.GetFullPath(lockDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, LockFileName);

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, bufferSize: 4096, FileOptions.WriteThrough);
        }
        catch (IOException exception) when (IsSharingViolation(exception))
        {
            throw new GpuModelLoadLockUnavailableException(path, exception);
        }

        var identity = new GpuModelLoadLockOwner(owner, runId, Environment.ProcessId, DateTimeOffset.UtcNow);
        try
        {
            stream.SetLength(0);
            JsonSerializer.Serialize(stream, identity, JsonOptions);
            stream.Flush(flushToDisk: true);
            stream.Position = 0;
            return new GpuModelLoadLock(stream, identity);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        // Closing only this instance's OS handle releases ownership. Keep the file
        // and its last owner record so a crash leaves a reusable, inspectable file.
        Interlocked.Exchange(ref _stream, null)?.Dispose();
    }

    private static bool IsSharingViolation(IOException exception)
    {
        var nativeCode = exception.HResult & 0xFFFF;
        return OperatingSystem.IsWindows()
            ? nativeCode is 32 or 33 // ERROR_SHARING_VIOLATION / ERROR_LOCK_VIOLATION
            : nativeCode == 11; // EAGAIN / EWOULDBLOCK from the exclusive file lock
    }
}

internal sealed record GpuModelLoadLockOwner(
    string Owner,
    string RunId,
    int ProcessId,
    DateTimeOffset AcquiredAtUtc);

internal sealed class GpuModelLoadLockUnavailableException : IOException
{
    public GpuModelLoadLockUnavailableException(string lockPath, Exception innerException)
        : base("Já existe uma carga de modelo mantendo o lock do KapiLab.", innerException)
    {
        LockPath = lockPath;
    }

    public string LockPath { get; }
}
