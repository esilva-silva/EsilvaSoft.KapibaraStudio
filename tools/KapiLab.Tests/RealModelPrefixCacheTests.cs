using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class RealModelPrefixCacheTests
{
    [Test]
    public async Task TextStopAtTheOutputLimitIsACompleteGeneration()
    {
        var package = Environment.GetEnvironmentVariable("KAPILAB_REAL_CPU_MODEL");
        var reference = Environment.GetEnvironmentVariable("KAPI_GENERATION_REFERENCE");
        if (string.IsNullOrEmpty(package) || string.IsNullOrEmpty(reference))
            Assert.Ignore("Prova manual CPU: defina KAPILAB_REAL_CPU_MODEL e KAPI_GENERATION_REFERENCE.");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(reference!));
        var row = json.RootElement.GetProperty("items").EnumerateArray().First(item =>
            item.GetProperty("outputIds").GetArrayLength() > 2
            && item.GetProperty("outputIds").GetArrayLength() < item.GetProperty("maximumTokens").GetInt32());
        var files = new KapiLabModelFileAccess();
        var validation = await new LocalModelCatalog(Path.GetDirectoryName(package), fileAccess: files).ValidateAsync(package!);
        Assert.That(validation.Model, Is.Not.Null, validation.Status.Message);
        await using var runtime = new OnnxLocalModelRuntime(fileAccess: files);
        await runtime.InitializeAsync(validation.Model!, new() { Acceleration = AiAccelerationMode.Cpu });
        var maximum = row.GetProperty("outputIds").GetArrayLength();
        var request = new ModelGenerationRequest("", "", 2048, maximum)
            { PromptTokens = row.GetProperty("promptIds").EnumerateArray().Select(id => id.GetInt32()).ToArray() };
        var truncated = await runtime.GenerateAsync(request);
        Assert.That(truncated.IsComplete, Is.False);
        Assert.That(truncated.Text, Is.Not.Empty);
        var stopped = await runtime.GenerateAsync(request with { StopSequences = [truncated.Text] });
        Assert.That(stopped.Text, Is.EqualTo(truncated.Text));
        Assert.That(stopped.GeneratedTokens, Is.EqualTo(maximum));
        Assert.That(stopped.IsComplete, Is.True, "O marcador textual conclui mesmo ao coincidir com o limite de saída.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PythonGenerationIdsMatchTheIdeIncludingEosAndCachedRepeats(bool useCache)
    {
        var package = Environment.GetEnvironmentVariable("KAPILAB_REAL_CPU_MODEL");
        var reference = Environment.GetEnvironmentVariable("KAPI_GENERATION_REFERENCE");
        if (string.IsNullOrEmpty(package) || string.IsNullOrEmpty(reference))
            Assert.Ignore("Prova manual CPU: defina KAPILAB_REAL_CPU_MODEL e KAPI_GENERATION_REFERENCE.");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(reference!));
        var files = new KapiLabModelFileAccess();
        var validation = await new LocalModelCatalog(Path.GetDirectoryName(package), fileAccess: files).ValidateAsync(package!);
        Assert.That(validation.Model, Is.Not.Null, validation.Status.Message);
        await using var runtime = new OnnxLocalModelRuntime(fileAccess: files, allowUnvalidatedPrefixCache: useCache);
        await runtime.InitializeAsync(validation.Model!, new() { Acceleration = AiAccelerationMode.Cpu, PrefixCacheMode = PrefixCacheMode.Reuse });
        if (json.RootElement.TryGetProperty("ortDllSha256", out var hash) && hash.ValueKind == JsonValueKind.String)
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var ort = process.Modules.Cast<System.Diagnostics.ProcessModule>().Single(module =>
                string.Equals(module.ModuleName, "onnxruntime.dll", StringComparison.OrdinalIgnoreCase));
            var actualHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(ort.FileName)));
            Assert.That(actualHash, Is.EqualTo(hash.GetString()).IgnoreCase, "Python e IDE devem usar a mesma DLL ORT.");
        }
        using var lifetime = new CancellationTokenSource();
        var index = 0;
        foreach (var row in json.RootElement.GetProperty("items").EnumerateArray())
        {
            var input = row.GetProperty("promptIds").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var expected = row.GetProperty("outputIds").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var request = new ModelGenerationRequest("", "", 2048, row.GetProperty("maximumTokens").GetInt32()) { PromptTokens = input };
            var baseline = new List<int>();
            await foreach (var chunk in runtime.StreamAsync(request))
                if (chunk.TokenId is { } id) baseline.Add(id);
            Assert.That(baseline, Is.EqualTo(expected), $"Python × IDE, caso {index}");
            if (!useCache) { index++; continue; }
            var cachedRequest = request with { PrefixCache = new() { Mode = PrefixCacheMode.Reuse, SessionKey = "manual-session", SessionLifetime = lifetime.Token } };
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var actual = new List<int>();
                GeneratedChunk? final = null;
                await foreach (var chunk in runtime.StreamAsync(cachedRequest))
                {
                    if (chunk.TokenId is { } id) actual.Add(id);
                    if (chunk.IsFinal) final = chunk;
                }
                Assert.That(actual, Is.EqualTo(expected), $"Cache × Python, caso {index}, tentativa {attempt}");
                if (attempt == 1) Assert.That(final!.PrefixCacheReusedTokens, Is.EqualTo(Math.Max(0, input.Length - 32)));
            }
            index++;
        }
        Assert.That(index, Is.GreaterThan(0));
        TestContext.WriteLine($"Paridade exata Python/IDE e cache: {index} prompts sintéticos; não substitui homologação de qualidade/latência.");
    }
}
