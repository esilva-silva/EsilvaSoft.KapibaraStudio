using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Net;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

public sealed class HuggingFaceModelSourceTests
{
    private const string Repository = "esilva/SlopCoder-Mongo-0.5B-ONNX";
    private const string Revision = "9e2775acb8fd226fb085bcc0668c26fa8e1be298";
    // Independent reference vectors: git blob id of "hello\n" and SHA-256 of "abc".
    private const string HelloBlobSha1 = "ce013625030ba8dba906f756967f9e9ca394464a";
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private static readonly RemoteModelRepository Configured = new(Repository, "esilva/SlopCoder-Mongo-0.5B", [new("int8", "qualidade maior")]);

    [Test]
    public async Task ListNamesVariantsByFamilyHardwareAndPrecisionInPublisherOrder()
    {
        using var fixture = new HubFixture();
        using var source = fixture.Source();
        var variants = await source.ListAsync(CancellationToken.None);
        Assert.That(string.Join(",", variants.Select(variant => variant.Variant)), Is.EqualTo("int8,int4"),
            "Configured folders come first; root files and folders without genai_config.json are not models.");
        var int8 = variants[0];
        var int4 = variants[1];
        Assert.That(int8.Title, Is.EqualTo("SlopCoder-Mongo-0.5B — CPU INT8"), "The family comes from the transformers repository, not the export repository.");
        Assert.That(int8.Hint, Is.EqualTo("qualidade maior"));
        Assert.That(int4.Hint, Is.Null);
        Assert.That(int4.BaseModelUrl, Is.EqualTo(new Uri("https://hf.test/esilva/SlopCoder-Mongo-0.5B")));
        Assert.That(int4.FolderName, Is.EqualTo("SlopCoder-Mongo-0.5B-ONNX-int4"));
        Assert.That(int4.Revision, Is.EqualTo(Revision));
        Assert.That(int4.License, Is.EqualTo("deepseek-license"));
        Assert.That(int4.SizeBytes, Is.EqualTo(9));
        Assert.That(string.Join(",", int4.Files.Select(file => file.Path)), Is.EqualTo("int4/genai_config.json,int4/model.onnx.data"), "The escaping path is skipped.");
        Assert.That(int4.Files[1].Sha256, Is.EqualTo(AbcSha256));
    }

    [Test]
    public async Task UnreachableRepositoryDoesNotHideOthersAndUnconfiguredRepositoryIsRejected()
    {
        using var fixture = new HubFixture();
        using var partial = fixture.Source(Configured, new RemoteModelRepository("esilva/Missing-ONNX", null, []));
        var variants = await partial.ListAsync(CancellationToken.None);
        Assert.That(variants, Has.Count.EqualTo(2));
        using var missing = fixture.Source(new RemoteModelRepository("esilva/Missing-ONNX", null, []));
        Assert.ThrowsAsync<HttpRequestException>(() => missing.ListAsync(CancellationToken.None), "Nothing listed at all is reported.");
        var foreign = variants[0] with { Repository = "esilva/Other-ONNX" };
        Assert.ThrowsAsync<InvalidDataException>(() => partial.DownloadAsync(foreign, fixture.Models, null, CancellationToken.None));
    }

    [Test]
    public async Task DownloadVerifiesEveryFileAndInstallsACatalogFolderWithoutOverwriting()
    {
        using var fixture = new HubFixture();
        using var source = fixture.Source();
        var variant = (await source.ListAsync(CancellationToken.None)).Single(item => item.Variant == "int4");
        var reports = new List<RemoteModelProgress>();
        var path = await source.DownloadAsync(variant, fixture.Models, new SynchronousProgress(reports.Add), CancellationToken.None);
        Assert.That(path, Is.EqualTo(Path.Combine(fixture.Models, "SlopCoder-Mongo-0.5B-ONNX-int4")));
        Assert.That(File.ReadAllText(Path.Combine(path, "genai_config.json")), Is.EqualTo("hello\n"));
        Assert.That(File.ReadAllText(Path.Combine(path, "model.onnx.data")), Is.EqualTo("abc"));
        Assert.That(Directory.GetDirectories(fixture.Models), Is.EqualTo(new[] { path }), "No staging folder is left behind.");
        Assert.That(reports[^1].CompletedBytes, Is.EqualTo(variant.SizeBytes));
        Assert.ThrowsAsync<IOException>(() => source.DownloadAsync(variant, fixture.Models, null, CancellationToken.None), "An installed model is never overwritten.");
    }

    [Test]
    public async Task CorruptFileInstallsNothingAndRetryResumesFromVerifiedFiles()
    {
        using var fixture = new HubFixture();
        fixture.Content["int4/model.onnx.data"] = "abd";
        using var source = fixture.Source();
        var variant = (await source.ListAsync(CancellationToken.None)).Single(item => item.Variant == "int4");
        var target = Path.Combine(fixture.Models, variant.FolderName);
        Assert.ThrowsAsync<InvalidDataException>(() => source.DownloadAsync(variant, fixture.Models, null, CancellationToken.None));
        Assert.That(Directory.Exists(target), Is.False);
        Assert.That(Directory.GetFiles(fixture.Models, "*.part", SearchOption.AllDirectories), Is.Empty);

        fixture.Content["int4/model.onnx.data"] = "abc";
        await source.DownloadAsync(variant, fixture.Models, null, CancellationToken.None);
        Assert.That(fixture.Requests["int4/genai_config.json"], Is.EqualTo(1), "A file verified in the failed attempt is not downloaded again.");
        Assert.That(File.ReadAllText(Path.Combine(target, "model.onnx.data")), Is.EqualTo("abc"));
    }

    [Test]
    public async Task CancelledDownloadLeavesNoInstalledFolder()
    {
        using var fixture = new HubFixture();
        using var source = fixture.Source();
        var variant = (await source.ListAsync(CancellationToken.None)).Single(item => item.Variant == "int8");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        Assert.CatchAsync<OperationCanceledException>(() => source.DownloadAsync(variant, fixture.Models, null, cancelled.Token));
        Assert.That(Directory.Exists(Path.Combine(fixture.Models, variant.FolderName)), Is.False);
        Assert.That(await new LocalModelCatalog(fixture.Models).DiscoverAsync(), Is.Empty, "The staging folder is hidden from the catalog.");
    }

    private sealed class SynchronousProgress(Action<RemoteModelProgress> report) : IProgress<RemoteModelProgress>
    {
        public void Report(RemoteModelProgress value) => report(value);
    }

    private sealed class HubFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "slop-hub-" + Guid.NewGuid().ToString("N"));

        public HubFixture()
        {
            Directory.CreateDirectory(Models);
            foreach (var variant in new[] { "int4", "int8" })
            {
                Content[$"{variant}/genai_config.json"] = "hello\n";
                Content[$"{variant}/model.onnx.data"] = "abc";
            }
        }

        public string Models => Path.Combine(_root, "Models");
        public Dictionary<string, string> Content { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, int> Requests { get; } = new(StringComparer.Ordinal);

        public HuggingFaceModelSource Source(params RemoteModelRepository[] repositories) =>
            new(repositories.Length == 0 ? [Configured] : repositories, new Handler(this), new Uri("https://hf.test/"));

        public HttpResponseMessage Respond(Uri uri)
        {
            var url = uri.AbsoluteUri;
            if (url == $"https://hf.test/api/models/{Repository}")
                return Json(new { sha = Revision, cardData = new { license_name = "deepseek-license" } });
            if (url == $"https://hf.test/api/models/{Repository}/tree/{Revision}?recursive=true")
                return Json(new object[]
                {
                    new { type = "directory", oid = new string('0', 40), size = 0, path = "int4" },
                    new { type = "file", oid = HelloBlobSha1, size = 6, path = "README.md" },
                    Blob("int4/genai_config.json"), Lfs("int4/model.onnx.data"),
                    Blob("int8/genai_config.json"), Lfs("int8/model.onnx.data"),
                    Blob("docs/notes.md"), Blob("int4/../escape.txt")
                });
            var prefix = $"https://hf.test/{Repository}/resolve/{Revision}/";
            if (url.StartsWith(prefix, StringComparison.Ordinal))
            {
                var path = Uri.UnescapeDataString(url[prefix.Length..]);
                Requests[path] = Requests.GetValueOrDefault(path) + 1;
                if (Content.TryGetValue(path, out var body)) return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(body)) };
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); }
            catch (IOException) { }
        }

        private static object Blob(string path) => new { type = "file", oid = HelloBlobSha1, size = 6, path };
        private static object Lfs(string path) => new { type = "file", oid = new string('2', 40), size = 3, path, lfs = new { oid = AbcSha256, size = 3, pointerSize = 130 } };
        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }

    private sealed class Handler(HubFixture fixture) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(fixture.Respond(request.RequestUri!));
        }
    }
}
