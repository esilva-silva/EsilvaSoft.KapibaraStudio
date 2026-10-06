using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using Microsoft.Extensions.DependencyInjection;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Mcp;

[TestFixture, NonParallelizable, Category("Integration")]
public sealed class CopilotReadCredentialCompositionTests
{
    [TestCase("get_indexes", true)]
    [TestCase("get_indexes", false)]
    [TestCase("mongo_count", true)]
    [TestCase("mongo_count", false)]
    [TestCase("get_document", true)]
    [TestCase("get_document", false)]
    [TestCase("mongo_explain", true)]
    [TestCase("mongo_explain", false)]
    public async Task SelectedConnectionGatePrecedesCredentialAccessAndStoreFailureIsNotPermissionDenial(string tool, bool selected)
    {
        using var workspace = new McpTemporaryWorkspace();
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(workspace.DatabasePath);
        services.AddKapibaraStudioLocalAiInfrastructure();
        var store = new DeniedCredentialStore { DenyReads = false };
        services.AddSingleton<ISecretStore>(store);
        var providerId = AgentProviderIds.GitHubCopilotSubscription;
        services.AddSingleton<IAgentProvider>(new ScriptedAgentProvider(providerId));
        using var container = services.BuildServiceProvider();
        var profiles = container.GetRequiredService<IConnectionProfileRepository>();
        var profile = ConnectionProfile.Create("Synthetic", "mongodb://reader@synthetic.invalid/app") with
        {
            SecretReference = new SecretReference(Guid.NewGuid())
        };
        await profiles.SaveAsync(profile);
        profile = (await profiles.GetAllAsync()).Single();
        store.DenyReads = true;
        store.Reads.Clear(); // Only dispatch reads count; creating the synthetic profile validates its credential.
        var permissions = AgentProviderPermissions.Default(providerId) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            ConnectionScope = AgentConnectionScope.Selected,
            SelectedConnectionIds = [selected ? profile.Id : Guid.NewGuid()],
            EnabledReadTools = [tool],
            DataSending = new AgentDataSendingPermissions { MongoDocuments = true },
            ConfirmationCategories = AgentConfirmationCategories.None
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new(false, true, false));
        var sessionId = AgentSessionId.New();
        var turnId = AgentTurnId.New();
        var sessionGuid = Guid.ParseExact(sessionId.Value, "N");
        var turnGuid = Guid.ParseExact(turnId.Value, "N");
        var turns = container.GetRequiredService<IAgentNativeChatTurnScopes>();
        turns.Register(new AgentNativeChatTurnScope(sessionGuid, turnGuid, providerId, plan, permissions, null));
        try
        {
            var binding = await container.GetRequiredService<IAgentToolBindingProvider>()
                .ResolveAsync(sessionId, turnId, providerId, tool, CancellationToken.None);
            Assert.That(binding, Is.Not.Null);
            var arguments = tool switch
            {
                "get_document" => System.Text.Json.JsonSerializer.Serialize(new
                {
                    connectionId = profile.Id, database = "app", collection = "items", idEjson = "{\"$numberLong\":\"7\"}"
                }),
                "mongo_explain" => System.Text.Json.JsonSerializer.Serialize(new
                {
                    connectionId = profile.Id, database = "app", collection = "items", filterEjson = "{}", limit = 1
                }),
                _ => System.Text.Json.JsonSerializer.Serialize(new
                {
                    connectionId = profile.Id, database = "app", collection = "items"
                })
            };
            var result = await container.GetRequiredService<IAgentToolRegistry>().InvokeAsync(binding!.Principal,
                new AgentInvocationContext(providerId, null, sessionGuid, turnGuid), binding.Destination,
                binding.OutputDataScope, tool, arguments);
            var audit = await container.GetRequiredService<IAgentAuditRepository>().GetRecentAsync();
            Assert.Multiple(() =>
            {
                Assert.That(result.Succeeded, Is.False);
                Assert.That(result.ErrorCode, Is.EqualTo(selected ? "ExecutionFailed" : "PermissionDenied"));
                Assert.That(store.Reads, Has.Count.EqualTo(selected ? 1 : 0), "Recusa de conexão nunca acessa o cofre.");
                Assert.That(result.StructuredContentJson, Is.Null);
                Assert.That(audit, Has.Count.EqualTo(2));
                Assert.That(audit.Single(item => item.Outcome != AgentAuditOutcome.Intent).DecisionReason,
                    Is.EqualTo(selected ? AgentAuditDecisionReason.ExecutionFailed : AgentAuditDecisionReason.PermissionMissing));
            });
        }
        finally { turns.Remove(sessionGuid, turnGuid); }
    }

    [TestCase("get_indexes")]
    [TestCase("mongo_find")]
    [TestCase("mongo_find_one")]
    [TestCase("sample_documents")]
    [TestCase("get_document")]
    [TestCase("mongo_count")]
    [TestCase("mongo_distinct")]
    [TestCase("mongo_explain")]
    public void ProductReadSourcesUseTheComposedConnectionCredentialStore(string tool)
    {
        using var workspace = new McpTemporaryWorkspace();
        var services = new ServiceCollection();
        services.AddKapibaraStudioInfrastructure(workspace.DatabasePath);
        var store = new DeniedCredentialStore();
        services.AddSingleton<ISecretStore>(store);
        using var container = services.BuildServiceProvider();
        var reference = new SecretReference(Guid.NewGuid());
        var profile = ConnectionProfile.Create("Synthetic", "mongodb://reader@synthetic.invalid/app") with
        {
            SourceGenerationId = Guid.NewGuid(), SecretReference = reference
        };

        var failure = Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await InvokeAsync(container, tool, profile));

        Assert.Multiple(() =>
        {
            Assert.That(store.Reads, Is.EqualTo(new[] { reference }),
                "As tools precisam resolver a referência pelo mesmo cofre composto para o IDE antes de pedir cliente.");
            Assert.That(failure!.Message, Does.Not.Contain("synthetic.invalid").And.Not.Contain("canary"));
        });
    }

    private static Task InvokeAsync(ServiceProvider container, string tool, ConnectionProfile profile) => tool switch
    {
        "get_indexes" => container.GetRequiredService<MongoAgentIndexSource>().GetIndexesAsync(profile,
            "app", "items", TimeSpan.FromSeconds(1), CancellationToken.None),
        "mongo_explain" => container.GetRequiredService<MongoAgentExplainSource>().ExplainAsync(profile,
            new AgentMongoFindQuery("app", "items", "{}", null, null, 1, 0, 1_000), CancellationToken.None),
        "mongo_count" => container.GetRequiredService<MongoAgentFindSource>().CountAsync(profile,
            new AgentMongoCountQuery("app", "items", "{}", 1_000), CancellationToken.None),
        "mongo_distinct" => container.GetRequiredService<MongoAgentFindSource>().DistinctAsync(profile,
            new AgentMongoDistinctQuery("app", "items", "value", "{}", 10, 1_000), CancellationToken.None),
        "get_document" => container.GetRequiredService<MongoAgentFindSource>().FindByIdAsync(profile,
            new AgentMongoFindByIdQuery("app", "items", "{\"$numberLong\":\"7\"}", 1_000), CancellationToken.None),
        _ => container.GetRequiredService<MongoAgentFindSource>().FindAsync(profile,
            new AgentMongoFindQuery("app", "items", "{}", null, null, 1, 0, 1_000), CancellationToken.None)
    };

    private sealed class DeniedCredentialStore : ISecretStore
    {
        public List<SecretReference> Reads { get; } = [];
        public bool DenyReads { get; set; } = true;
        public Task<SecretStoreResult<SecretStoreAvailability>> GetAvailabilityAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Success(SecretStoreAvailability.Available));
        public Task<SecretStoreResult<string>> GetAsync(SecretReference reference, CancellationToken cancellationToken = default)
        {
            Reads.Add(reference);
            return Task.FromResult(DenyReads ? SecretStoreResults.Failed<string>(SecretStoreFailureCode.Denied)
                : SecretStoreResults.Success("mongodb://reader:synthetic-password-canary@synthetic.invalid/app"));
        }
        public Task<SecretStoreOperationResult> SetAsync(SecretReference reference, string secret,
            CancellationToken cancellationToken = default) => throw new AssertionException("Não pode gravar credencial.");
        public Task<SecretStoreOperationResult> DeleteAsync(SecretReference reference,
            CancellationToken cancellationToken = default) => throw new AssertionException("Não pode apagar credencial.");
    }
}
