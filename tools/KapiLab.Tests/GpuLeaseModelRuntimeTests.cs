using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
[NonParallelizable]
public sealed class GpuLeaseModelRuntimeTests
{
    private static readonly LocalModelDefinition Model = new("fixture", "fixture", "unused", "qwen");

    [Test]
    public async Task AutoPlanWithGpuAcquiresLeaseBeforeRuntimeInitialization()
    {
        using var workspace = new TemporaryWorkspace();
        var inner = new ProbeRuntime(() => Assert.Throws<IOException>(() =>
            new FileStream(Path.Combine(workspace.Path, "tmp", "kapilab-model-load.lock"), FileMode.Open,
                FileAccess.ReadWrite, FileShare.None)));
        await using (var runtime = Create(inner, workspace.Path, GpuDevice, GpuExecutionMode.Standalone))
            await runtime.InitializeAsync(Model, new AutocompleteSettings { Acceleration = AiAccelerationMode.Auto });
        Assert.That(inner.Initialized, Is.True);
        Assert.That(File.Exists(Path.Combine(workspace.Path, "tmp", "kapilab-model-load.lock")), Is.True);
    }

    [Test]
    public async Task CpuOnlyPlanDoesNotAcquireGpuLease()
    {
        using var workspace = new TemporaryWorkspace();
        var inner = new ProbeRuntime();
        await using (var runtime = Create(inner, workspace.Path, [new(AiAccelerationMode.Cpu, "CPU", "CPU", true)]))
            await runtime.InitializeAsync(Model, new AutocompleteSettings { Acceleration = AiAccelerationMode.Cpu });
        Assert.That(inner.Initialized, Is.True);
        Assert.That(File.Exists(Path.Combine(workspace.Path, "tmp", "kapilab-model-load.lock")), Is.False);
    }

    [Test]
    public async Task BusyGpuLeaseStopsBeforeInnerRuntimeInitialization()
    {
        using var workspace = new TemporaryWorkspace();
        using var existing = GpuModelLoadLock.Acquire(Path.Combine(workspace.Path, "tmp"), "other", "active");
        var inner = new ProbeRuntime();
        await using var runtime = Create(inner, workspace.Path, GpuDevice, GpuExecutionMode.Standalone);
        Assert.ThrowsAsync<GpuModelLoadLockUnavailableException>(async () =>
            await runtime.InitializeAsync(Model, new AutocompleteSettings { Acceleration = AiAccelerationMode.Gpu }));
        Assert.That(inner.Initialized, Is.False);
    }

    [Test]
    public async Task GpuWithoutModeStopsBeforeNativeInitialization()
    {
        using var workspace = new TemporaryWorkspace();
        var inner = new ProbeRuntime();
        await using var runtime = Create(inner, workspace.Path, GpuDevice);
        Assert.ThrowsAsync<GpuCoordinationException>(async () =>
            await runtime.InitializeAsync(Model, new AutocompleteSettings { Acceleration = AiAccelerationMode.Gpu }));
        Assert.That(inner.Initialized, Is.False);
        Assert.That(File.Exists(Path.Combine(workspace.Path, "tmp", "kapilab.heartbeat")), Is.False);
    }

    [Test]
    public async Task StandaloneWaitsForPauseRemovalAndHeartbeatSpansRuntimeLifetime()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, "tmp"));
        var pause = Path.Combine(workspace.Path, "tmp", "gpu.pause");
        await File.WriteAllTextAsync(pause, "pause");
        var inner = new ProbeRuntime(() => Assert.That(File.Exists(Path.Combine(workspace.Path, "tmp", "kapilab.heartbeat")), Is.True));
        await using var runtime = new GpuLeaseModelRuntime(inner, new ProbeHardware(GpuDevice), Path.Combine(workspace.Path, "tmp"),
            "test", "run-test", workspace.Path, GpuExecutionMode.Standalone, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(10));
        var initializing = runtime.InitializeAsync(Model, new AutocompleteSettings { Acceleration = AiAccelerationMode.Gpu });
        await Task.Delay(30);
        Assert.That(inner.Initialized, Is.False);
        File.Delete(pause);
        await initializing;
        var heartbeatPath = Path.Combine(workspace.Path, "tmp", "kapilab.heartbeat");
        var first = await File.ReadAllTextAsync(heartbeatPath);
        await Task.Delay(60);
        var second = await File.ReadAllTextAsync(heartbeatPath);
        Assert.That(second, Does.Contain("run-test"));
        Assert.That(second, Does.Contain("updatedAtUtc"));
        Assert.That(second, Is.Not.EqualTo(first));
        await runtime.DisposeAsync();
        Assert.That(File.Exists(heartbeatPath), Is.False);
    }

    [Test]
    public async Task QueueModeRequiresTokenAndNeverWritesItToHeartbeat()
    {
        using var workspace = new TemporaryWorkspace();
        var previous = Environment.GetEnvironmentVariable("KAPILAB_GPU_TOKEN");
        try
        {
            Environment.SetEnvironmentVariable("KAPILAB_GPU_TOKEN", null);
            var denied = Create(new ProbeRuntime(), workspace.Path, GpuDevice, GpuExecutionMode.ViaQueue);
            await using (denied)
                Assert.ThrowsAsync<GpuCoordinationException>(async () =>
                    await denied.InitializeAsync(Model, new AutocompleteSettings { Acceleration = AiAccelerationMode.Gpu }));

            const string token = "synthetic-queue-secret";
            Environment.SetEnvironmentVariable("KAPILAB_GPU_TOKEN", token);
            var allowed = Create(new ProbeRuntime(), workspace.Path, GpuDevice, GpuExecutionMode.ViaQueue);
            await using (allowed)
            {
                await allowed.InitializeAsync(Model, new AutocompleteSettings { Acceleration = AiAccelerationMode.Gpu });
                var heartbeat = await File.ReadAllTextAsync(Path.Combine(workspace.Path, "tmp", "kapilab.heartbeat"));
                Assert.That(heartbeat, Does.Not.Contain(token));
            }
        }
        finally { Environment.SetEnvironmentVariable("KAPILAB_GPU_TOKEN", previous); }
    }

    [Test]
    public async Task ReplacesStaleHeartbeatAfterExclusiveGuardRecovery()
    {
        using var workspace = new TemporaryWorkspace();
        var temporary = Directory.CreateDirectory(Path.Combine(workspace.Path, "tmp"));
        var heartbeatPath = Path.Combine(temporary.FullName, "kapilab.heartbeat");
        await File.WriteAllTextAsync(heartbeatPath,
            "{\"schema\":\"kapilab-gpu-heartbeat-v1\",\"owner\":\"crashed\",\"runId\":\"old\",\"processId\":1,\"updatedAtUtc\":\"2000-01-01T00:00:00Z\"}");
        var inner = new ProbeRuntime(() => Assert.That(File.ReadAllText(heartbeatPath), Does.Contain("run-after-crash")));
        var runtime = new GpuLeaseModelRuntime(inner, new ProbeHardware(GpuDevice), Path.Combine(workspace.Path, "tmp"),
            "test", "run-after-crash", workspace.Path, GpuExecutionMode.Standalone);
        await using (runtime)
            await runtime.InitializeAsync(Model, new AutocompleteSettings { Acceleration = AiAccelerationMode.Gpu });
        Assert.That(File.Exists(heartbeatPath), Is.False);
    }

    [Test]
    public void PauseWaitCanBeCancelled()
    {
        using var workspace = new TemporaryWorkspace();
        var temporary = Directory.CreateDirectory(Path.Combine(workspace.Path, "tmp"));
        File.WriteAllText(Path.Combine(temporary.FullName, "gpu.pause"), "pause");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await GpuExecutionCoordination.WaitForStandaloneResumeAsync(workspace.Path,
                TimeSpan.FromMilliseconds(5), cancellation.Token));
    }

    private static readonly IReadOnlyList<AiHardwareDevice> GpuDevice =
        [new(AiAccelerationMode.Gpu, "DirectML", "test GPU", true)];

    private static GpuLeaseModelRuntime Create(ProbeRuntime inner, string workspace, IReadOnlyList<AiHardwareDevice> devices,
        GpuExecutionMode mode = GpuExecutionMode.Unspecified) =>
        new(inner, new ProbeHardware(devices), Path.Combine(workspace, "tmp"), "test", Guid.NewGuid().ToString("N"), workspace, mode);

    private sealed class ProbeHardware(IReadOnlyList<AiHardwareDevice> devices) : IAiHardwareProbe
    {
        public Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default) => Task.FromResult(devices);
    }

    private sealed class ProbeRuntime(Action? onInitialize = null) : ILocalModelRuntime
    {
        public bool Initialized { get; private set; }
        public LocalModelRuntimeInfo? RuntimeInfo => null;
        public void SetLocalization(Func<string, string> localize) { }
        public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default)
        {
            Initialized = true;
            onInitialize?.Invoke();
            return Task.CompletedTask;
        }
        public Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kapilab-runtime-lock-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public string Path { get; }
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
