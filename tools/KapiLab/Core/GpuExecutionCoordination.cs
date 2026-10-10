using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal enum GpuExecutionMode
{
    Unspecified,
    ViaQueue,
    Standalone,
}

internal sealed class GpuCoordinationException(string message, Exception? innerException = null)
    : IOException(message, innerException);

internal static class GpuExecutionCoordination
{
    public static bool TryParseMode(bool viaQueue, bool standaloneGpu, out GpuExecutionMode mode)
    {
        mode = (viaQueue, standaloneGpu) switch
        {
            (false, false) => GpuExecutionMode.Unspecified,
            (true, false) => GpuExecutionMode.ViaQueue,
            (false, true) => GpuExecutionMode.Standalone,
            _ => GpuExecutionMode.Unspecified,
        };
        return !(viaQueue && standaloneGpu);
    }

    public static bool HasQueueToken() => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("KAPILAB_GPU_TOKEN"));

    public static async Task WaitForStandaloneResumeAsync(string workspace, TimeSpan pollInterval, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pollInterval, TimeSpan.Zero);
        var root = Path.GetFullPath(workspace);
        var temporaryDirectory = Directory.CreateDirectory(Path.Combine(root, "tmp")).FullName;
        LabWorkspace.RefuseBlindInput(temporaryDirectory, root);
        var pausePath = Path.Combine(temporaryDirectory, "gpu.pause");
        LabWorkspace.RefuseBlindInput(pausePath, root);
        while (File.Exists(pausePath) || Directory.Exists(pausePath))
        {
            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
            LabWorkspace.RefuseBlindInput(pausePath, root);
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}

/// <summary>Workspace heartbeat owned by one live GPU lease; the token and model data never enter this file.</summary>
internal sealed class GpuLeaseHeartbeat : IAsyncDisposable
{
    private const string Schema = "kapilab-gpu-heartbeat-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _path;
    private readonly string _owner;
    private readonly string _runId;
    private readonly int _processId;
    private readonly TimeSpan _interval;
    private readonly FileStream _guard;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _pulseTask;
    private int _disposed;

    private GpuLeaseHeartbeat(string path, string owner, string runId, TimeSpan interval, FileStream guard)
    {
        _path = path;
        _owner = owner;
        _runId = runId;
        _processId = Environment.ProcessId;
        _interval = interval;
        _guard = guard;
        _pulseTask = Task.Run(PulseAsync);
    }

    public static async Task<GpuLeaseHeartbeat> StartAsync(string workspace, string owner, string runId,
        TimeSpan? interval = null)
    {
        var root = Path.GetFullPath(workspace);
        var directory = Directory.CreateDirectory(Path.Combine(root, "tmp")).FullName;
        LabWorkspace.RefuseBlindInput(directory, root);
        var path = Path.Combine(directory, "kapilab.heartbeat");
        var guardPath = path + ".guard";
        LabWorkspace.RefuseBlindInput(guardPath, root);
        FileStream guard;
        try { guard = new FileStream(guardPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException exception) { throw new GpuCoordinationException("Heartbeat GPU já pertence a outra execução.", exception); }
        GpuLeaseHeartbeat? heartbeat = null;
        try
        {
            heartbeat = new GpuLeaseHeartbeat(path, owner, runId, interval ?? TimeSpan.FromSeconds(30), guard);
            await heartbeat.WritePulseAsync().ConfigureAwait(false);
            return heartbeat;
        }
        catch
        {
            if (heartbeat is not null) await heartbeat.DisposeAsync().ConfigureAwait(false);
            else guard.Dispose();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _stop.CancelAsync().ConfigureAwait(false);
        try { await _pulseTask.ConfigureAwait(false); }
        finally
        {
            try
            {
                if (await IsOwnedByThisRunAsync().ConfigureAwait(false)) File.Delete(_path);
            }
            finally
            {
                _guard.Dispose();
                _stop.Dispose();
            }
        }
    }

    private async Task PulseAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(_interval);
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
                await WritePulseAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private async Task WritePulseAsync()
    {
        LabWorkspace.RefuseBlindInput(_path, Path.GetDirectoryName(Path.GetDirectoryName(_path)!)!);
        // The exclusive guard is the ownership boundary. A prior heartbeat can be stale after a crash;
        // acquiring this guard proves no cooperating writer still owns it, so this run may replace it.
        var payload = JsonSerializer.Serialize(new HeartbeatRecord(Schema, _owner, _runId, _processId, DateTimeOffset.UtcNow), JsonOptions);
        var temporary = _path + "." + _runId + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var bytes = new UTF8Encoding(false).GetBytes(payload);
                await stream.WriteAsync(bytes).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, _path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private async Task<bool> IsOwnedByThisRunAsync()
    {
        try
        {
            await using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                root.TryGetProperty("schema", out var schema) && schema.GetString() == Schema &&
                root.TryGetProperty("owner", out var owner) && owner.GetString() == _owner &&
                root.TryGetProperty("runId", out var runId) && runId.GetString() == _runId &&
                root.TryGetProperty("processId", out var processId) && processId.TryGetInt32(out var pid) && pid == _processId;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record HeartbeatRecord(string Schema, string Owner, string RunId, int ProcessId, DateTimeOffset UpdatedAtUtc);
}
