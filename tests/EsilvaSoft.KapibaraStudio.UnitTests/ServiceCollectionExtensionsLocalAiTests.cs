using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Composição real da IA local (as mesmas duas chamadas que <c>App.axaml.cs</c> faz): a camada de geração e o
/// provider explícito saem do container, e o serviço de modelo continua sendo <strong>um</strong> — um segundo
/// significaria um segundo modelo na memória e duas filas de prioridade concorrendo pelo mesmo acelerador.
/// </summary>
[TestFixture]
public sealed class ServiceCollectionExtensionsLocalAiTests
{
    private string _root = "";

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "kapibarastudio-di-ai-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* Um arquivo ainda preso não deve falhar o teste que já terminou. */ }
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(Path.Combine(_root, "workspace.db"));
        services.AddKapibaraStudioLocalAiInfrastructure();
        return services.BuildServiceProvider();
    }

    [Test]
    public async Task TheExplicitProviderAndThePipelineShareTheSingleModelService()
    {
        await using var provider = BuildProvider();

        var models = provider.GetRequiredService<ILocalAiModelService>();
        var pipeline = provider.GetRequiredService<AiGenerationPipeline>();
        var completion = provider.GetRequiredService<AiCompletionProvider>();
        var byInterface = provider.GetRequiredService<IAiCompletionProvider>();

        Assert.Multiple(() =>
        {
            Assert.That(pipeline.Models, Is.SameAs(models), "O pipeline recebe o singleton, não constrói um serviço.");
            Assert.That(completion.Models, Is.SameAs(models));
            Assert.That(byInterface, Is.SameAs(completion), "O contrato e a classe resolvem a mesma instância.");
            Assert.That(provider.GetRequiredService<ILocalAiModelService>(), Is.SameAs(models));
            Assert.That(provider.GetRequiredService<AiGenerationPipeline>(), Is.SameAs(pipeline));
        });
    }

    /// <summary>Nada da IA local é carregado por resolver o container: pesos só entram sob pedido explícito.</summary>
    [Test]
    public async Task ResolvingTheAiCompositionLoadsNoModel()
    {
        await using var provider = BuildProvider();

        var completion = provider.GetRequiredService<AiCompletionProvider>();

        Assert.Multiple(() =>
        {
            Assert.That(completion.Models.LoadedModel, Is.Null);
            Assert.That(completion.Models.Status.State, Is.EqualTo(LocalModelState.NotLoaded));
        });
    }
}
