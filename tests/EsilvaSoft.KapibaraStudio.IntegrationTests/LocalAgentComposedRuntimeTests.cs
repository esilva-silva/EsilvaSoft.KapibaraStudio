using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static EsilvaSoft.KapibaraStudio.Testing.LocalAgentScript;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// KS-US05: o <see cref="LocalAgentProvider"/> real sobre a composição de produção (runtime, registry, política,
/// auditoria e LiteDB reais), com o modelo substituído por um roteiro. Não há pesos nem MongoDB: a fonte de metadados é
/// um espião, e é ele que prova que uma chamada negada não chega ao banco. Sucesso de tarefa com modelo real não é
/// medido aqui.
/// </summary>
[TestFixture]
[Category("Integration")]
public sealed class LocalAgentComposedRuntimeTests
{
    private const string ListDatabases = "list_databases";
    private const string Canary = "RACIOCINIO-CANARIO-7f3a91c2";
    private static readonly AutocompleteSettings Selected = new() { ModelPath = "models" };
    private static readonly AgentPlatformFacts Platform = new(HasWorkspaceFolder: false, ProductToolsAvailable: true, NativeToolsAvailable: false);

    private sealed class Composition : IAsyncDisposable
    {
        private readonly ConnectionCredentialRecoveryTests.Workspace _workspace = new();
        private readonly ServiceProvider _provider;

        public Composition(ScriptedAgentRuntime script)
        {
            Script = script;
            var catalog = new CompletionCatalogFake { Validation = new(AgentModel(maxToolCalls: 4, maxResultBytes: 2048), new(LocalModelState.Available, "available")) };
            var services = new ServiceCollection();
            services.AddKapibaraStudioInfrastructure(_workspace.Path,
                new AgentPlatformOptions { InProcessToolExposureStage = AgentToolExposureStage.Metadata });
            services.RemoveAll<ILocalAiModelService>();
            services.AddSingleton<ILocalAiModelService>(_ => new LocalAiModelService(catalog, () => script));
            services.RemoveAll<IAgentProvider>();
            services.AddSingleton<IAgentProvider>(provider => provider.GetRequiredService<LocalAgentProvider>());
            services.RemoveAll<ISecretStore>();
            services.AddSingleton<ISecretStore, InMemoryProfileSecretStore>();
            services.RemoveAll<IMongoMetadataSource>();
            services.AddSingleton<IMongoMetadataSource>(Metadata);
            _provider = services.BuildServiceProvider();
        }

        public ScriptedAgentRuntime Script { get; }
        public MetadataSpy Metadata { get; } = new();
        public string Directory => _workspace.DirectoryPath;
        public T Get<T>() where T : notnull => _provider.GetRequiredService<T>();

        /// <summary>Salva o perfil e as permissões do provider local e devolve o plano efetivo do turno.</summary>
        public async Task<(ConnectionProfile Profile, AgentProviderPermissions Permissions, AgentTurnPlan Plan)> ConfigureAsync(
            bool selectConnection, CancellationToken cancellationToken)
        {
            await Get<IAutocompleteService>().ConfigureAsync(Selected, cancellationToken);
            var profile = ConnectionProfile.Create("Fixture privada", "mongodb://fixture.invalid:27017");
            await Get<IConnectionProfileRepository>().SaveAsync(profile, cancellationToken);
            var saved = await Get<IAgentProviderPermissionsRepository>().SaveAsync(AgentProviderPermissions.Default(LocalAgentProvider.Id) with
            {
                ConnectionScope = AgentConnectionScope.Selected,
                SelectedConnectionIds = selectConnection ? [profile.Id] : [],
                EnabledReadTools = [ListDatabases],
            }, expectedRevision: 0, cancellationToken);
            Assert.That(saved.Succeeded, Is.True, saved.ErrorCode);
            var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, saved.Value!, Platform, requireExternalDestinationConsent: false);
            Assert.That(plan.ProductTools, Does.Contain(ListDatabases));
            return (profile, saved.Value!, plan);
        }

        public async Task<List<AgentEvent>> RunTurnAsync(string message, AgentProviderPermissions permissions, AgentTurnPlan plan,
            CancellationToken cancellationToken)
        {
            var runtime = Get<IAgentRuntime>();
            var session = await runtime.StartSessionAsync(new AgentSessionOptions(LocalAgentProvider.Id), cancellationToken);
            var events = new List<AgentEvent>();
            try
            {
                var request = new AgentTurnRequest(AgentTurnId.New(), message, "fixture-tab", 0)
                {
                    Plan = plan, Permissions = permissions, ConversationId = Guid.NewGuid(),
                };
                await foreach (var item in runtime.RunTurnAsync(session, request, cancellationToken)) events.Add(item);
            }
            finally
            {
                await runtime.CloseSessionAsync(session, CancellationToken.None);
            }
            return events;
        }

        public async ValueTask DisposeAsync()
        {
            await _provider.DisposeAsync();
            _workspace.Dispose();
        }
    }

    [Test]
    public async Task APermittedMetadataCallRunsThroughTheRealRegistryAndTheAnswerUsesItsResult()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var composition = new Composition(new ScriptedAgentRuntime());
        var (profile, permissions, plan) = await composition.ConfigureAsync(selectConnection: true, timeout.Token);
        composition.Metadata.Databases = ["vendas", "logs"];
        composition.Script
            .Then(Text(Call(ListDatabases, $"{{\"connectionId\":\"{profile.Id}\"}}")))
            .Then(Text("Há dois bancos."));

        var events = await composition.RunTurnAsync("Quais bancos existem?", permissions, plan, timeout.Token);

        var audit = await composition.Get<IAgentAuditRepository>().GetRecentAsync(20, timeout.Token);
        var toolMessage = composition.Script.Requests[1].ChatPrompt!.Messages.Single(message => message.Role == "tool").Content;
        Assert.Multiple(() =>
        {
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
            Assert.That(composition.Metadata.ListDatabaseNamesCalls, Is.EqualTo(1), "A chamada autorizada lê apenas nomes de bancos.");
            Assert.That(audit.Count(entry => entry.ToolName == ListDatabases && entry.Outcome == AgentAuditOutcome.Succeeded), Is.EqualTo(1));
            Assert.That(toolMessage, Does.Contain("vendas").And.Contain("logs").And.Contain("Succeeded"),
                "O resultado do registry volta ao modelo no envelope do contrato.");
            Assert.That(string.Concat(events.Where(item => item.Kind == AgentEventKind.MessageDelta).Select(item => item.Text)),
                Is.EqualTo("Há dois bancos."));
        });
    }

    [Test]
    public async Task ACallWithoutAGrantIsDeniedBeforeAnyMetadataReadAndTheModelSeesTheDenial()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var composition = new Composition(new ScriptedAgentRuntime());
        var (profile, permissions, plan) = await composition.ConfigureAsync(selectConnection: false, timeout.Token);
        composition.Script
            .Then(Text(Call(ListDatabases, $"{{\"connectionId\":\"{profile.Id}\"}}")))
            .Then(Text("Não tenho permissão para essa conexão."));

        var events = await composition.RunTurnAsync("Quais bancos existem?", permissions, plan, timeout.Token);

        var audit = await composition.Get<IAgentAuditRepository>().GetRecentAsync(20, timeout.Token);
        var denial = audit.Single(entry => entry.ToolName == ListDatabases && entry.Outcome == AgentAuditOutcome.Denied);
        using var toolMessage = JsonDocument.Parse(
            composition.Script.Requests[1].ChatPrompt!.Messages.Single(message => message.Role == "tool").Content);
        Assert.Multiple(() =>
        {
            Assert.That(composition.Metadata.ListDatabaseNamesCalls, Is.Zero, "A recusa acontece antes de qualquer leitura no MongoDB.");
            Assert.That(denial.Outcome, Is.EqualTo(AgentAuditOutcome.Denied));
            Assert.That(denial.DecisionReason, Is.EqualTo(AgentAuditDecisionReason.PermissionMissing));
            Assert.That(toolMessage.RootElement.GetProperty("status").GetString(), Is.EqualTo("Denied"));
            Assert.That(toolMessage.RootElement.GetProperty("error").GetString(), Is.EqualTo("PermissionDenied"));
            Assert.That(toolMessage.RootElement.GetProperty("result").GetString(), Does.Not.Contain(profile.Name)
                .And.Not.Contain("fixture.invalid"), "A recusa não revela a conexão.");
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.Completed), "O turno segue e o modelo pode explicar a recusa.");
        });
    }

    [TestCase("mongo_find")]
    [TestCase("mongo_count")]
    [TestCase("sample_documents")]
    [TestCase("mongo_find_one")]
    [TestCase("get_document")]
    [TestCase("mongo_distinct")]
    [TestCase("mongo_explain")]
    [TestCase("get_collection_schema")]
    [TestCase("insert_one")]
    [TestCase("update_one")]
    [TestCase("delete_one")]
    [TestCase("create_index")]
    [TestCase("drop_index")]
    public async Task APermanentlyRefusedToolNameNeverReachesTheRegistryPolicyAuditOrMongo(string refused)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var composition = new Composition(new ScriptedAgentRuntime());
        var (profile, permissions, plan) = await composition.ConfigureAsync(selectConnection: true, timeout.Token);
        composition.Script.Then(Text(Call(refused, $"{{\"connectionId\":\"{profile.Id}\",\"database\":\"vendas\",\"collection\":\"pedidos\"}}")));

        var events = await composition.RunTurnAsync("Consulte os pedidos diretamente.", permissions, plan, timeout.Token);

        var registry = composition.Get<IAgentToolRegistry>();
        var audit = await composition.Get<IAgentAuditRepository>().GetRecentAsync(20, timeout.Token);
        Assert.Multiple(() =>
        {
            Assert.That(events, Has.None.Matches<AgentEvent>(item => item.Kind == AgentEventKind.ToolRequested));
            Assert.That(events[^1].Outcome, Is.EqualTo(AgentTurnOutcome.Failed), "A aprovação do usuário nem chega a ser pedida.");
            Assert.That(composition.Script.Requests[0].ChatPrompt!.ToolsJson, Does.Not.Contain(refused), "O nome nunca é anunciado ao modelo.");
            Assert.That(registry.FindInProcessDescriptor(LocalAgentProvider.Id, refused), Is.Null);
            Assert.That(registry.GetInProcessInputSchemaJson(LocalAgentProvider.Id, refused), Is.Null);
            Assert.That(audit, Is.Empty, "Nada é auditado porque nada foi invocado.");
            Assert.That(composition.Metadata.TotalCalls, Is.Zero);
        });
    }

    [Test]
    public async Task RawReasoningStaysInReasoningEventsAndOutOfAuditPersistenceAndTheNextPrompts()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        string directory;
        await using (var composition = new Composition(new ScriptedAgentRuntime()))
        {
            directory = composition.Directory;
            var (profile, permissions, plan) = await composition.ConfigureAsync(selectConnection: true, timeout.Token);
            composition.Script
                .Then(Token(ThinkStart), Text("Vou listar. "), Text(Canary), Token(ThinkEnd),
                    Text(Call(ListDatabases, $"{{\"connectionId\":\"{profile.Id}\"}}")))
                .Then(Text("Pronto."))
                .Then(Text("Segundo turno."));

            var first = await composition.RunTurnAsync("Quais bancos existem?", permissions, plan, timeout.Token);
            var second = await composition.RunTurnAsync("E agora?", permissions, plan, timeout.Token);

            var audit = await composition.Get<IAgentAuditRepository>().GetRecentAsync(50, timeout.Token);
            var carriers = first.Concat(second).Where(item => JsonSerializer.Serialize(item).Contains(Canary, StringComparison.Ordinal)).ToArray();
            var prompts = composition.Script.Requests.Skip(1)
                .Select(request => string.Concat(request.ChatPrompt!.Messages.Select(message => message.Content)) + request.ChatPrompt.ToolsJson);
            var persisted = await composition.Get<LiteDbConnectionProfileRepository>().FindCollectionsContainingAsync([Canary]);
            Assert.Multiple(() =>
            {
                Assert.That(carriers, Is.Not.Empty, "Controle positivo: o canário foi mesmo publicado como raciocínio.");
                Assert.That(carriers.Select(item => item.Kind), Has.All.EqualTo(AgentEventKind.ReasoningDelta),
                    "Só o evento de raciocínio carrega o texto; mensagem, tool e conclusão não.");
                Assert.That(audit, Is.Not.Empty);
                Assert.That(JsonSerializer.Serialize(audit), Does.Not.Contain(Canary), "Auditoria não recebe raciocínio.");
                Assert.That(prompts, Has.None.Contains(Canary),
                    "O raciocínio não volta ao modelo no passo seguinte nem no turno seguinte.");
                Assert.That(persisted, Is.Empty, "Nenhuma coleção do LiteDB guarda o raciocínio sem opt-in.");
            });
        }

        Assert.That(ContainsInFiles(directory, Canary), Is.False, "Nenhum arquivo do workspace, nem sidecar, contém o raciocínio.");
    }

    /// <summary>Varredura de bytes do LiteDB e de qualquer sidecar, em UTF-8 e UTF-16LE.</summary>
    private static bool ContainsInFiles(string directory, string canary)
    {
        if (!Directory.Exists(directory)) return false;
        var patterns = new[] { Encoding.UTF8.GetBytes(canary), Encoding.Unicode.GetBytes(canary) };
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (FileNotFoundException) { continue; }
            if (patterns.Any(pattern => bytes.AsSpan().IndexOf(pattern) >= 0)) return true;
        }
        return false;
    }

    private sealed class MetadataSpy : IMongoMetadataSource
    {
        public IReadOnlyList<string> Databases { get; set; } = [];
        public int ListDatabaseNamesCalls { get; private set; }
        public int TotalCalls { get; private set; }

        public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum,
            CancellationToken cancellationToken)
        {
            ListDatabaseNamesCalls++;
            TotalCalls++;
            return Task.FromResult(new BoundedMetadataResult<string>(Databases, false));
        }

        private T Unexpected<T>() { TotalCalls++; throw new NotSupportedException("Leitura não prevista neste cenário."); }

        public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) =>
            Unexpected<Task<IReadOnlyList<string>>>();
        public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database,
            CancellationToken cancellationToken) => Unexpected<Task<IReadOnlyList<CollectionEntry>>>();
        public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database,
            int maximum, CancellationToken cancellationToken) => Unexpected<Task<BoundedMetadataResult<CollectionEntry>>>();
        public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection,
            CancellationToken cancellationToken) => Unexpected<Task<CollectionDefinition?>>();
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection,
            CancellationToken cancellationToken) => Unexpected<Task<IReadOnlyList<IndexInfo>>>();
        public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection,
            SchemaSampleOptions options, CancellationToken cancellationToken) => Unexpected<Task<IReadOnlyList<SampledDocument>>>();
        public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile,
            string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes,
            CancellationToken cancellationToken) => Unexpected<Task<ConcreteCollectionSchemaSampleResult>>();
    }
}
