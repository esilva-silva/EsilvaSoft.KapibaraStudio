using EsilvaSoft.KapibaraStudio.SystemAdapters;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class LocalAgentProviderModelIntegrationTests
{
    [Test, Explicit("Requer pesos ONNX reais em SLOP_QWEN_MODEL; sem eles permanece ignorado."), Category("LocalModelIntegration")]
    public async Task RealModelStreamsATurnOffline()
    {
        var path = Environment.GetEnvironmentVariable("SLOP_QWEN_MODEL");
        if (string.IsNullOrEmpty(path)) Assert.Ignore("SLOP_QWEN_MODEL não definido: sem modelo real.");
        var models = new LocalAiModelService(new LocalModelCatalog(fileAccess: new LocalModelFileAccess(), workspacePaths: new LocalWorkspacePathResolver()), () => new OnnxLocalModelRuntime(fileAccess: new LocalModelFileAccess()));
        await using var _ = models;
        var autocomplete = new AutocompleteService(new AiAutocompleteProvider(models));
        await autocomplete.ConfigureAsync(new AutocompleteSettings { ModelPath = path! });
        await using var session = await new LocalAgentProvider(models, autocomplete)
            .CreateSessionAsync(new(LocalAgentProvider.Id), CancellationToken.None);

        var events = await CollectAsync(session, Turn("db.users.find("));

        Assert.That(events.Last().Kind, Is.EqualTo(AgentEventKind.MessageCompleted));
    }

    private static AgentTurnRequest Turn(string message) => new(AgentTurnId.New(), message, "tab-1", 1);

    private static async Task<List<AgentProviderEvent>> CollectAsync(IAgentSession session, AgentTurnRequest request)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);
        return events;
    }
}
