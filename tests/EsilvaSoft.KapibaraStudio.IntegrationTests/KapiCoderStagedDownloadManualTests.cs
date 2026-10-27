using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.SystemAdapters;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>Manual roundtrip of real staged files; only the Hub transport is simulated.</summary>
[TestFixture, Category("Integration"), Category("LocalModelIntegration")]
public sealed class KapiCoderStagedDownloadManualTests
{
    [TestCase("nano-onnx", "cpu-int4", "esilva/KapiCoder-Mongo-Nano-ONNX", "esilva/KapiCoder-Mongo-Nano")]
    [TestCase("small-onnx", "cpu-int8", "esilva/KapiCoder-Mongo-Small-ONNX", "esilva/KapiCoder-Mongo-Small")]
    [Explicit("Copia e verifica pesos reais do staging; requer KAPI_ONNX_STAGE_ROOT e espaço livre temporário.")]
    public async Task StagedPackageDownloadsAndValidates(string stageDirectory, string variantName, string repository, string baseRepository)
    {
        var stageRoot = Environment.GetEnvironmentVariable("KAPI_ONNX_STAGE_ROOT");
        Assert.That(stageRoot, Is.Not.Null.And.Not.Empty, "Defina KAPI_ONNX_STAGE_ROOT para a raiz do staging.");
        var variantRoot = Path.Combine(stageRoot!, stageDirectory, variantName);
        Assert.That(Directory.Exists(variantRoot), Is.True, $"Variante ausente: {variantRoot}");
        Assert.That(File.Exists(Path.Combine(variantRoot, "kapibarastudio-model.json")), Is.True, "Manifesto do modelo ausente no staging.");

        var entries = new Dictionary<string, StagedFile>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(variantRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(variantRoot, path).Replace('\\', '/');
            await using var stream = File.OpenRead(path);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));
            entries.Add($"{variantName}/{relative}", new StagedFile(path, new FileInfo(path).Length, hash));
        }
        Assert.That(entries.ContainsKey($"{variantName}/genai_config.json"), Is.True);
        Assert.That(entries.Values.Any(file => file.Length > 1_000_000), Is.True, "O staging deve conter pesos reais.");

        using var handler = new StagedHubHandler(repository, entries);
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(),
            [new RemoteModelRepository(repository, baseRepository, [])], handler, new Uri("https://stage.test/"));
        using var temporary = new SyntheticDirectory();
        var models = Path.Combine(temporary.Path, "Models");
        var variants = await source.ListAsync(CancellationToken.None);
        var variant = variants.Single(item => item.Variant == variantName);
        Assert.That(variant.IsInstallable, Is.True);
        Assert.That(variant.Files, Has.Count.EqualTo(entries.Count));

        var installed = await source.DownloadAsync(variant, models, null, CancellationToken.None);
        Assert.That(installed, Is.EqualTo(Path.Combine(models, variant.FolderName)));
        Assert.That(Directory.GetDirectories(models), Is.EqualTo(new[] { installed }));
        foreach (var file in variant.Files)
        {
            var expected = entries[file.Path];
            var local = Path.Combine(installed, file.Path[(variantName.Length + 1)..].Replace('/', Path.DirectorySeparatorChar));
            Assert.That(new FileInfo(local).Length, Is.EqualTo(expected.Length), file.Path);
            await using var stream = File.OpenRead(local);
            Assert.That(Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)), Is.EqualTo(expected.Sha256), file.Path);
        }

        // Check the package manifest against the independently computed staging hashes and downloaded files.
        await using var manifestStream = File.OpenRead(Path.Combine(installed, "kapibarastudio-model.json"));
        using var manifest = await JsonDocument.ParseAsync(manifestStream);
        Assert.That(manifest.RootElement.ValueKind, Is.EqualTo(JsonValueKind.Object));
        await using var integrityStream = File.OpenRead(Path.Combine(installed, "kapicoder_manifest.json"));
        using var integrity = await JsonDocument.ParseAsync(integrityStream);
        Assert.That(integrity.RootElement.GetProperty("schema").GetString(), Is.EqualTo("kapilab-kapicoder-manifest-v1"));
        var listed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in integrity.RootElement.GetProperty("files").EnumerateArray())
        {
            var relative = item.GetProperty("path").GetString()!;
            Assert.That(listed.Add(relative), Is.True, $"Arquivo duplicado no manifesto: {relative}");
            Assert.That(relative, Is.Not.EqualTo("kapicoder_manifest.json"));
            Assert.That(entries.TryGetValue($"{variantName}/{relative}", out var staged), Is.True, $"Arquivo do manifesto ausente: {relative}");
            Assert.That(item.GetProperty("sha256").GetString(), Is.EqualTo(staged!.Sha256).IgnoreCase, relative);
            Assert.That(item.GetProperty("bytes").GetInt64(), Is.EqualTo(staged.Length), relative);
        }
        Assert.That(listed, Is.Not.Empty);
        var catalog = new LocalModelCatalog(models, fileAccess: new LocalModelFileAccess());
        var validation = await catalog.ValidateAsync(installed);
        Assert.That(validation.Validity, Is.EqualTo(LocalModelValidity.Valid), validation.Status.Message);
        Assert.That(validation.Model, Is.Not.Null);
        Assert.That((await catalog.DiscoverAsync()).Select(item => item.Path), Does.Contain(installed));
    }

    private sealed record StagedFile(string Path, long Length, string Sha256);

    private sealed class StagedHubHandler(string repository, IReadOnlyDictionary<string, StagedFile> entries) : HttpMessageHandler
    {
        private const string Revision = "0123456789abcdef0123456789abcdef01234567";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = request.RequestUri!.AbsoluteUri;
            if (url == $"https://stage.test/api/models/{repository}")
                return Task.FromResult(Json(new { sha = Revision, cardData = new { license = "test-stage" } }));
            if (url == $"https://stage.test/api/models/{repository}/tree/{Revision}?recursive=true")
                return Task.FromResult(Json(entries.Select(entry => new
                {
                    type = "file", path = entry.Key, oid = new string('0', 40), size = entry.Value.Length,
                    lfs = new { oid = entry.Value.Sha256, size = entry.Value.Length }
                }).ToArray()));
            var prefix = $"https://stage.test/{repository}/resolve/{Revision}/";
            if (url.StartsWith(prefix, StringComparison.Ordinal) && entries.TryGetValue(Uri.UnescapeDataString(url[prefix.Length..]), out var file))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(File.OpenRead(file.Path)) });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
