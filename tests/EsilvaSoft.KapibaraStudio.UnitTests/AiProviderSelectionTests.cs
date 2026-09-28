using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AiProviderSelectionTests
{
    private static readonly AiHardwareDevice Cpu = new(AiAccelerationMode.Cpu, "CPU", "Test CPU", true);
    private static readonly AiHardwareDevice Gpu = new(AiAccelerationMode.Gpu, "DirectML", "Test GPU", true);
    private static readonly AiHardwareDevice MissingGpu = new(AiAccelerationMode.Gpu, "", "GPU", false) { Reason = "DirectML provider unavailable." };
    private static readonly AiHardwareDevice Npu = new(AiAccelerationMode.Npu, "QNN", "Test NPU", true);
    private static readonly AiHardwareDevice MissingNpu = new(AiAccelerationMode.Npu, "", "NPU", false);
    private static readonly AiAccelerationMode[] AutomaticOrder = [AiAccelerationMode.Npu, AiAccelerationMode.Gpu, AiAccelerationMode.Cpu];
    private static readonly AiAccelerationMode[] CpuOnly = [AiAccelerationMode.Cpu];
    private static readonly string[] ProbeLines = ["CPU — Ryzen", "GPU — RX 7800 XT (DirectML, 15,8 GB)", "NPU — indisponível"];
    private static readonly string[] IdleStatusLines = ["Modelo selecionado: SlopCoder-Mongo-1.5B", "Estado: Não carregado", "Hardware: Automático",
        "Provider detectado: DirectML", "Dispositivo: Test GPU", "Modelo ainda não carregado; será validado sob demanda."];

    [Test]
    public void AutomaticTriesNpuGpuThenCpuAmongAvailableAndCompatibleDevices()
    {
        var automatic = AiProviderSelector.Plan(new(), [Cpu, Gpu, Npu]);
        Assert.That(automatic.AllowFallback, Is.True);
        Assert.That(automatic.Candidates.Select(candidate => candidate.Kind), Is.EqualTo(AutomaticOrder));
        Assert.That(AiProviderSelector.Plan(new(), [Cpu, MissingGpu, MissingNpu]).Candidates.Single().GenAiName, Is.EqualTo("cpu"));
        Assert.That(AiProviderSelector.Plan(new(), [Cpu, Gpu, Npu], CpuOnlyModel()).Candidates.Select(candidate => candidate.Kind), Is.EqualTo(CpuOnly));
    }

    [Test]
    public void ExplicitHardwareUsesOnlyThatBackendAndExplainsUnavailability()
    {
        var gpu = AiProviderSelector.Plan(new() { Acceleration = AiAccelerationMode.Gpu }, [Cpu, Gpu]);
        Assert.That(gpu.AllowFallback, Is.False);
        Assert.That(gpu.Candidates.Single(), Is.EqualTo(new AiProviderCandidate(AiAccelerationMode.Gpu, "DirectML", "dml", "Test GPU")));
        var unavailable = Assert.Throws<AiProviderUnavailableException>(() => AiProviderSelector.Plan(new() { Acceleration = AiAccelerationMode.Gpu }, [Cpu, MissingGpu]))!;
        Assert.That(unavailable.Message, Is.EqualTo("Não foi possível executar este modelo utilizando GPU.\nMotivo: DirectML provider unavailable.\nVocê pode selecionar: Automático ou CPU."));
        Assert.Throws<AiProviderUnavailableException>(() => AiProviderSelector.Plan(new() { Acceleration = AiAccelerationMode.Npu }, [Cpu, Gpu, MissingNpu]));
        Assert.Throws<AiProviderUnavailableException>(() => AiProviderSelector.Plan(new() { ExecutionProvider = AiExecutionProvider.Cuda }, [Cpu, Gpu]));
        Assert.Throws<AiProviderUnavailableException>(() => AiProviderSelector.Plan(new() { Acceleration = AiAccelerationMode.Gpu }, [Cpu, Gpu], CpuOnlyModel()));
    }

    [Test]
    public void ProbeOffersOnlyProvidersTheRuntimeReportsAndPrefersTheFirstAdapter()
    {
        var devices = OnnxHardwareProbe.Describe(["DmlExecutionProvider", "CPUExecutionProvider"],
        [
            new("CPUExecutionProvider", AiAccelerationMode.Cpu, "Ryzen"),
            new("DmlExecutionProvider", AiAccelerationMode.Gpu, "Integrated", 485L << 20, 1),
            new("DmlExecutionProvider", AiAccelerationMode.Gpu, "RX 7800 XT", 16177L << 20, 0)
        ]);
        Assert.That(devices.Select(device => LocalAiStatusFormatter.DeviceLine(device)), Is.EqualTo(ProbeLines));
        var cpuBuild = OnnxHardwareProbe.Describe(["CPUExecutionProvider"], []);
        Assert.That(cpuBuild.Single(device => device.Kind == AiAccelerationMode.Cpu).IsAvailable, Is.True);
        Assert.That(cpuBuild.Single(device => device.Kind == AiAccelerationMode.Gpu).Reason, Does.Contain("DirectML ou CUDA"));
    }

    [Test]
    public void StatusBeforeLoadingNamesSelectionStateHardwareAndDetectedProvider()
    {
        var text = LocalAiStatusFormatter.Format(new(LocalModelState.NotLoaded, "Modelo ainda não carregado; será validado sob demanda."),
            "SlopCoder-Mongo-1.5B", AiAccelerationMode.Auto, [Cpu, Gpu, MissingNpu]);
        Assert.That(text.Split('\n'), Is.EqualTo(IdleStatusLines));
    }

    internal static LocalModelDefinition CpuOnlyModel() =>
        new("cpu-only", "CPU only", "models", "Qwen2.5-Coder") { Metadata = new() { Hardware = CpuOnly } };
}
