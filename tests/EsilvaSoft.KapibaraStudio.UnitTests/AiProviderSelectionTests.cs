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
    public void NpuProviderNameAloneDoesNotPromoteAnUnverifiedDevice()
    {
        var withoutDevice = OnnxHardwareProbe.Describe(["CPUExecutionProvider", "QNNExecutionProvider"], []);
        Assert.That(withoutDevice.Single(device => device.Kind == AiAccelerationMode.Npu).IsAvailable, Is.False);
        Assert.That(AiProviderSelector.Plan(new(), withoutDevice).Candidates.Select(candidate => candidate.Kind), Is.EqualTo(CpuOnly));
        var withDevice = OnnxHardwareProbe.Describe(["CPUExecutionProvider", "QNNExecutionProvider"],
            [new("QNNExecutionProvider", AiAccelerationMode.Npu, "Qualcomm NPU")]);
        Assert.That(withDevice.Single(device => device.Kind == AiAccelerationMode.Npu).IsAvailable, Is.True);
        var pluginDevice = OnnxHardwareProbe.Describe(["CPUExecutionProvider"],
            [new("QNNExecutionProvider", AiAccelerationMode.Npu, "Qualcomm NPU") { Vendor = "Qualcomm Technologies, Inc." }]);
        Assert.That(pluginDevice.Single(device => device.Kind == AiAccelerationMode.Npu).Provider, Is.EqualTo("QNN"));
        Assert.That(pluginDevice.Single(device => device.Kind == AiAccelerationMode.Npu).IsAvailable, Is.True);
        Assert.That(pluginDevice.Single(device => device.Kind == AiAccelerationMode.Npu).Vendor, Is.EqualTo("Qualcomm Technologies, Inc."));
        var unrelatedDevice = OnnxHardwareProbe.Describe(["CPUExecutionProvider", "QNNExecutionProvider"],
            [new("QNNExecutionProvider", AiAccelerationMode.Cpu, "QNN CPU backend")]);
        Assert.That(unrelatedDevice.Single(device => device.Kind == AiAccelerationMode.Npu).IsAvailable, Is.False);
    }

    [TestCase("Qualcomm Technologies, Inc.", true)]
    [TestCase(" QUALCOMM ", true)]
    [TestCase("Other Qualcomm", false)]
    [TestCase(null, false)]
    public void QualcommHardwareRequiresAReportedManufacturer(string? vendor, bool expected) =>
        Assert.That(OnnxHardwareProbe.IsQualcommVendor(vendor), Is.EqualTo(expected));

    [Test]
    public void HtpPlanRequiresActualQualcommQnnNpuAndNeverFallsBack()
    {
        var model = CpuOnlyModel() with { IsHtpPipeline = true };
        var unverified = new AiHardwareDevice(AiAccelerationMode.Npu, "QNN", "NPU", true);
        Assert.Throws<AiProviderUnavailableException>(() => AiProviderSelector.Plan(new(), [Cpu, unverified], model));
        var qnn = unverified with { Vendor = "Qualcomm Technologies, Inc." };
        var plan = AiProviderSelector.Plan(new(), [Cpu, qnn], model);
        Assert.That(plan.AllowFallback, Is.False);
        Assert.That(plan.Candidates.Select(candidate => candidate.Kind), Is.EqualTo(new[] { AiAccelerationMode.Npu }));
        Assert.Throws<AiProviderUnavailableException>(() => AiProviderSelector.Plan(new() { Acceleration = AiAccelerationMode.Cpu }, [Cpu, qnn], model));
    }

    [TestCase(2016, 32, true)]
    [TestCase(2017, 32, false)]
    [TestCase(2017, 16, false)]
    [TestCase(1985, 32, true)]
    [TestCase(1, 33, false)]
    public void HtpBudgetAccountsForStaticPrefillPadding(int prompt, int generated, bool accepted)
    {
        if (accepted) Assert.DoesNotThrow(() => OnnxLocalModelRuntime.ValidateHtpBudget(prompt, generated));
        else Assert.Throws<LocalModelContextException>(() => OnnxLocalModelRuntime.ValidateHtpBudget(prompt, generated));
    }

    [Test]
    public void HtpBudgetReportsExcessiveGenerationClearly()
    {
        var error = Assert.Throws<LocalModelContextException>(() => OnnxLocalModelRuntime.ValidateHtpBudget(16, 33));
        Assert.That(error!.Message, Does.Contain("32 tokens"));
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
