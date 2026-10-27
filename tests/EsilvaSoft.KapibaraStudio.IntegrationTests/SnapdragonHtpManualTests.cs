using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, NonParallelizable, Category("Integration"), Category("LocalModelIntegration")]
public sealed class SnapdragonHtpManualTests
{
    [Test, Explicit("Requer Windows ARM64, NPU Qualcomm e modelos HTP já baixados."), CancelAfter(300_000)]
    public async Task RealHtpPackagesLoadAndGenerateOnReportedQualcommNpu()
    {
        var root = Environment.GetEnvironmentVariable("KAPI_SNAPDRAGON_MODELS_ROOT");
        Assert.That(root, Is.Not.Null.And.Not.Empty);
        var hardware = new OnnxHardwareProbe();
        var devices = await hardware.GetAvailableHardwareAsync(TestContext.CurrentContext.CancellationToken);
        Assert.That(devices.Any(device => device.IsAvailable && device.Kind == AiAccelerationMode.Npu && device.Provider == "QNN"
            && device.Vendor?.Contains("Qualcomm", StringComparison.OrdinalIgnoreCase) == true), Is.True, "NPU QNN Qualcomm não reportada.");
        var catalog = new LocalModelCatalog(root, fileAccess: new LocalModelFileAccess());
        foreach (var family in new[] { "Nano", "Small" })
        {
            var path = Path.Combine(root!, $"KapiCoder-Mongo-{family}-ONNX-htp-fim-exact-v1");
            var validation = await catalog.ValidateAsync(path, TestContext.CurrentContext.CancellationToken);
            Assert.That(validation.Validity, Is.EqualTo(LocalModelValidity.Valid), validation.Status.Message);
            Assert.That(validation.Model!.IsHtpPipeline, Is.True);
            await using var runtime = new OnnxLocalModelRuntime(hardware: hardware, fileAccess: new LocalModelFileAccess());
            await runtime.InitializeAsync(validation.Model, new AutocompleteSettings { Acceleration = AiAccelerationMode.Npu }, TestContext.CurrentContext.CancellationToken);
            Assert.That(runtime.RuntimeInfo?.Backend, Is.EqualTo(AiAccelerationMode.Npu));
            Assert.That(runtime.RuntimeInfo?.Provider, Is.EqualTo("QNN"));
            Assert.That(runtime.RuntimeInfo?.UsedFallback, Is.False);
            var request = new ModelGenerationRequest("db.clientes.find({", "})", 2048, 24);
            var whole = await runtime.GenerateAsync(request, TestContext.CurrentContext.CancellationToken);
            var chunks = new List<GeneratedChunk>();
            await foreach (var chunk in runtime.StreamAsync(request, TestContext.CurrentContext.CancellationToken)) chunks.Add(chunk);
            Assert.That(chunks, Is.Not.Empty);
            Assert.That(chunks[^1].IsFinal, Is.True);
            Assert.That(string.Concat(chunks.Select(chunk => chunk.Text)), Is.EqualTo(whole.Text));
            Assert.That(whole.GeneratedTokens, Is.InRange(0, 24));
            TestContext.WriteLine($"{family}: QNN, tokens={whole.GeneratedTokens}, fallback={whole.UsedCpuFallback}.");
        }
    }
}
