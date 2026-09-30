using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using static EsilvaSoft.KapibaraStudio.IntegrationTests.LocalModelDiskFixture;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
public sealed class LocalAiModelServiceCatalogIntegrationTests
{
    [Test]
    public async Task AnUnknownContextContractRefusesGenerationAndLoadAndNotOnlyTheListing()
    {
        using var models = new SyntheticDirectory();
        CreateQwenModel(models.Path, "Unknown-Contract", """{"contextContract":"repository-files-v3"}""");
        var runtime = new CompletionRuntimeFake();
        await using var service = new LocalAiModelService(new LocalModelCatalog(models.Path, fileAccess: new LocalModelFileAccess()), () => runtime);
        var settings = new AutocompleteSettings { ModelDirectory = models.Path, SelectedModel = "Unknown-Contract" }.Validate();

        var generation = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.GenerateAsync(
            LocalModelRole.Autocomplete, settings, Request, AiRequestPriority.Interactive))!;
        var load = Assert.ThrowsAsync<LocalModelUnavailableException>(() => service.LoadModelAsync(LocalModelRole.Autocomplete, settings))!;

        Assert.That(generation.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.ModelInvalid));
        Assert.That(generation.Message, Does.Contain("repository-files-v3").And.Contain("contrato de contexto"));
        Assert.That(load.UnavailableReason, Is.EqualTo(LocalModelUnavailableReason.ModelInvalid),
            "A janela de recusa aberta pela primeira reprovação não pode esconder a causa durável atrás de um cooldown.");
        Assert.That(load.RetryAfter, Is.Not.Null, "A janela existe e é exibível, mas o motivo continua sendo o pacote.");
        Assert.That(runtime.Initializations, Is.Zero, "Nenhum peso é carregado para descobrir que o contrato não serve.");
    }

    private static ModelGenerationRequest Request(LocalModelDefinition model) => new("db.", "", 512, 8);

    private sealed class CompletionRuntimeFake : ILocalModelRuntime
    {
        public int Initializations { get; private set; }
        public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default)
        {
            Initializations++;
            return Task.CompletedTask;
        }

        public Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ModelGenerationResult("find({})", 3, TimeSpan.FromMilliseconds(3), "cpu"));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
