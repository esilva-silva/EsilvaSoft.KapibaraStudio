using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.SystemAdapters;
using System.Net;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture, Category("Integration")]
public sealed class HuggingFaceSnapdragonCatalogTests
{
    private const string NanoRevision = "a85d11c9f8db2f4388836c991b292b1cdb160d39";
    private const string SmallRevision = "c03ba2339ba263a3881134d0acd3ee24e1a80717";
    private const string Folder = "htp-fim-exact-v1";
    private const string BlobHash = "ce013625030ba8dba906f756967f9e9ca394464a";
    private const string LfsHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private static readonly string[] LegacyFolders = ["cpu-int4", "cpu-int8", "dml-fp16"];
    private static readonly string[] ExpectedOrder = ["cpu-int4", "cpu-int8", "dml-fp16", "cpu-int8", "dml-fp16", Folder, Folder];
    private static readonly string[] Families = ["Nano", "Small"];

    [Test, Explicit("Consulta somente metadados públicos dos dois commits HTP, sem baixar pesos.")]
    public async Task PublicPinnedSnapdragonMetadataIsDownloadableButRequiresUnsupportedRuntime()
    {
        var repositories = HuggingFaceModelSource.DefaultRepositories.Where(repository => repository.Revision is not null).ToArray();
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), repositories);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var variants = await source.ListAsync(cancellation.Token);
        Assert.That(variants, Has.Count.EqualTo(2));
        foreach (var variant in variants)
        {
            Assert.That(variant.IsInstallable, Is.True);
            Assert.That(variant.RequiresUnsupportedRuntime, Is.True);
            Assert.That(variant.Variant, Is.EqualTo(Folder));
            Assert.That(variant.Files, Is.Not.Empty);
            Assert.That(variant.SizeBytes, Is.GreaterThan(0));
            Assert.That(variant.Files.All(file => file.GitBlobSha1 is { Length: 40 }), Is.True);
            TestContext.WriteLine($"{variant.Repository}@{variant.Revision}: {variant.Files.Count} arquivos, {variant.SizeBytes} bytes; instalação={variant.IsInstallable}");
        }
    }

    [TestCase("Nano", NanoRevision)]
    [TestCase("Small", SmallRevision)]
    public async Task PublishedSnapdragonRevisionIsListedWithHashesAndAnExplicitRuntimeGate(string family, string revision)
    {
        using var handler = new HubHandler();
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), handler: handler);
        var variants = await source.ListAsync(CancellationToken.None);
        var repository = $"esilva/KapiCoder-Mongo-{family}-ONNX";
        var variant = variants.Single(item => item.Repository == repository && item.Variant == Folder);

        Assert.Multiple(() =>
        {
            Assert.That(variants.Select(item => item.Variant), Is.EqualTo(ExpectedOrder));
            Assert.That(variants[0].IsInstallable, Is.True);
            Assert.That(variants[0].Repository, Is.EqualTo("esilva/KapiCoder-Mongo-Nano-ONNX"));
            Assert.That(variant.Revision, Is.EqualTo(revision));
            Assert.That(variant.IsInstallable, Is.True);
            Assert.That(variant.RequiresUnsupportedRuntime, Is.True);
            Assert.That(variants[0].RequiresUnsupportedRuntime, Is.False);
            Assert.That(variant.HintKey, Is.EqualTo("remoteHintSnapdragonUnavailable"));
            Assert.That(variant.Hint, Does.Contain("experimental").And.Contain("runtime").And.Contain("download disponível"));
            Assert.That(variant.License, Is.EqualTo("apache-2.0"));
            Assert.That(variant.Family, Is.EqualTo($"KapiCoder-Mongo-{family}"));
            Assert.That(variant.BaseModelUrl, Is.EqualTo(new Uri($"https://huggingface.co/esilva/KapiCoder-Mongo-{family}")));
            Assert.That(variant.PageUrl, Is.EqualTo(new Uri($"https://huggingface.co/{repository}/tree/{revision}/{Folder}")));
            Assert.That(variant.SizeBytes, Is.EqualTo(9));
            Assert.That(variant.Files, Has.Count.EqualTo(2));
            Assert.That(variant.Files[0].GitBlobSha1, Is.EqualTo(BlobHash));
            Assert.That(variant.Files[1].Sha256, Is.EqualTo(LfsHash));
            Assert.That(source.RepositoryUrls, Has.Count.EqualTo(2));
            Assert.That(handler.Requests, Does.Contain($"api/models/{repository}/revision/{revision}"));
            Assert.That(handler.Requests, Does.Contain($"api/models/{repository}/tree/{revision}/{Folder}?recursive=true"));
            Assert.That(handler.Requests.Any(path => path.Contains("/resolve/", StringComparison.Ordinal)), Is.False);
        });
    }

    [TestCase("Nano")]
    [TestCase("Small")]
    public async Task DownloadUsesEachPublishedCommitAndVerifiesBothHashTypesWithoutClaimingRuntimeCompatibility(string family)
    {
        using var root = new SyntheticDirectory();
        using var handler = new HubHandler();
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), handler: handler);
        var variant = (await source.ListAsync(CancellationToken.None)).Single(item => item.Family == $"KapiCoder-Mongo-{family}" && item.Variant == Folder);
        var models = Path.Combine(root.Path, "models");
        var installed = await source.DownloadAsync(variant, models, null, CancellationToken.None);

        Assert.That(installed, Is.EqualTo(Path.Combine(models, $"KapiCoder-Mongo-{family}-ONNX-{Folder}")));
        Assert.That(File.ReadAllText(Path.Combine(installed, "genai_config.json")), Is.EqualTo("hello\n"));
        Assert.That(File.ReadAllText(Path.Combine(installed, "context_qnn.bin")), Is.EqualTo("abc"));
        Assert.That(Directory.GetDirectories(models), Is.EquivalentTo(new[] { installed }));
        Assert.That(variant.RequiresUnsupportedRuntime, Is.True, "A completed download is not a runtime compatibility test.");
        Assert.That(handler.Requests.Count(path => path.Contains("/resolve/", StringComparison.Ordinal)), Is.EqualTo(2));
        Assert.That(handler.Requests, Does.Contain($"{variant.Repository}/resolve/{variant.Revision}/{Folder}/context_qnn.bin"));
        Assert.ThrowsAsync<IOException>(() => source.DownloadAsync(variant, models, null, CancellationToken.None));
    }

    [TestCase("Nano", "genai_config.json")]
    [TestCase("Nano", "context_qnn.bin")]
    [TestCase("Small", "genai_config.json")]
    [TestCase("Small", "context_qnn.bin")]
    public async Task CorruptGitBlobOrLfsPayloadCannotPublishEitherPinnedPackage(string family, string corruptFile)
    {
        using var root = new SyntheticDirectory();
        using var handler = new HubHandler { CorruptFile = corruptFile };
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), handler: handler);
        var variant = (await source.ListAsync(CancellationToken.None)).Single(item => item.Family == $"KapiCoder-Mongo-{family}" && item.Variant == Folder);
        var models = Path.Combine(root.Path, "models");

        var error = Assert.ThrowsAsync<InvalidDataException>(() => source.DownloadAsync(variant, models, null, CancellationToken.None));
        Assert.That(error!.Message, Does.Contain("hash"));
        Assert.That(Directory.Exists(Path.Combine(models, variant.FolderName)), Is.False);
        Assert.That(Directory.EnumerateFiles(models, "*.part", SearchOption.AllDirectories), Is.Empty);
    }

    [TestCase("Nano")]
    [TestCase("Small")]
    public async Task ForgedRevisionCannotDownloadAPinnedFolder(string family)
    {
        using var root = new SyntheticDirectory();
        using var handler = new HubHandler();
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), handler: handler);
        var variant = (await source.ListAsync(CancellationToken.None)).Single(item => item.Family == $"KapiCoder-Mongo-{family}" && item.Variant == Folder);
        var requests = handler.Requests.Count;
        var models = Path.Combine(root.Path, "models");

        Assert.ThrowsAsync<InvalidDataException>(() => source.DownloadAsync(variant with { Revision = new string('f', 40) }, models, null, CancellationToken.None));
        Assert.That(handler.Requests, Has.Count.EqualTo(requests));
        Assert.That(Directory.Exists(models), Is.False);
    }

    [Test]
    public async Task CommitResponseMismatchDoesNotListOrFollowTheWrongRevision()
    {
        using var handler = new HubHandler { WrongNanoRevision = true };
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), handler: handler);
        var variants = await source.ListAsync(CancellationToken.None);

        Assert.That(variants.Count(item => item.Variant == Folder), Is.EqualTo(1));
        Assert.That(variants.Single(item => item.Variant == Folder).Revision, Is.EqualTo(SmallRevision));
        Assert.That(handler.Requests.Any(path => path.Contains($"/tree/{NanoRevision}/", StringComparison.Ordinal)), Is.False);
        Assert.That(variants[0].Variant, Is.EqualTo("cpu-int4"));
    }

    [Test]
    public async Task MalformedPinnedFileHashesDoNotHideTheUsableMainCatalog()
    {
        using var handler = new HubHandler { MalformedPinnedHash = true };
        using var source = new HuggingFaceModelSource(new RemoteModelStorage(), handler: handler);
        var variants = await source.ListAsync(CancellationToken.None);

        Assert.That(variants, Has.Count.EqualTo(5));
        Assert.That(variants.All(item => item.IsInstallable), Is.True);
        Assert.That(variants[0].Variant, Is.EqualTo("cpu-int4"));
    }

    [TestCase("main", Folder)]
    [TestCase(NanoRevision, "../escape")]
    public void UnsafePinnedSourceIsRejectedBeforeAnyRequest(string revision, string folder)
    {
        var repository = new RemoteModelRepository("org/model", null, []) { Revision = revision, VariantFolder = folder };
        Assert.Throws<ArgumentException>(() =>
        {
            using var source = new HuggingFaceModelSource(new RemoteModelStorage(), [repository]);
        });
    }

    private sealed class HubHandler : HttpMessageHandler
    {
        public bool WrongNanoRevision { get; init; }
        public bool MalformedPinnedHash { get; init; }
        public string? CorruptFile { get; init; }
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.That(request.Headers.Authorization, Is.Null);
            var path = request.RequestUri!.PathAndQuery.TrimStart('/');
            Requests.Add(path);
            foreach (var family in Families)
            {
                var repository = $"esilva/KapiCoder-Mongo-{family}-ONNX";
                var main = family == "Nano" ? new string('1', 40) : new string('2', 40);
                var pinned = family == "Nano" ? NanoRevision : SmallRevision;
                if (path == $"api/models/{repository}") return Json(new { sha = main, cardData = new { license = "apache-2.0" } });
                if (path == $"api/models/{repository}/revision/{pinned}")
                    return Json(new { sha = family == "Nano" && WrongNanoRevision ? main : pinned, cardData = new { license = "apache-2.0" } });
                if (path == $"api/models/{repository}/tree/{main}?recursive=true")
                {
                    var folders = LegacyFolders.Where(folder => family == "Nano" || folder != "cpu-int4").Append(Folder);
                    return Json(folders.SelectMany(folder => Files(folder, false)));
                }
                if (path == $"api/models/{repository}/tree/{pinned}/{Folder}?recursive=true")
                    return Json(Files(Folder, MalformedPinnedHash).Concat(Files("unrelated-folder", false)));
                if (path.StartsWith($"{repository}/resolve/{pinned}/{Folder}/", StringComparison.Ordinal))
                {
                    var config = path.EndsWith("/genai_config.json", StringComparison.Ordinal);
                    var corrupt = CorruptFile is not null && path.EndsWith("/" + CorruptFile, StringComparison.Ordinal);
                    var body = config ? corrupt ? "other\n" : "hello\n" : corrupt ? "xyz" : "abc";
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(body)) });
                }
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        // Bounded synthetic metadata: no real weight payload is downloaded by these tests.
        private static object[] Files(string folder, bool malformed) =>
        [
            new { type = "file", path = folder + "/genai_config.json", size = 6, oid = BlobHash },
            new { type = "file", path = folder + "/context_qnn.bin", size = 3, oid = new string('a', 40), lfs = new { oid = malformed ? "invalid" : LfsHash } }
        ];

        private static Task<HttpResponseMessage> Json(object value) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") });
    }
}
