using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Net;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

public sealed class HuggingFaceModelSourcePortTests
{
    private const string Repository = "owner/model-ONNX";
    private const string Revision = "9e2775acb8fd226fb085bcc0668c26fa8e1be298";
    private const string BlobSha1 = "ce013625030ba8dba906f756967f9e9ca394464a";
    private const string AbcSha256 = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
    private static readonly RemoteModelRepository RepositoryConfig = new(Repository, null, [new("int4", "compacto")]);

    [Test]
    public async Task DownloadUsesOnlyStoragePortAndInstallsAfterVerifyingFiles()
    {
        var storage = new MemoryModelStorage();
        using var source = CreateSource(storage);
        var variant = (await source.ListAsync(CancellationToken.None)).Single();

        var installed = await source.DownloadAsync(variant, "/models", null, CancellationToken.None);

        Assert.That(installed, Is.EqualTo("/models/model-ONNX-int4"));
        Assert.That(storage.ReadText("/models/model-ONNX-int4/genai_config.json"), Is.EqualTo("hello\n"));
        Assert.That(storage.ReadText("/models/model-ONNX-int4/model.onnx.data"), Is.EqualTo("abc"));
        Assert.That(storage.DirectoryExists("/models/.model-ONNX-int4.download"), Is.False);
        Assert.That(storage.DirectoryExists(installed), Is.True);
        Assert.That(storage.FileOperationCount, Is.GreaterThan(0));
    }

    [Test]
    public async Task HashFailureCleansPartialFileAndRetryReusesVerifiedStagingFile()
    {
        var storage = new MemoryModelStorage();
        var handler = new HubHandler("abd");
        using var source = new HuggingFaceModelSource(storage, [RepositoryConfig], handler, new Uri("https://hf.test/"));
        var variant = (await source.ListAsync(CancellationToken.None)).Single();

        Assert.ThrowsAsync<InvalidDataException>(() => source.DownloadAsync(variant, "/models", null, CancellationToken.None));
        Assert.That(storage.Files.Keys, Does.Not.Contain("/models/.model-ONNX-int4.download/model.onnx.data.part"));
        Assert.That(storage.DirectoryExists("/models/model-ONNX-int4"), Is.False);

        handler.Data = "abc";
        await source.DownloadAsync(variant, "/models", null, CancellationToken.None);
        Assert.That(handler.ConfigDownloads, Is.EqualTo(1));
    }

    [Test]
    public async Task DiskQuotaFailureIsReportedBeforeAnyDownloadRequest()
    {
        var storage = new MemoryModelStorage { AvailableBytes = 0 };
        var handler = new HubHandler("abc");
        using var source = new HuggingFaceModelSource(storage, [RepositoryConfig], handler, new Uri("https://hf.test/"));
        var variant = (await source.ListAsync(CancellationToken.None)).Single();

        Assert.ThrowsAsync<IOException>(() => source.DownloadAsync(variant, "/models", null, CancellationToken.None));
        Assert.That(handler.FileDownloads, Is.Zero);
    }

    private static HuggingFaceModelSource CreateSource(MemoryModelStorage storage) =>
        new(storage, [RepositoryConfig], new HubHandler("abc"), new Uri("https://hf.test/"));

    private sealed class HubHandler(string data) : HttpMessageHandler
    {
        public string Data { get; set; } = data;
        public int ConfigDownloads { get; private set; }
        public int FileDownloads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var url = request.RequestUri!.AbsoluteUri;
            if (url == $"https://hf.test/api/models/{Repository}") return Task.FromResult(Json(new { sha = Revision }));
            if (url == $"https://hf.test/api/models/{Repository}/tree/{Revision}?recursive=true")
                return Task.FromResult(Json(new object[]
                {
                    new { type = "file", oid = BlobSha1, size = 6, path = "int4/genai_config.json" },
                    new { type = "file", oid = new string('2', 40), size = 3, path = "int4/model.onnx.data", lfs = new { oid = AbcSha256 } }
                }));

            if (url.EndsWith("/int4/genai_config.json", StringComparison.Ordinal))
            {
                ConfigDownloads++;
                return Task.FromResult(Text("hello\n"));
            }
            if (url.EndsWith("/int4/model.onnx.data", StringComparison.Ordinal))
            {
                FileDownloads++;
                return Task.FromResult(Text(Data));
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage Json(object value) =>
            new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
        private static HttpResponseMessage Text(string value) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.ASCII.GetBytes(value)) };
    }

    private sealed class MemoryModelStorage : IRemoteModelStorage
    {
        private readonly HashSet<string> _directories = new(StringComparer.Ordinal) { "/", "/models" };
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
        public long? AvailableBytes { get; init; } = 1_000_000;
        public int FileOperationCount { get; private set; }

        public string GetFullPath(string path) => Normalize(path);
        public string Combine(params string[] paths) => Normalize(string.Join('/', paths.Where(path => path.Length > 0)));
        public string? GetDirectoryName(string path)
        {
            var normalized = Normalize(path);
            var index = normalized.LastIndexOf('/');
            return index <= 0 ? "/" : normalized[..index];
        }
        public bool DirectoryExists(string path) => _directories.Contains(Normalize(path));
        public void CreateDirectory(string path)
        {
            var normalized = Normalize(path);
            var parent = GetDirectoryName(normalized);
            if (parent is not null && !_directories.Contains(parent)) CreateDirectory(parent);
            _directories.Add(normalized);
        }
        public void MoveDirectory(string source, string destination)
        {
            source = Normalize(source);
            destination = Normalize(destination);
            if (_directories.Contains(destination)) throw new IOException("Destination exists.");
            foreach (var item in _directories.Where(item => item == source || item.StartsWith(source + "/", StringComparison.Ordinal)).ToArray())
            {
                _directories.Remove(item);
                _directories.Add(destination + item[source.Length..]);
            }
            foreach (var item in Files.Keys.Where(item => item.StartsWith(source + "/", StringComparison.Ordinal)).ToArray())
            {
                Files[destination + item[source.Length..]] = Files[item];
                Files.Remove(item);
            }
            FileOperationCount++;
        }
        public bool FileExists(string path) => Files.ContainsKey(Normalize(path));
        public long GetFileLength(string path) => Files[Normalize(path)].LongLength;
        public IReadOnlyList<string> EnumerateFiles(string path, string pattern, bool recursive) =>
            Files.Keys.Where(file => file.StartsWith(Normalize(path) + "/", StringComparison.Ordinal)).ToArray();
        public Stream OpenRead(string path)
        {
            FileOperationCount++;
            return new MemoryStream(Files[Normalize(path)], writable: false);
        }
        public Stream OpenWrite(string path)
        {
            FileOperationCount++;
            return new SavingStream(bytes => Files[Normalize(path)] = bytes);
        }
        public void DeleteFile(string path) { FileOperationCount++; Files.Remove(Normalize(path)); }
        public void MoveFile(string source, string destination)
        {
            source = Normalize(source);
            destination = Normalize(destination);
            Files[destination] = Files[source];
            Files.Remove(source);
            FileOperationCount++;
        }
        public long? GetAvailableFreeSpace(string path) => AvailableBytes;
        public string ReadText(string path) => Encoding.ASCII.GetString(Files[Normalize(path)]);

        private static string Normalize(string path)
        {
            var parts = new List<string>();
            foreach (var part in path.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(part);
            }
            return "/" + string.Join('/', parts);
        }

        private sealed class SavingStream(Action<byte[]> save) : MemoryStream
        {
            protected override void Dispose(bool disposing)
            {
                if (disposing) save(ToArray());
                base.Dispose(disposing);
            }
        }
    }
}
