using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Testing;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class LocalModelInspectionPortTests
{
    private static readonly int[] ExpectedTokens = [2, 3, 0];
    [Test]
    public async Task MetadataIsReadFromInjectedStreamAndRetainsDeclaredCapabilities()
    {
        var files = QwenFiles();
        files.Add(LocalModelMetadata.FileName, """
            {"name":"Modelo em memória","capabilities":["chat","future"],"hardware":["cpu"],
             "generation":{"autocomplete":{"maxTokens":128,"temperature":0.3}}}
            """);

        var validation = await new LocalModelCatalog(files.Root, fileAccess: files).ValidateAsync(files.Root);

        Assert.That(validation.Validity, Is.EqualTo(LocalModelValidity.Valid));
        Assert.That(validation.Model!.Name, Is.EqualTo("Modelo em memória"));
        Assert.That(validation.Model.Capabilities, Is.EqualTo(LocalModelCapabilities.Chat));
        Assert.That(validation.Model.Metadata!.Autocomplete, Is.EqualTo(new LocalModelGenerationDefaults(128, 0.3)));
        Assert.That(files.Opened, Does.Contain(Path.Combine(files.Root, LocalModelMetadata.FileName)));
        Assert.That(files.Streams.All(stream => !stream.CanRead), Is.True, "All inspection streams must be disposed.");
    }

    [TestCase("{\"capabilities\":\"chat\"}")]
    [TestCase("{\"generation\":{\"autocomplete\":{\"maxTokens\":0}}}")]
    [TestCase("{\"contextContract\":\"unknown-contract\"}")]
    public async Task InvalidMetadataRejectsModelBeforeTokenizerValidation(string metadata)
    {
        var files = QwenFiles();
        files.Add(LocalModelMetadata.FileName, metadata);

        var validation = await new LocalModelCatalog(files.Root, fileAccess: files).ValidateAsync(files.Root);

        Assert.That(validation.Validity, Is.EqualTo(LocalModelValidity.Invalid));
        Assert.That(files.Opened, Does.Not.Contain(Path.Combine(files.Root, "tokenizer.json")));
    }

    [Test]
    public async Task OversizedMetadataIsRejectedWithoutOpeningItsStream()
    {
        var files = QwenFiles();
        files.Add(LocalModelMetadata.FileName, "{}", 1024 * 1024 + 1);

        var validation = await new LocalModelCatalog(files.Root, fileAccess: files).ValidateAsync(files.Root);

        Assert.That(validation.Validity, Is.EqualTo(LocalModelValidity.Invalid));
        Assert.That(files.Opened, Does.Not.Contain(Path.Combine(files.Root, LocalModelMetadata.FileName)));
    }

    [Test]
    public void DeepSeekManifestSelectionAndWeightsUseOnlyInjectedAccess()
    {
        var files = new MemoryModelFiles();
        var adapter = new DeepSeekCoderModelAdapter(files);
        Assert.That(adapter.CanHandle("llama", files.Root), Is.False);
        var tokens = DeepSeekFimPromptBuilder.Tokens;
        files.Add(DeepSeekCoderModelAdapter.ManifestFileName, JsonSerializer.Serialize(new
        {
            prompt = new
            {
                format = LocalModelPromptFormats.DeepSeekCoderFim,
                tokens = tokens.ToDictionary(token => token.Key, token => token.Value.Text),
                token_ids = tokens.ToDictionary(token => token.Key, token => token.Value.Id)
            },
            generation = new { stop_token_ids = tokens.Where(token => token.Key != "bos").Select(token => token.Value.Id) }
        }));
        using var tokenizer = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            added_tokens = tokens.Select(token => new { content = token.Value.Text, id = token.Value.Id })
        }));
        var decoderPath = Path.Combine(files.Root, "model.onnx");
        var folder = new ModelFolder(files.Root, "llama", decoderPath, tokenizer.RootElement);

        Assert.That(adapter.CanHandle("llama", files.Root), Is.True);
        Assert.That(adapter.CanHandle("qwen2", files.Root), Is.False);
        Assert.That(adapter.Validate(folder)!.State, Is.EqualTo(LocalModelState.MissingFiles));
        files.Add("model.onnx.data", "");
        Assert.That(adapter.Validate(folder)!.State, Is.EqualTo(LocalModelState.MissingFiles));
        files.Add("model.onnx.data", "weights");
        Assert.That(adapter.Validate(folder), Is.Null);
        Assert.That(files.Streams.All(stream => !stream.CanRead), Is.True);
    }

    [Test]
    public void TokenizerUsesInjectedStreamForMergesAndAddedMarkers()
    {
        var files = new MemoryModelFiles();
        files.Add("tokenizer.json", """
            {"normalizer":null,"pre_tokenizer":{"pretokenizers":[{"type":"ByteLevel","add_prefix_space":false,"use_regex":false}]},
             "added_tokens":[{"content":"<|EOT|>","id":3}],
             "model":{"type":"BPE","byte_fallback":false,"unk_token":null,"vocab":{"a":0,"b":1,"ab":2},"merges":["a b"]}}
            """);

        var tokenizer = new DeepSeekModelTokenizer(Path.Combine(files.Root, "tokenizer.json"), files);
        var encoded = tokenizer.Encode("ab<|EOT|>a");
        Assert.That(encoded, Is.EqualTo(ExpectedTokens));
        Assert.That(tokenizer.Decode(encoded), Is.EqualTo("ab<|EOT|>a"));
        using var decoder = tokenizer.CreateIncrementalDecoder();
        Assert.That(string.Concat(encoded.Select(decoder.Append)) + decoder.Flush(), Is.EqualTo("ab<|EOT|>a"));
        Assert.That(files.Streams.Single().CanRead, Is.False);
    }

    [Test]
    public async Task ReadFailureBecomesInvalidModelAndDoesNotInspectTokenizer()
    {
        var files = QwenFiles();
        files.Add(LocalModelMetadata.FileName, "{}");
        files.FailRead = Path.Combine(files.Root, LocalModelMetadata.FileName);

        var validation = await new LocalModelCatalog(files.Root, fileAccess: files).ValidateAsync(files.Root);

        Assert.That(validation.Validity, Is.EqualTo(LocalModelValidity.Invalid));
        Assert.That(files.Opened, Does.Not.Contain(Path.Combine(files.Root, "tokenizer.json")));
    }

    private static MemoryModelFiles QwenFiles()
    {
        var files = new MemoryModelFiles();
        files.Add("genai_config.json", """{"model":{"type":"qwen2","decoder":{"filename":"model.onnx"}}}""");
        files.Add("model.onnx", "weights");
        files.Add("tokenizer.json", JsonSerializer.Serialize(new
        {
            added_tokens = QwenFimPromptBuilder.SpecialTokens.Select(token => new { content = token })
        }));
        files.Add("tokenizer_config.json", "{}");
        return files;
    }

    private sealed class MemoryModelFiles : ILocalModelFileAccess
    {
        private readonly Dictionary<string, (byte[] Content, long Length)> _files = new(StringComparer.Ordinal);
        public string Root { get; } = SyntheticPaths.Combine("memory-models", "model");
        public List<string> Opened { get; } = [];
        public List<MemoryStream> Streams { get; } = [];
        public string? FailRead { get; set; }
        public void Add(string name, string content, long? length = null)
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            _files[Path.Combine(Root, name)] = (bytes, length ?? bytes.Length);
        }
        public bool DirectoryExists(string path) => path == Root;
        public IReadOnlyList<string> EnumerateDirectories(string path) => throw new InvalidOperationException();
        public string NormalizeDirectoryPath(string path) => path;
        public string GetDirectoryName(string path) => Path.GetFileName(path);
        public bool FileExists(string path) => _files.ContainsKey(path);
        public long GetFileLength(string path) => _files[path].Length;
        public IReadOnlyList<(string Path, long Length)> EnumerateTopLevelFiles(string path) =>
            _files.Select(file => (file.Key, file.Value.Length)).ToArray();
        public Stream OpenRead(string path)
        {
            Opened.Add(path);
            if (path == FailRead) throw new IOException("Injected read failure.");
            var stream = new MemoryStream(_files[path].Content, writable: false);
            Streams.Add(stream);
            return stream;
        }
    }
}
