using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using static EsilvaSoft.KapibaraStudio.IntegrationTests.LocalModelDiskFixture;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
public sealed class LocalModelCatalogIntegrationTests
{
    private static readonly AiAccelerationMode[] CpuOnly = [AiAccelerationMode.Cpu];
    private static readonly string[] DiscoveredFolders = ["Broken-Metadata", "Empty", "Llama-Plain", "SlopCoder-Mongo-0.5B", "SlopCoder-Mongo-1.5B", "SlopCoder-Test"];

    [Test]
    public async Task DiscoveryUsesFolderNamesOptionalMetadataAndIsolatesInvalidFolders()
    {
        using var models = new SyntheticDirectory();
        CreateQwenModel(models.Path, "SlopCoder-Mongo-0.5B");
        CreateQwenModel(models.Path, "SlopCoder-Mongo-1.5B", """
            {"name":"SlopCoder Mongo 1.5B","version":"1.0.0","parameters":"1.5B","domain":["mongodb","json"],"capabilities":["autocomplete","fim","future"],
             "hardware":["cpu"],"recommendedContextTokens":2048,"recommendedCompletionTokens":128,"generation":{"chat":{"maxTokens":512,"temperature":0.3}}}
            """);
        File.Delete(Path.Combine(CreateQwenModel(models.Path, "SlopCoder-Test"), "tokenizer.json"));
        CreateQwenModel(models.Path, "Broken-Metadata", "{\"capabilities\":\"chat\"}");
        File.WriteAllText(Path.Combine(CreateQwenModel(models.Path, "Llama-Plain"), "genai_config.json"), "{\"model\":{\"type\":\"llama\",\"decoder\":{\"filename\":\"model.onnx\"}}}");
        Directory.CreateDirectory(Path.Combine(models.Path, "Empty"));

        var found = (await new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess()).DiscoverAsync()).ToDictionary(validation => Path.GetFileName(validation.Path));

        Assert.That(found.Keys, Is.EqualTo(DiscoveredFolders));
        Assert.That(found["SlopCoder-Mongo-0.5B"].Model!.Name, Is.EqualTo("SlopCoder-Mongo-0.5B"));
        Assert.That(found["SlopCoder-Mongo-0.5B"].Model!.Capabilities.HasFlag(LocalModelCapabilities.Chat), Is.True);
        var described = found["SlopCoder-Mongo-1.5B"].Model!;
        Assert.That(described.Id, Is.EqualTo("SlopCoder-Mongo-1.5B"));
        Assert.That(described.Name, Is.EqualTo("SlopCoder Mongo 1.5B"));
        Assert.That(described.Capabilities, Is.EqualTo(LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Fim));
        Assert.That(described.Metadata!.Hardware, Is.EqualTo(CpuOnly));
        Assert.That(described.Metadata.Chat, Is.EqualTo(new LocalModelGenerationDefaults(512, 0.3)));
        Assert.That(found["SlopCoder-Test"].Validity, Is.EqualTo(LocalModelValidity.MissingFiles));
        Assert.That(found["SlopCoder-Test"].Status.Message, Does.Contain("tokenizer.json"));
        Assert.That(found["Empty"].Validity, Is.EqualTo(LocalModelValidity.MissingFiles));
        Assert.That(found["Broken-Metadata"].Validity, Is.EqualTo(LocalModelValidity.Invalid));
        Assert.That(found["Llama-Plain"].Validity, Is.EqualTo(LocalModelValidity.Unsupported));
    }

    [Test]
    public async Task Qwen3AgentMetadataIsAcceptedWithoutFallingBackToFim()
    {
        using var models = new SyntheticDirectory();
        var path = CreateQwen3AgentModel(models.Path, "Qwen3-Agent");
        var catalog = new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess());

        var valid = await catalog.ValidateAsync(path);
        var tokenizerPath = Path.Combine(path, "tokenizer.json");
        await File.WriteAllTextAsync(tokenizerPath, "{\"added_tokens\":[{\"content\":\"<|im_start|>\",\"id\":151644}]}");
        var invalid = await catalog.ValidateAsync(path);

        Assert.Multiple(() =>
        {
            Assert.That(valid.Validity, Is.EqualTo(LocalModelValidity.Valid));
            Assert.That(valid.Model!.PromptFormat, Is.EqualTo(LocalModelPromptFormats.Qwen3ChatMlTools));
            Assert.That(valid.Model.Capabilities.HasFlag(LocalModelCapabilities.Agent), Is.True);
            Assert.That(valid.Model.Capabilities.HasFlag(LocalModelCapabilities.Tools), Is.True);
            Assert.That(valid.Model.Capabilities.HasFlag(LocalModelCapabilities.Fim), Is.False);
            Assert.That(valid.Model.Metadata!.Agent!.MaxToolCallsPerTurn, Is.EqualTo(4));
            Assert.That(invalid.Validity, Is.EqualTo(LocalModelValidity.Unsupported));
        });
    }

    [Test]
    public async Task ModelExposesContextWindowAndAutocompleteGenerationLimit()
    {
        using var models = new SyntheticDirectory();
        var path = CreateQwenModel(models.Path, "Limited", "{\"generation\":{\"autocomplete\":{\"maxTokens\":128}}}");
        var config = Path.Combine(path, "genai_config.json");
        await File.WriteAllTextAsync(config, "{\"model\":{\"type\":\"qwen2\",\"context_length\":4096,\"decoder\":{\"filename\":\"model.onnx\"}}}");

        var validation = await new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess()).ValidateAsync(path);

        Assert.That(validation.Model!.ContextLength, Is.EqualTo(4096));
        Assert.That(validation.Model.AutocompleteMaximumTokens, Is.EqualTo(128));
        Assert.That((validation.Model.EffectiveContextLength, validation.Model.EffectiveAutocompleteMaximumTokens), Is.EqualTo((4096, 128)));
        Assert.That(validation.Model.BudgetConfidence, Is.EqualTo("declarado pelo modelo"));

        var undeclaredPath = CreateQwenModel(models.Path, "Undeclared");
        await File.WriteAllTextAsync(Path.Combine(undeclaredPath, "genai_config.json"), "{\"model\":{\"type\":\"qwen2\",\"decoder\":{\"filename\":\"model.onnx\"}}}");
        var undeclared = await new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess()).ValidateAsync(undeclaredPath);
        Assert.That((undeclared.Model!.ContextLength, undeclared.Model.AutocompleteMaximumTokens), Is.EqualTo(((int?)null, (int?)null)));
        Assert.That((undeclared.Model.EffectiveContextLength, undeclared.Model.EffectiveAutocompleteMaximumTokens), Is.EqualTo((8192, 256)));
        Assert.That(undeclared.Model.BudgetConfidence, Is.EqualTo("estimativa"));
    }

    [Test]
    public async Task ModelMetadataAcceptsDeclaredBudgetsAboveLegacyLimits()
    {
        using var models = new SyntheticDirectory();
        var path = CreateQwenModel(models.Path, "Large-Budget", "{\"recommendedContextTokens\":32768,\"recommendedCompletionTokens\":1024,\"generation\":{\"autocomplete\":{\"maxTokens\":1024}}}");
        await File.WriteAllTextAsync(Path.Combine(path, "genai_config.json"), "{\"model\":{\"type\":\"qwen2\",\"context_length\":32768,\"decoder\":{\"filename\":\"model.onnx\"}}}");

        var validation = await new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess()).ValidateAsync(path);

        Assert.That(validation.Model!.ContextLength, Is.EqualTo(32768));
        Assert.That(validation.Model.Metadata!.RecommendedContextTokens, Is.EqualTo(32768));
        Assert.That(validation.Model.Metadata.RecommendedCompletionTokens, Is.EqualTo(1024));
        Assert.That(validation.Model.AutocompleteMaximumTokens, Is.EqualTo(1024));
    }

    /// <summary>Lote A31c: compatibilidade de contrato de prompt decidida só pelo metadata, sem carregar pesos.</summary>
    [Test]
    public async Task ContextContractIsOptionalAndOnlyAnUnknownIdentifierRejectsThePackage()
    {
        using var models = new SyntheticDirectory();
        CreateQwenModel(models.Path, "No-Metadata");
        CreateQwenModel(models.Path, "No-Contract", """{"name":"Sem contrato"}""");
        CreateQwenModel(models.Path, "Known-Contract", """{"contextContract":"editor-context-v1","supportsRepositoryContext":true}""");
        CreateQwenModel(models.Path, "Repo-Context-False", """{"contextContract":"editor-context-v1","supportsRepositoryContext":false}""");
        CreateQwenModel(models.Path, "Unknown-Contract", """{"contextContract":"repository-files-v3"}""");

        var found = (await new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess()).DiscoverAsync()).ToDictionary(validation => Path.GetFileName(validation.Path));

        Assert.That(found["No-Metadata"].Validity, Is.EqualTo(LocalModelValidity.Valid));
        Assert.That(found["No-Metadata"].Model!.Metadata, Is.Null);
        Assert.That(found["No-Contract"].Validity, Is.EqualTo(LocalModelValidity.Valid));
        Assert.That(found["No-Contract"].Model!.Metadata!.ContextContract, Is.Null);
        Assert.That(found["Known-Contract"].Validity, Is.EqualTo(LocalModelValidity.Valid));
        Assert.That(found["Known-Contract"].Model!.Metadata!.ContextContract, Is.EqualTo(LocalModelContextContracts.EditorContextV1));
        Assert.That(found["Unknown-Contract"].Validity, Is.EqualTo(LocalModelValidity.Invalid));
        Assert.That(found["Unknown-Contract"].Status.Message, Does.Contain("repository-files-v3").And.Contain("contrato de contexto"));
        Assert.That(found["Unknown-Contract"].Status.Message, Does.Contain(LocalModelContextContracts.EditorContextV1));
        Assert.That(found["Unknown-Contract"].Model, Is.Null);
    }

    /// <summary>Null (undeclared), true and false are three distinct states of <c>supportsRepositoryContext</c>.</summary>
    [Test]
    public async Task RepositoryContextSupportDistinguishesAbsentFromExplicitFalse()
    {
        using var models = new SyntheticDirectory();
        CreateQwenModel(models.Path, "Absent", """{"name":"Sem declaração"}""");
        CreateQwenModel(models.Path, "True", """{"supportsRepositoryContext":true}""");
        CreateQwenModel(models.Path, "False", """{"supportsRepositoryContext":false}""");

        var found = (await new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess()).DiscoverAsync()).ToDictionary(validation => Path.GetFileName(validation.Path));

        Assert.That(found["Absent"].Model!.Metadata!.SupportsRepositoryContext, Is.Null);
        Assert.That(found["True"].Model!.Metadata!.SupportsRepositoryContext, Is.True);
        Assert.That(found["False"].Model!.Metadata!.SupportsRepositoryContext, Is.False);
        Assert.That(found.Values.Select(validation => validation.Validity), Is.All.EqualTo(LocalModelValidity.Valid));
    }

    /// <summary>Same discipline as the rest of the reader: a wrong JSON type is malformed metadata, not an unknown contract.</summary>
    [TestCase("""{"contextContract":7}""")]
    [TestCase("""{"contextContract":["editor-context-v1"]}""")]
    [TestCase("""{"supportsRepositoryContext":"true"}""")]
    [TestCase("""{"supportsRepositoryContext":1}""")]
    public async Task WrongTypesInTheNewFieldsAreRejectedAsMalformedMetadata(string metadata)
    {
        using var models = new SyntheticDirectory();
        CreateQwenModel(models.Path, "Typed-Wrong", metadata);

        var validation = await new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess()).ValidateAsync(Path.Combine(models.Path, "Typed-Wrong"));

        Assert.That(validation.Validity, Is.EqualTo(LocalModelValidity.Invalid));
        Assert.That(validation.Status.Message, Does.Contain(LocalModelMetadata.FileName));
        Assert.That(validation.Model, Is.Null);
    }

}
