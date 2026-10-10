using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class RealModelInferenceCancellationTests
{
    [Test]
    public async Task CancellingAfterARealTokenStopsNativeGenerationWithoutFinalChunk()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("A validação manual Linux permanece fora da meta executável deste chat.");
        var package = Environment.GetEnvironmentVariable("KAPILAB_REAL_CPU_MODEL");
        if (string.IsNullOrWhiteSpace(package) || !Directory.Exists(package))
            Assert.Ignore("Defina KAPILAB_REAL_CPU_MODEL para habilitar a prova local com pesos reais.");

        package = Path.GetFullPath(package);
        var files = new KapiLabModelFileAccess();
        var catalog = new LocalModelCatalog(Path.GetDirectoryName(package), fileAccess: files);
        var validation = await catalog.ValidateAsync(package);
        Assert.That(validation.Status.State, Is.EqualTo(LocalModelState.Available),
            "A integração exige um pacote real que passe pelo catálogo da IDE.");
        Assert.That(validation.Model, Is.Not.Null);

        var settings = new AutocompleteSettings
        {
            ModelPath = package,
            Acceleration = AiAccelerationMode.Cpu,
            ContextTokens = 4096,
            MaximumCompletionTokens = 4096,
        }.Validate();
        await using var runtime = new OnnxLocalModelRuntime(fileAccess: files, hardware: new OnnxHardwareProbe());
        await runtime.InitializeAsync(validation.Model!, settings);
        Assert.That(runtime.RuntimeInfo?.Backend, Is.EqualTo(AiAccelerationMode.Cpu),
            "A prova é restrita ao backend CPU para não depender de coordenação GPU.");

        using var cancellation = new CancellationTokenSource();
        await using var chunks = runtime.StreamAsync(new ModelGenerationRequest("db.", "", 4096, 4096), cancellation.Token)
            .GetAsyncEnumerator(cancellation.Token);
        Assert.That(await chunks.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromMinutes(2)), Is.True,
            "A inferência real deve produzir pelo menos um pedaço antes do cancelamento.");
        Assert.That(chunks.Current.IsFinal, Is.False, "O primeiro pedaço deve ocorrer durante a geração nativa.");

        cancellation.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () =>
            await chunks.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30)));
    }
}
