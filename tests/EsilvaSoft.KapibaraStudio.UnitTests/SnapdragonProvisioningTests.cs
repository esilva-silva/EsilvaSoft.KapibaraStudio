using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class SnapdragonProvisioningTests
{
    // Fully qualified per OS without reading host state; the fake source never touches the disk.
    private static readonly string ModelsDirectory = OperatingSystem.IsWindows() ? @"C:\kapibara-models" : "/kapibara-models";
    private static readonly AiHardwareDevice Qualcomm = new(AiAccelerationMode.Npu, "QNN", "Physical NPU", true) { Vendor = "Qualcomm Technologies, Inc." };
    private static readonly IReadOnlyList<RemoteModelVariant> Publications = SnapdragonModelPublications.Pinned.Select(item =>
        new RemoteModelVariant(item.Repository, item.Revision, SnapdragonModelPublications.Folder, item.Repository.Split('/')[1] + "-htp-fim-exact-v1", 10,
            "apache-2.0", new Uri("https://huggingface.co/" + item.Repository), [])).ToArray();

    [TestCase(true, true, "0.18.0", true, true)]
    [TestCase(true, true, "0.18.0+build", true, true)]
    [TestCase(false, true, "0.18.0", true, false)]
    [TestCase(true, false, "0.18.0", true, false)]
    [TestCase(true, true, "0.15.2", true, false)]
    [TestCase(true, true, "0.18.0", false, false)]
    [TestCase(true, true, null, true, false)]
    [TestCase(true, true, "0.0.0.0", true, false)]
    public void BuildGateUsesCompiledPackageMarkerNotTheUnversionedOfficialAssembly(bool windows, bool arm64, string? version, bool plugin, bool expected) =>
        Assert.That(SnapdragonRuntimeAvailability.SupportsBuild(windows, arm64, version, plugin), Is.EqualTo(expected));

    [TestCase(AiAccelerationMode.Npu, "QNN", true, "Qualcomm", true)]
    [TestCase(AiAccelerationMode.Npu, "QNN", true, "Qualcomm Technologies, Inc.", true)]
    [TestCase(AiAccelerationMode.Npu, "QNN", true, null, false)]
    [TestCase(AiAccelerationMode.Npu, "QNN", true, "Intel", false)]
    [TestCase(AiAccelerationMode.Npu, "OpenVINO", true, "Qualcomm", false)]
    [TestCase(AiAccelerationMode.Cpu, "QNN", true, "Qualcomm", false)]
    [TestCase(AiAccelerationMode.Gpu, "QNN", true, "Qualcomm", false)]
    [TestCase(AiAccelerationMode.Npu, "QNN", false, "Qualcomm", false)]
    public async Task NoDownloadWithoutAPositiveQualcommQnnNpu(AiAccelerationMode kind, string provider, bool available, string? vendor, bool eligible)
    {
        var probe = new Probe([new(kind, provider, "Qualcomm Snapdragon CPU branding is not evidence", available) { Vendor = vendor }]);
        var source = new Source();
        using var service = new SnapdragonModelProvisioner(new SnapdragonRuntimeAvailability(probe, () => true), source, new ApplicationOperationService());
        var result = await service.ProvisionAsync(ModelsDirectory);
        Assert.That(result.Eligible, Is.EqualTo(eligible));
        Assert.That(source.ListCalls, Is.EqualTo(eligible ? 1 : 0));
        Assert.That(source.Transfers, Has.Count.EqualTo(eligible ? 2 : 0));
    }

    [Test]
    public async Task RegisteredProviderOrArmPlatformAloneNeverStartsMetadataRequests()
    {
        var source = new Source();
        using var service = new SnapdragonModelProvisioner(new SnapdragonRuntimeAvailability(new Probe([]), () => true), source, new ApplicationOperationService());
        Assert.That((await service.ProvisionAsync(ModelsDirectory)).Eligible, Is.False);
        Assert.That(source.ListCalls, Is.Zero);
    }

    [Test]
    public async Task BothPackagesAreSerializedAndTheSameDirectoryIsNotRepeatedOnReopening()
    {
        var entered = Signal(); var release = Signal();
        var source = new Source { Transfer = async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); return "verified"; } };
        using var service = Create(source);
        var first = service.ProvisionAsync(ModelsDirectory);
        await entered.Task;
        var second = service.ProvisionAsync(ModelsDirectory);
        Assert.That(second.IsCompleted, Is.False);
        release.TrySetResult();
        var results = await Task.WhenAll(first, second);
        Assert.That(results[0], Is.SameAs(results[1]));
        Assert.That(source.ListCalls, Is.EqualTo(1));
        Assert.That(source.Transfers, Has.Count.EqualTo(2));
        Assert.That(source.MaximumActive, Is.EqualTo(1));
    }

    [Test]
    public async Task CancellingAWaitingCallerDoesNotCancelTheActiveBatch()
    {
        var entered = Signal(); var release = Signal();
        var source = new Source { Transfer = async (_, token) => { entered.TrySetResult(); await release.Task.WaitAsync(token); return "verified"; } };
        using var service = Create(source);
        var first = service.ProvisionAsync(ModelsDirectory); await entered.Task;
        using var cancellation = new CancellationTokenSource();
        var second = service.ProvisionAsync(ModelsDirectory, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        Assert.ThrowsAsync<OperationCanceledException>(async () => await second);
        release.TrySetResult();
        Assert.That((await first).Packages.All(item => item.Completed), Is.True);
    }

    [Test]
    public async Task OnePackageFailureDoesNotCancelTheOtherAndExplicitRetryRecoversWithoutRestart()
    {
        var fail = true;
        var source = new Source { Transfer = (variant, _) => fail && variant.Repository.Contains("Nano", StringComparison.Ordinal)
            ? Task.FromException<string>(new IOException("network failed")) : Task.FromResult("verified") };
        var operations = new ApplicationOperationService();
        using var service = Create(source, operations);
        var failed = await service.ProvisionAsync(ModelsDirectory);
        Assert.That(failed.Packages.Count(item => item.Completed), Is.EqualTo(1));
        Assert.That(operations.LastCompleted!.Status, Is.EqualTo(ApplicationOperationStatus.Warning));
        Assert.That(operations.LastCompleted.Description, Does.Contain("network failed"));
        Assert.That(await service.ProvisionAsync(ModelsDirectory), Is.SameAs(failed));
        fail = false;
        Assert.That((await service.ProvisionAsync(ModelsDirectory, retry: true)).Packages.All(item => item.Completed), Is.True);
        Assert.That(source.ListCalls, Is.EqualTo(2));
    }

    [Test]
    public async Task CancellingOnePackageOperationDoesNotCancelSmallAndProgressIsVisible()
    {
        var operations = new ApplicationOperationService();
        var progressSeen = false;
        var source = new Source { Transfer = async (variant, token) =>
        {
            if (variant.Repository.Contains("Nano", StringComparison.Ordinal))
            {
                var active = operations.ActiveOperations.First(item => item.Priority == ApplicationOperationPriority.Normal);
                progressSeen = active.Progress == 50;
                operations.Cancel(active.Id);
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            return "verified";
        } };
        using var service = Create(source, operations);
        var result = await service.ProvisionAsync(ModelsDirectory);
        Assert.That(progressSeen, Is.True);
        Assert.That(result.Packages[0].Cancelled, Is.True);
        Assert.That(result.Packages[1].Completed, Is.True);
    }

    [Test]
    public async Task DisposeCancelsActiveAndWaitingCallersWithoutLeavingAnUnobservedOrPendingTask()
    {
        var entered = Signal();
        var source = new Source { Transfer = async (_, token) => { entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return "unreachable"; } };
        using var service = Create(source);
        var active = service.ProvisionAsync(ModelsDirectory); await entered.Task;
        var waiting = service.ProvisionAsync(ModelsDirectory);
        service.Dispose();
        foreach (var task in new[] { active, waiting })
            Assert.CatchAsync<OperationCanceledException>(async () => await task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.ThrowsAsync<ObjectDisposedException>(() => service.ProvisionAsync(ModelsDirectory));
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static SnapdragonModelProvisioner Create(Source source, ApplicationOperationService? operations = null) =>
        new(new SnapdragonRuntimeAvailability(new Probe([Qualcomm]), () => true), source, operations ?? new ApplicationOperationService());

    private sealed class Probe(IReadOnlyList<AiHardwareDevice> devices) : IAiHardwareProbe
    {
        public Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default) => Task.FromResult(devices);
    }

    private sealed class Source : IRemoteModelSource
    {
        private int _active;
        public int MaximumActive { get; private set; }
        public int ListCalls { get; private set; }
        public List<string> Transfers { get; } = [];
        public Func<RemoteModelVariant, CancellationToken, Task<string>>? Transfer { get; init; }
        public IReadOnlyList<Uri> RepositoryUrls => [];
        public Task<IReadOnlyList<RemoteModelVariant>> ListAsync(CancellationToken cancellationToken) { ListCalls++; return Task.FromResult(Publications); }
        public async Task<string> DownloadAsync(RemoteModelVariant variant, string modelsDirectory, IProgress<RemoteModelProgress>? progress, CancellationToken cancellationToken)
        {
            Transfers.Add(variant.Repository);
            MaximumActive = Math.Max(MaximumActive, ++_active);
            try { progress?.Report(new(5, 10, "weights")); return Transfer is null ? "verified" : await Transfer(variant, cancellationToken); }
            finally { _active--; }
        }
    }
}
