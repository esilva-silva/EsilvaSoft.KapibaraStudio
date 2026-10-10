using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class StrictKapiModelValidationTests
{
    private static readonly string[] BasicCapabilities = ["chat", "fim"];
    private static readonly string[] AgentCapabilities = ["chat", "agent", "tools", "reasoning"];

    private sealed class Package : IDisposable
    {
        public Package()
        {
            Root = Path.Combine(Path.GetTempPath(), "kapilab-strict-model-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }
        public KapiLabModelFileAccess Files { get; } = new();
        public void Write(string name, string content)
        {
            var path = Path.Combine(Root, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string Metadata(string minimum = "0.15.2", string schema = StrictKapiModelValidation.MetadataSchema) =>
        JsonSerializer.Serialize(new
        {
            schema,
            contextContract = "editor-context-v1",
            capabilities = BasicCapabilities,
            runtime = new { minimumGenAi = minimum },
        });

    [Test]
    public async Task LocalSchemaValidityIsIndependentOfIdeSupportAndMinimumVersionIsSemantic()
    {
        using var package = new Package();
        package.Write("genai_config.json", "{\"model\":{\"type\":\"qwen2\",\"context_length\":4096,\"decoder\":{\"filename\":\"model.onnx\"}}}");
        package.Write("model.onnx", "synthetic-weight-fixture");
        package.Write("tokenizer_config.json", "{}");
        package.Write("tokenizer.json", JsonSerializer.Serialize(new
        {
            added_tokens = QwenFimPromptBuilder.SpecialTokens.Select((token, index) => new { content = token, id = 1000 + index }),
        }));
        package.Write("kapibarastudio-model.json", Metadata(minimum: "0.17.1"));

        var schema = StrictKapiModelValidation.ValidateMetadata(package.Root, package.Files);
        var ide = await new LocalModelCatalog(package.Root, fileAccess: package.Files).ValidateAsync(package.Root);

        Assert.Multiple(() =>
        {
            Assert.That(schema.Valid, Is.True, string.Join(",", schema.Issues));
            Assert.That(ide.Status.State, Is.EqualTo(LocalModelState.Unsupported));
        });
        package.Write("kapibarastudio-model.json", Metadata(minimum: "0.15.2"));
        Assert.That(StrictKapiModelValidation.ValidateMetadata(package.Root, package.Files).Valid, Is.True);
        Assert.That((await new LocalModelCatalog(package.Root, fileAccess: package.Files).ValidateAsync(package.Root)).Status.State,
            Is.EqualTo(LocalModelState.Available));
    }

    [Test]
    public void UnknownSchemaNpuBlockAndInvalidVersionFailClosed()
    {
        using var package = new Package();
        package.Write("kapibarastudio-model.json", Metadata(schema: "external-model-v2"));
        Assert.That(StrictKapiModelValidation.ValidateMetadata(package.Root, package.Files).Issues,
            Does.Contain("metadata.schema_unsupported"));

        package.Write("kapibarastudio-model.json", "{\"schema\":\"kapilab-kapi-model-v1\",\"contextContract\":\"editor-context-v1\",\"capabilities\":[],\"runtime\":{\"minimumGenAi\":\"0.15.2\"},\"npu\":{}}");
        Assert.That(StrictKapiModelValidation.ValidateMetadata(package.Root, package.Files).Issues,
            Does.Contain("metadata.npu_schema_unavailable"));

        package.Write("kapibarastudio-model.json", Metadata(minimum: "0.15"));
        Assert.That(StrictKapiModelValidation.ValidateMetadata(package.Root, package.Files).Issues,
            Does.Contain("metadata.runtime.minimum_genai_invalid"));
    }

    [Test]
    public void ReasoningBudgetAndTemplateHashAreCheckedWithoutLoadingWeights()
    {
        using var package = new Package();
        const string template = "{{ messages }}";
        package.Write("chat_template.jinja", template);
        string MetadataWithBudget(int preferred, string hash) => JsonSerializer.Serialize(new
        {
            schema = StrictKapiModelValidation.MetadataSchema,
            contextContract = "editor-context-v1",
            capabilities = AgentCapabilities,
            runtime = new { minimumGenAi = "0.15.2" },
            agent = new
            {
                promptFormat = LocalModelPromptFormats.Qwen3ChatMlTools, chatTemplateFile = "chat_template.jinja",
                toolCallFormat = "qwen-hermes-json", toolSchemaVersion = "kapibara-tools-2026-10",
                maxToolCallsPerTurn = 12, maxToolResultBytes = 6144,
            },
            reasoning = new
            {
                supported = true, format = "qwen3-think", toggle = "hard", history = "strip-previous-turns",
                onBudgetExceeded = "force-close", startTokenId = 151667, endTokenId = 151668,
                defaultEnabled = true, budgetTokens = new { minimum = 1, maximum = 8192, @default = preferred },
                turnBudgetTokens = 8192, disabledGenerationSuffix = "<think></think>",
                forceCloseText = "</think>", templateSha256 = hash,
            },
            generation = new
            {
                agent = new
                {
                    maxAnswerTokensPerStep = 256,
                    sampling = new
                    {
                        thinking = new { temperature = 0.6, topP = 0.95, topK = 20 },
                        nonThinking = new { temperature = 0.7, topP = 0.8, topK = 20 },
                    },
                },
            },
        });

        package.Write("kapibarastudio-model.json", MetadataWithBudget(512, Hash(template)));
        Assert.That(StrictKapiModelValidation.ValidateMetadata(package.Root, package.Files).Valid, Is.True);

        package.Write("chat_template.jinja", "changed");
        Assert.That(StrictKapiModelValidation.ValidateMetadata(package.Root, package.Files).Issues,
            Does.Contain("metadata.reasoning.template_hash_mismatch"));
        package.Write("kapibarastudio-model.json", MetadataWithBudget(8193, Hash("changed")));
        Assert.That(StrictKapiModelValidation.ValidateMetadata(package.Root, package.Files).Issues,
            Does.Contain("metadata.default_invalid"));
    }

    [Test]
    public void ExplicitManifestHashesOnlyListedContainedFilesAndRejectsMismatch()
    {
        using var package = new Package();
        package.Write("genai_config.json", "config");
        package.Write("nested/model.onnx", "weights");
        package.Write("unlisted.txt", "not claimed by the manifest");
        string Manifest(string hash) => JsonSerializer.Serialize(new
        {
            schema = StrictKapiModelValidation.ManifestSchema,
            files = new[]
            {
                new { path = "genai_config.json", sha256 = Hash("config") },
                new { path = "nested/model.onnx", sha256 = hash },
            },
        });
        package.Write("kapicoder_manifest.json", Manifest(Hash("weights")));
        var valid = StrictKapiModelValidation.VerifyHashes(package.Root, package.Files);
        Assert.Multiple(() =>
        {
            Assert.That(valid.Valid, Is.True);
            Assert.That(valid.VerifiedFiles, Is.EqualTo(2));
        });

        package.Write("nested/model.onnx", "changed");
        Assert.That(StrictKapiModelValidation.VerifyHashes(package.Root, package.Files).Issues,
            Does.Contain("manifest.hash_mismatch"));
        package.Write("kapicoder_manifest.json", JsonSerializer.Serialize(new
        {
            schema = StrictKapiModelValidation.ManifestSchema,
            files = new[] { new { path = "../outside.txt", sha256 = Hash("outside") } },
        }));
        Assert.That(StrictKapiModelValidation.VerifyHashes(package.Root, package.Files).Issues,
            Does.Contain("manifest.entry_invalid"));
    }

    [Test]
    public void MissingOrUnversionedManifestCannotClaimIntegrity()
    {
        using var package = new Package();
        Assert.That(StrictKapiModelValidation.VerifyHashes(package.Root, package.Files).Valid, Is.False);
        package.Write("kapicoder_manifest.json", JsonSerializer.Serialize(new
        {
            schema = "unknown-manifest-v2",
            files = new[] { new { path = "model.onnx", sha256 = Hash("weights") } },
        }));
        Assert.That(StrictKapiModelValidation.VerifyHashes(package.Root, package.Files).Issues,
            Does.Contain("manifest.schema_unsupported"));
    }

    [Test]
    public void DuplicatePropertiesAndManifestPathAliasesAreRejected()
    {
        using var package = new Package();
        package.Write("kapibarastudio-model.json",
            "{\"schema\":\"kapilab-kapi-model-v1\",\"schema\":\"kapilab-kapi-model-v1\",\"contextContract\":\"editor-context-v1\",\"capabilities\":[],\"runtime\":{\"minimumGenAi\":\"0.15.2\"}}");
        Assert.That(StrictKapiModelValidation.ValidateMetadata(package.Root, package.Files).Issues,
            Does.Contain("metadata.object_or_duplicate_property"));

        package.Write("asset.bin", "asset");
        var entries = new[]
        {
            new { path = "asset.bin", sha256 = Hash("asset") },
            new { path = "asset.bin", sha256 = Hash("asset") },
        };
        package.Write("kapicoder_manifest.json", JsonSerializer.Serialize(new
        {
            schema = StrictKapiModelValidation.ManifestSchema, files = entries,
        }));
        Assert.That(StrictKapiModelValidation.VerifyHashes(package.Root, package.Files).Issues,
            Does.Contain("manifest.path_duplicate"));

        package.Write("kapicoder_manifest.json", JsonSerializer.Serialize(new
        {
            schema = StrictKapiModelValidation.ManifestSchema,
            files = new[] { new { path = "kapicoder_manifest.json", sha256 = Hash("placeholder") } },
        }));
        Assert.That(StrictKapiModelValidation.VerifyHashes(package.Root, package.Files).Issues,
            Does.Contain("manifest.entry_invalid"));
    }
}
