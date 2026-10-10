using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Creates the KapiLab model service with a user-profile-wide GPU model-load lease.</summary>
internal static class KapiLabModelServices
{
    public static LocalAiModelService Create(LocalModelCatalog catalog, KapiLabModelFileAccess fileAccess, string command,
        string workspace, GpuExecutionMode gpuMode, IAutocompleteDiagnostics? diagnostics = null)
    {
        var hardware = new OnnxHardwareProbe();
        var runId = Guid.NewGuid().ToString("N");
        return new LocalAiModelService(catalog, () => new GpuLeaseModelRuntime(
            new OnnxLocalModelRuntime(diagnostics: diagnostics, fileAccess: fileAccess, hardware: hardware), hardware,
            GpuModelLoadLock.DefaultLockDirectory, command, runId, workspace, gpuMode), hardware, diagnostics);
    }

    public static bool IsGpuLockUnavailable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is GpuModelLoadLockUnavailableException or GpuCoordinationException) return true;
        return false;
    }
}

/// <summary>
/// Acquires the cross-process GPU lease from the same provider plan the ONNX runtime will use,
/// before forwarding initialization (and therefore before its first native Model allocation).
/// The lease follows the runtime lifetime, including provider fallback, until its session is disposed.
/// </summary>
internal sealed class GpuLeaseModelRuntime(
    ILocalModelRuntime inner, IAiHardwareProbe hardware, string lockDirectory, string owner, string runId,
    string workspace = ".", GpuExecutionMode gpuMode = GpuExecutionMode.Unspecified,
    TimeSpan? heartbeatInterval = null, TimeSpan? pausePollInterval = null) : ILocalModelRuntime
{
    private GpuModelLoadLock? _lease;
    private GpuLeaseHeartbeat? _heartbeat;
    private int _disposed;

    public LocalModelRuntimeInfo? RuntimeInfo => inner.RuntimeInfo;

    public void SetLocalization(Func<string, string> localize) => inner.SetLocalization(localize);

    public async Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default) =>
        await InitializeAsync(model, settings, null, cancellationToken).ConfigureAwait(false);

    public async Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, IProgress<string>? progress,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_lease is null)
        {
            var devices = await hardware.GetAvailableHardwareAsync(cancellationToken).ConfigureAwait(false);
            var plan = AiProviderSelector.Plan(settings, devices, model);
            if (plan.Candidates.Any(candidate => candidate.Kind == AiAccelerationMode.Gpu))
            {
                if (gpuMode == GpuExecutionMode.Unspecified)
                    throw new GpuCoordinationException("Carga GPU exige --via-queue ou --standalone-gpu.");
                if (gpuMode == GpuExecutionMode.ViaQueue && !GpuExecutionCoordination.HasQueueToken())
                    throw new GpuCoordinationException("Carga GPU via fila exige KAPILAB_GPU_TOKEN não vazio.");
                if (gpuMode == GpuExecutionMode.Standalone)
                    await GpuExecutionCoordination.WaitForStandaloneResumeAsync(workspace,
                        pausePollInterval ?? TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);

                var lease = GpuModelLoadLock.Acquire(lockDirectory, owner, runId);
                try
                {
                    var heartbeat = await GpuLeaseHeartbeat.StartAsync(workspace, owner, runId, heartbeatInterval).ConfigureAwait(false);
                    _lease = lease;
                    _heartbeat = heartbeat;
                }
                catch
                {
                    lease.Dispose();
                    throw;
                }
            }
        }
        try { await inner.InitializeAsync(model, settings, progress, cancellationToken).ConfigureAwait(false); }
        catch
        {
            await ReleaseGpuLeaseAsync().ConfigureAwait(false);
            throw;
        }
    }

    public Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default) =>
        inner.GenerateAsync(request, cancellationToken);

    public IAsyncEnumerable<GeneratedChunk> StreamAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default) =>
        inner.StreamAsync(request, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { await inner.DisposeAsync().ConfigureAwait(false); }
        finally { await ReleaseGpuLeaseAsync().ConfigureAwait(false); }
    }

    private async ValueTask ReleaseGpuLeaseAsync()
    {
        try { if (Interlocked.Exchange(ref _heartbeat, null) is { } heartbeat) await heartbeat.DisposeAsync().ConfigureAwait(false); }
        finally { Interlocked.Exchange(ref _lease, null)?.Dispose(); }
    }
}
