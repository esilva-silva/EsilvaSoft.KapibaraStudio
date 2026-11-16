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
        var autocomplete = new AutocompleteService(models);
        await autocomplete.ConfigureAsync(new AutocompleteSettings { ModelPath = path! });
        await using var session = await new LocalAgentProvider(models, autocomplete)
            .CreateSessionAsync(new(LocalAgentProvider.Id), CancellationToken.None);

        var events = await CollectAsync(session, Turn("db.users.find("));

        Assert.That(events.Last().Kind, Is.EqualTo(AgentEventKind.MessageCompleted));
    }

    private const string AgentModelVariable = "KAPI_AGENT_MODEL";
    private const string ListDatabases = "list_databases";
    private static readonly TimeSpan AgentTurnLimit = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Pacote de agente real apontado por <c>KAPI_AGENT_MODEL</c>; <c>KAPI_AGENT_DEVICE</c> (cpu, gpu ou auto) escolhe o
    /// hardware. O registry só anuncia <c>list_databases</c>: o teste devolve um resultado sintético, sem MongoDB.
    /// </summary>
    private sealed class RealAgent : IAsyncDisposable
    {
        private readonly LocalAiModelService _models;

        private RealAgent(LocalAiModelService models, LocalAgentProvider provider)
        {
            _models = models;
            Provider = provider;
        }

        public LocalAgentProvider Provider { get; }

        public static async Task<RealAgent> StartAsync()
        {
            var path = Environment.GetEnvironmentVariable(AgentModelVariable);
            if (string.IsNullOrEmpty(path)) Assert.Ignore(AgentModelVariable + " não definido: sem pacote de agente real.");
            var acceleration = (Environment.GetEnvironmentVariable("KAPI_AGENT_DEVICE") ?? "auto").ToLowerInvariant() switch
            {
                "cpu" => AiAccelerationMode.Cpu, "gpu" => AiAccelerationMode.Gpu, _ => AiAccelerationMode.Auto,
            };
            var catalog = new LocalModelCatalog(fileAccess: new LocalModelFileAccess(), workspacePaths: new LocalWorkspacePathResolver());
            var validation = await catalog.ValidateAsync(path!);
            Assert.That(validation.Model, Is.Not.Null, "O pacote apontado não é válido: " + validation.Status.Message);
            var models = new LocalAiModelService(catalog, () => new OnnxLocalModelRuntime(fileAccess: new LocalModelFileAccess()));
            var autocomplete = new AutocompleteService(models);
            await autocomplete.ConfigureAsync(new AutocompleteSettings
            {
                ModelPath = path!, Acceleration = acceleration, AiTimeoutMilliseconds = 60_000,
                ContextTokens = validation.Model!.Metadata?.RecommendedContextTokens ?? 8192,
            });
            var provider = new LocalAgentProvider(models, autocomplete, new EsilvaSoft.KapibaraStudio.Testing.AnnouncedToolsRegistry(ListDatabases));
            var availability = await provider.GetAvailabilityAsync();
            Assert.That(availability.Capabilities.ToolCalling, Is.True, "O pacote não é servido como agente: " + availability.Reason);
            return new RealAgent(models, provider);
        }

        public ValueTask DisposeAsync() => _models.DisposeAsync();
    }

    private static AgentTurnRequest AgentTurn(string message, bool withTool) => new(AgentTurnId.New(), message, "tab-1", 1)
    {
        Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], withTool ? [ListDatabases] : [],
            AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None),
        Permissions = new AgentProviderPermissions { ProviderId = LocalAgentProvider.Id },
    };

    [Test, Explicit("Requer pacote de agente ONNX real em KAPI_AGENT_MODEL; sem ele permanece ignorado."), Category("LocalModelIntegration")]
    public async Task RealAgentModelAnswersATurnWithoutTools()
    {
        await using var agent = await RealAgent.StartAsync();
        await using var session = await agent.Provider.CreateSessionAsync(new(LocalAgentProvider.Id), CancellationToken.None);
        using var limit = new CancellationTokenSource(AgentTurnLimit);

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(AgentTurn("Responda em uma frase curta: o que é um índice no MongoDB?", withTool: false), limit.Token))
            events.Add(item);

        Assert.Multiple(() =>
        {
            Assert.That(events, Has.None.Matches<AgentProviderEvent>(item => item.Kind == AgentEventKind.AgentError),
                string.Join(", ", events.Where(item => item.Kind == AgentEventKind.AgentError).Select(item => item.Text)));
            Assert.That(events[^1].Kind, Is.EqualTo(AgentEventKind.MessageCompleted));
            Assert.That(string.Concat(events.Where(item => item.Kind == AgentEventKind.MessageDelta).Select(item => item.Text)).Trim(), Is.Not.Empty);
            Assert.That(events, Has.None.Matches<AgentProviderEvent>(item => item.Kind == AgentEventKind.ToolRequested),
                "Sem tools no plano, nenhuma chamada pode ser pedida.");
        });
    }

    [Test, Explicit("Requer pacote de agente ONNX real em KAPI_AGENT_MODEL; sem ele permanece ignorado."), Category("LocalModelIntegration")]
    public async Task RealAgentModelRequestsAnAllowedToolAndAnswersFromItsResult()
    {
        await using var agent = await RealAgent.StartAsync();
        await using var session = await agent.Provider.CreateSessionAsync(new(LocalAgentProvider.Id), CancellationToken.None);
        using var limit = new CancellationTokenSource(AgentTurnLimit);
        var request = AgentTurn("Quais bancos de dados existem na conexão \"c1\"? Use a ferramenta disponível antes de responder.", withTool: true);

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, limit.Token))
        {
            events.Add(item);
            if (item.Kind == AgentEventKind.ToolRequested)
                await session.SubmitToolResultAsync(new AgentToolResult(AgentSessionId.New(), request.TurnId, item.ToolCallId!.Value,
                    AgentToolResultStatus.Succeeded, "{\"databases\":[\"kapi_vendas\",\"kapi_logs\"]}"), CancellationToken.None);
        }

        var answer = string.Concat(events.Where(item => item.Kind == AgentEventKind.MessageDelta).Select(item => item.Text));
        Assert.Multiple(() =>
        {
            Assert.That(events, Has.None.Matches<AgentProviderEvent>(item => item.Kind == AgentEventKind.AgentError),
                string.Join(", ", events.Where(item => item.Kind == AgentEventKind.AgentError).Select(item => item.Text)));
            Assert.That(events.Where(item => item.Kind == AgentEventKind.ToolRequested).Select(item => item.ToolName), Has.All.EqualTo(ListDatabases));
            Assert.That(events.Count(item => item.Kind == AgentEventKind.ToolRequested), Is.GreaterThanOrEqualTo(1),
                "O modelo deveria pedir a única tool permitida.");
            Assert.That(events[^1].Kind, Is.EqualTo(AgentEventKind.MessageCompleted));
            Assert.That(answer, Does.Contain("kapi_vendas").Or.Contain("kapi_logs"), "A resposta deveria usar o resultado devolvido.");
        });
    }

    [Test, Explicit("Requer pacote de agente ONNX real em KAPI_AGENT_MODEL; sem ele permanece ignorado."), Category("LocalModelIntegration")]
    public async Task CancellingARealAgentTurnEndsItWithoutAnErrorAndTheSessionAnswersAgain()
    {
        await using var agent = await RealAgent.StartAsync();
        await using var session = await agent.Provider.CreateSessionAsync(new(LocalAgentProvider.Id), CancellationToken.None);
        using var limit = new CancellationTokenSource(AgentTurnLimit);
        var request = AgentTurn("Explique em detalhes, com muitos exemplos, como funcionam índices compostos.", withTool: false);

        var cancelled = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, limit.Token))
        {
            cancelled.Add(item);
            // Primeiro sinal de geração nativa: cancela com o modelo no meio do trabalho.
            if (cancelled.Count == 1) await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
        }
        var next = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(AgentTurn("Responda apenas: pronto.", withTool: false), limit.Token)) next.Add(item);

        Assert.Multiple(() =>
        {
            Assert.That(cancelled, Has.None.Matches<AgentProviderEvent>(item => item.Kind == AgentEventKind.AgentError));
            Assert.That(cancelled, Has.None.Matches<AgentProviderEvent>(item => item.Kind == AgentEventKind.MessageCompleted),
                "O turno cancelado não é publicado como concluído.");
            Assert.That(next[^1].Kind, Is.EqualTo(AgentEventKind.MessageCompleted), "A sessão continua utilizável depois do cancelamento.");
        });
    }

    private static AgentTurnRequest Turn(string message) => new(AgentTurnId.New(), message, "tab-1", 1);

    private static async Task<List<AgentProviderEvent>> CollectAsync(IAgentSession session, AgentTurnRequest request)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);
        return events;
    }
}
