using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalAiModelServiceRuntimeIntegrationTests
{
    [Test, Explicit("Consulta o ONNX Runtime nativo desta máquina."), Category("LocalModelIntegration")]
    public void RealHardwareProbeAlwaysReportsCpu()
    {
        var devices = OnnxHardwareProbe.Detect();
        foreach (var device in devices) TestContext.WriteLine(LocalAiStatusFormatter.DeviceLine(device) + (device.Reason is null ? "" : " · " + device.Reason));
        Assert.That(devices.Single(device => device.Kind == AiAccelerationMode.Cpu).IsAvailable, Is.True);
    }

    [TestCase(AiAccelerationMode.Cpu)]
    [TestCase(AiAccelerationMode.Gpu)]
    [TestCase(AiAccelerationMode.Auto)]
    [Explicit("Defina SLOP_QWEN_MODEL para uma pasta de modelo ONNX GenAI externa."), Category("LocalModelIntegration")]
    public async Task RealModelTestRunsOnTheRequestedHardware(AiAccelerationMode hardware)
    {
        var path = Environment.GetEnvironmentVariable("SLOP_QWEN_MODEL");
        Assert.That(path, Is.Not.Null.And.Not.Empty);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path!));
        var probe = new OnnxHardwareProbe();
        await using var service = new LocalAiModelService(new LocalModelCatalog(Path.GetDirectoryName(root), fileAccess: new LocalModelFileAccess()), () => new OnnxLocalModelRuntime(hardware: probe, fileAccess: new LocalModelFileAccess()), probe);
        var report = await service.TestModelAsync(new() { SelectedModel = Path.GetFileName(root), Acceleration = hardware });
        TestContext.WriteLine(LocalAiStatusFormatter.FormatReport(report));
        Assert.That(report.Succeeded, Is.True, report.Message);
        if (hardware != AiAccelerationMode.Auto) Assert.That(report.Backend, Is.EqualTo(hardware));
    }
}
