using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using System.Net;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class HuggingFaceKapiCoderCatalogTests
{
    [Test, Explicit("Consulta somente metadados públicos do Hugging Face; sem download de pesos."), Category("LocalModelIntegration")]
    public async Task PublishedCatalogListsTheCurrentFamiliesWithoutInstallingWeights()
    {
        // The source publications are public; ONNX export repositories are prepared separately and may not exist yet.
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), OriginalRepositories);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var variants = await source.ListAsync(cancellation.Token);
        foreach (var family in new[] { "KapiCoder-Mongo-Small", "KapiCoder-Mongo-Nano" })
        {
            var published = variants.Single(variant => variant.Family == family && !variant.IsInstallable);
            Assert.That(published.BaseModelUrl, Is.EqualTo(new Uri("https://huggingface.co/esilva/" + family)));
            Assert.That(published.Files, Is.Empty);
            Assert.That(published.SizeBytes, Is.GreaterThan(0));
            Assert.That(published.Revision, Has.Length.EqualTo(40));
            TestContext.WriteLine($"{published.Repository}: revisão {published.Revision}, pesos {published.SizeBytes} bytes, instalação={published.IsInstallable}");
        }
    }

    private const string Small = "esilva/KapiCoder-Mongo-Small";
    private const string Nano = "esilva/KapiCoder-Mongo-Nano";
    private const string SmallRevision = "4ba57bc1f56a5b3cbe486dd314321ce4cdb2cae6";
    private const string NanoRevision = "c03bfe15ba1ff4e5b58a4b0953349eca82bb43f1";
    private const string BlobSha1 = "ce013625030ba8dba906f756967f9e9ca394464a";
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private static readonly RemoteModelRepository[] OriginalRepositories = [new(Small, Small, []), new(Nano, Nano, [])];
    private static readonly string[] ExpectedRepositoryUrls = ["https://huggingface.co/" + Small, "https://huggingface.co/" + Nano];
    private static readonly string[] ExpectedFamilies = ["KapiCoder-Mongo-Small", "KapiCoder-Mongo-Nano"];
    private static readonly string[] ExpectedRevisions = [SmallRevision, NanoRevision];
    private static readonly long[] ExpectedWeightSizes = [1_234, 567];
    private static readonly string[] ExpectedOnnxFiles = ["int4/genai_config.json", "int4/model.onnx.data"];
    private static readonly string[] ExpectedExportVariants = ["cpu-int4", "cpu-int8", "dml-fp16", "cpu-int8", "dml-fp16"];
    private static readonly string[] ExpectedExportTitles =
    [
        "KapiCoder-Mongo-Nano — CPU INT4", "KapiCoder-Mongo-Nano — CPU INT8", "KapiCoder-Mongo-Nano — GPU DirectML FP16",
        "KapiCoder-Mongo-Small — CPU INT8", "KapiCoder-Mongo-Small — GPU DirectML FP16"
    ];

    [Test]
    public async Task DefaultOnnxCatalogPrioritizesNanoCpuInt4AndRetainsSourceCards()
    {
        using var handler = new KapiCoderOnnxCatalogHandler();
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), handler: handler);

        var variants = await source.ListAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(source.RepositoryUrls[0], Is.EqualTo(new Uri("https://huggingface.co/esilva/KapiCoder-Mongo-Nano-ONNX")));
            Assert.That(source.RepositoryUrls[1], Is.EqualTo(new Uri("https://huggingface.co/esilva/KapiCoder-Mongo-Small-ONNX")));
            Assert.That(variants.Select(variant => variant.Variant), Is.EqualTo(ExpectedExportVariants));
            Assert.That(variants.Select(variant => variant.Title), Is.EqualTo(ExpectedExportTitles));
            Assert.That(variants.All(variant => variant.IsInstallable), Is.True);
            Assert.That(variants[0].BaseModelUrl, Is.EqualTo(new Uri("https://huggingface.co/esilva/KapiCoder-Mongo-Nano")));
            Assert.That(variants[^1].BaseModelUrl, Is.EqualTo(new Uri("https://huggingface.co/esilva/KapiCoder-Mongo-Small")));
            Assert.That(variants[0].HintKey, Is.EqualTo("remoteHintCpuInt4Default"));
            Assert.That(variants[1].HintKey, Is.EqualTo("remoteHintCpuInt8Alternative"));
            Assert.That(variants[2].HintKey, Is.EqualTo("remoteHintDirectMlFp16"));
            Assert.That(variants[2].Hint, Does.Contain("experimental"));
            Assert.That(handler.Requests, Has.Count.EqualTo(6), "Only metadata is requested; unavailable pinned publications must not hide the default exports.");
        });
        foreach (var variant in variants)
        {
            Assert.That(variant.License, Is.EqualTo("apache-2.0"));
            Assert.That(variant.Files, Has.Count.EqualTo(2));
            Assert.That(variant.Files[0].GitBlobSha1, Is.EqualTo(BlobSha1));
            Assert.That(variant.Files[1].Sha256, Is.EqualTo(AbcSha256));
        }
    }

    [Test]
    public async Task DefaultNanoCpuInt4InstallsFromThePinnedExportWithoutDownloadingOtherVariants()
    {
        using var root = new SyntheticDirectory();
        using var handler = new KapiCoderOnnxCatalogHandler();
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), handler: handler);
        var selected = (await source.ListAsync(CancellationToken.None))[0];

        var path = await source.DownloadAsync(selected, root.Path, null, CancellationToken.None);

        Assert.That(path, Is.EqualTo(Path.Combine(root.Path, "KapiCoder-Mongo-Nano-ONNX-cpu-int4")));
        Assert.That(File.ReadAllText(Path.Combine(path, "genai_config.json")), Is.EqualTo("hello\n"));
        Assert.That(File.ReadAllText(Path.Combine(path, "model.onnx.data")), Is.EqualTo("abc"));
        Assert.That(handler.Requests.Count(request => request.Contains("/resolve/", StringComparison.Ordinal)), Is.EqualTo(2));
        Assert.That(handler.Requests, Does.Contain($"esilva/KapiCoder-Mongo-Nano-ONNX/resolve/{KapiCoderOnnxCatalogHandler.NanoRevision}/cpu-int4/genai_config.json"));
    }

    [Test]
    public async Task ConfiguredSourcePublicationsShowBothFamiliesWhenOnlySafetensorsArePublished()
    {
        using var handler = new CatalogHandler();
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), OriginalRepositories, handler);

        var variants = await source.ListAsync(CancellationToken.None);

        Assert.That(source.RepositoryUrls.Select(uri => uri.AbsoluteUri), Is.EqualTo(ExpectedRepositoryUrls));
        Assert.That(variants.Select(variant => variant.Family), Is.EqualTo(ExpectedFamilies));
        Assert.That(variants.Select(variant => variant.Revision), Is.EqualTo(ExpectedRevisions));
        Assert.That(variants.Select(variant => variant.SizeBytes), Is.EqualTo(ExpectedWeightSizes));
        foreach (var variant in variants)
        {
            Assert.Multiple(() =>
            {
                Assert.That(variant.IsInstallable, Is.False);
                Assert.That(variant.Variant, Is.EqualTo("safetensors"));
                Assert.That(variant.Files, Is.Empty, "An incompatible publication must never be passed to the installer as downloadable weights.");
                Assert.That(variant.HintKey, Is.EqualTo("remoteHintRequiresOnnx"));
                Assert.That(variant.Hint, Does.Contain("ONNX GenAI"));
                Assert.That(variant.License, Is.EqualTo("apache-2.0"));
                Assert.That(variant.PageUrl, Is.EqualTo(new Uri($"https://huggingface.co/{variant.Repository}/tree/{variant.Revision}")));
                Assert.That(variant.BaseModelUrl, Is.EqualTo(new Uri("https://huggingface.co/" + variant.Repository)));
            });
        }
        Assert.That(handler.Requests, Has.Count.EqualTo(4), "Listing needs only public metadata and the pinned tree, never model weights or credentials.");
        Assert.That(handler.Requests, Does.Contain($"api/models/{Small}/tree/{SmallRevision}?recursive=true"));
        Assert.That(handler.Requests, Does.Contain($"api/models/{Nano}/tree/{NanoRevision}?recursive=true"));
    }

    [Test]
    public async Task SafetensorsCannotBeInstalledEvenWhenTheDownloaderIsCalledDirectly()
    {
        using var handler = new CatalogHandler();
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), OriginalRepositories, handler);
        var variants = await source.ListAsync(CancellationToken.None);
        var requestsBeforeDownload = handler.Requests.Count;

        foreach (var variant in variants)
        {
            var error = Assert.ThrowsAsync<InvalidDataException>(() => source.DownloadAsync(variant, "unused-model-directory", null, CancellationToken.None));
            Assert.That(error!.Message, Does.Contain("ONNX GenAI"));
            Assert.ThrowsAsync<InvalidDataException>(() => source.DownloadAsync(variant with { IsInstallable = true }, "unused-model-directory", null, CancellationToken.None),
                "Changing the informational flag cannot create an empty model installation.");
        }
        Assert.That(handler.Requests, Has.Count.EqualTo(requestsBeforeDownload));
    }

    [Test]
    public async Task FutureOnnxVariantsCoexistWithTheIncompatibleOriginalPublication()
    {
        using var handler = new CatalogHandler { IncludeOnnx = true };
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), OriginalRepositories, handler);

        var variants = await source.ListAsync(CancellationToken.None);
        var onnx = variants.Single(variant => variant.IsInstallable);

        Assert.Multiple(() =>
        {
            Assert.That(variants, Has.Count.EqualTo(3));
            Assert.That(variants.Count(variant => !variant.IsInstallable), Is.EqualTo(2), "Finding an export must not hide the original publication's incompatible format.");
            Assert.That(onnx.Repository, Is.EqualTo(Small));
            Assert.That(onnx.Family, Is.EqualTo("KapiCoder-Mongo-Small"));
            Assert.That(onnx.Variant, Is.EqualTo("int4"));
            Assert.That(onnx.FolderName, Is.EqualTo("KapiCoder-Mongo-Small-int4"));
            Assert.That(onnx.Revision, Is.EqualTo(SmallRevision));
            Assert.That(onnx.SizeBytes, Is.EqualTo(9));
            Assert.That(onnx.Files.Select(file => file.Path), Is.EqualTo(ExpectedOnnxFiles));
            Assert.That(onnx.Files[0].GitBlobSha1, Is.EqualTo(BlobSha1));
            Assert.That(onnx.Files[1].Sha256, Is.EqualTo(AbcSha256));
            Assert.That(onnx.PageUrl, Is.EqualTo(new Uri($"https://huggingface.co/{Small}/tree/{SmallRevision}/int4")));
        });
    }

    [Test]
    public async Task UnreachableFamilyDoesNotHideTheOtherFamilysIncompatiblePublication()
    {
        using var handler = new CatalogHandler { MissingSmall = true };
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), OriginalRepositories, handler);

        var variant = (await source.ListAsync(CancellationToken.None)).Single();

        Assert.That(variant.Repository, Is.EqualTo(Nano));
        Assert.That(variant.IsInstallable, Is.False);
        Assert.That(variant.HintKey, Is.EqualTo("remoteHintRequiresOnnx"));
    }

    [Test]
    public async Task OverflowingWeightSizesAreReportedAsInvalidDataAndDoNotHideOtherRepositories()
    {
        using var handler = new CatalogHandler { OverflowSmallWeightSizes = true };
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), OriginalRepositories, handler);

        var variant = (await source.ListAsync(CancellationToken.None)).Single();

        Assert.That(variant.Repository, Is.EqualTo(Nano));
        using var malformedOnly = new HuggingFaceModelSource(new RemoteModelStorage(), [new RemoteModelRepository(Small, Small, [])], handler);
        var error = Assert.ThrowsAsync<InvalidDataException>(() => malformedOnly.ListAsync(CancellationToken.None));
        Assert.That(error!.InnerException, Is.TypeOf<OverflowException>());
    }

    [Test]
    public async Task CustomRepositoryKeepsItsBaseCardAndSafetensorsShardSizes()
    {
        const string custom = "custom/model";
        using var handler = new CatalogHandler { CustomRepository = custom };
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(),
            [new RemoteModelRepository(custom, "custom/original", [])], handler);

        var variant = (await source.ListAsync(CancellationToken.None)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(variant.IsInstallable, Is.False);
            Assert.That(variant.Family, Is.EqualTo("original"));
            Assert.That(variant.SizeBytes, Is.EqualTo(567));
            Assert.That(variant.BaseModelUrl, Is.EqualTo(new Uri("https://huggingface.co/custom/original")));
        });
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        // Revisions and license match public metadata; sizes and file hashes below are synthetic.
        public bool IncludeOnnx { get; init; }
        public bool MissingSmall { get; init; }
        public bool OverflowSmallWeightSizes { get; init; }
        public string? CustomRepository { get; init; }
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.That(request.Headers.Authorization, Is.Null);
            var relative = request.RequestUri!.PathAndQuery.TrimStart('/');
            Requests.Add(relative);
            foreach (var repository in new[] { Small, Nano, CustomRepository }.OfType<string>())
            {
                if (repository == Small && MissingSmall) continue;
                var revision = repository == Small ? SmallRevision : NanoRevision;
                if (relative == $"api/models/{repository}")
                    return Task.FromResult(Json(new { sha = revision, cardData = new { license = "apache-2.0" } }));
                if (relative != $"api/models/{repository}/tree/{revision}?recursive=true") continue;
                var tree = new List<object>
                {
                    Blob("README.md", 6), Blob("config.json", 6), Blob("tokenizer.json", 6),
                    Blob("../escape.safetensors", 100_000)
                };
                if (repository == Small)
                {
                    tree.Add(Blob("model.safetensors", 1_234));
                    if (OverflowSmallWeightSizes) tree.Add(Blob("extra.safetensors", long.MaxValue));
                }
                else
                {
                    tree.Add(Blob("model-00001-of-00002.safetensors", 400));
                    tree.Add(Blob("model-00002-of-00002.safetensors", 167));
                }
                if (repository == Small && IncludeOnnx)
                {
                    tree.Add(Blob("int4/genai_config.json", 6));
                    tree.Add(new { type = "file", oid = new string('2', 40), size = 3, path = "int4/model.onnx.data", lfs = new { oid = AbcSha256 } });
                }
                return Task.FromResult(Json(tree));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static object Blob(string path, long size) => new { type = "file", oid = BlobSha1, size, path };
        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}

/// <summary>Synthetic public hub responses; the export repositories do not need to be published to exercise the catalog.</summary>
internal sealed class KapiCoderOnnxCatalogHandler : HttpMessageHandler
{
    internal const string NanoRevision = "1111111111111111111111111111111111111111";
    private const string SmallRevision = "2222222222222222222222222222222222222222";
    private const string BlobSha1 = "ce013625030ba8dba906f756967f9e9ca394464a";
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private static readonly string[] NanoFolders = ["dml-fp16", "cpu-int8", "cpu-int4"];
    private static readonly string[] SmallFolders = ["dml-fp16", "cpu-int8"];
    private static readonly string[] Families = ["Nano", "Small"];
    public bool UseRepresentativePackageSizes { get; init; }
    public List<string> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Assert.That(request.Headers.Authorization, Is.Null);
        var relative = request.RequestUri!.PathAndQuery.TrimStart('/');
        Requests.Add(relative);
        foreach (var family in Families)
        {
            var repository = $"esilva/KapiCoder-Mongo-{family}-ONNX";
            var revision = family == "Nano" ? NanoRevision : SmallRevision;
            if (relative == $"api/models/{repository}")
                return Task.FromResult(Json(new { sha = revision, cardData = new { license = "apache-2.0" } }));
            if (relative == $"api/models/{repository}/tree/{revision}?recursive=true")
            {
                var folders = family == "Nano" ? NanoFolders : SmallFolders;
                var tree = folders.SelectMany(folder => new object[]
                {
                    new { type = "file", path = folder + "/genai_config.json", size = 6, oid = BlobSha1 },
                    new { type = "file", path = folder + "/model.onnx.data", size = UseRepresentativePackageSizes ? RepresentativePackageSize(family, folder) - 6 : 3, oid = new string('a', 40), lfs = new { oid = AbcSha256 } }
                });
                return Task.FromResult(Json(tree));
            }
            if (relative.StartsWith($"{repository}/resolve/{revision}/", StringComparison.Ordinal))
            {
                if (UseRepresentativePackageSizes) throw new InvalidOperationException("The UI fixture lists metadata only; model downloads are not allowed.");
                var body = relative.EndsWith("/genai_config.json", StringComparison.Ordinal) ? "hello\n" : "abc";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(body)) });
            }
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    // Totals represent the staged package snapshots for UI sizing. These are synthetic tree entries, not individual file sizes or weights.
    private static long RepresentativePackageSize(string family, string folder) => (family, folder) switch
    {
        ("Nano", "cpu-int4") => 410_666_723,
        ("Nano", "cpu-int8") => 1_123_062_556,
        ("Nano", "dml-fp16") => 1_005_607_530,
        ("Small", "cpu-int8") => 2_703_298_435,
        ("Small", "dml-fp16") => 3_107_611_211,
        _ => throw new ArgumentOutOfRangeException(nameof(folder))
    };

    private static HttpResponseMessage Json(object value) =>
        new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}
