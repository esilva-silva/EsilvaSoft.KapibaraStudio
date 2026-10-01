using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;
using MongoDB.Bson;
using MongoDB.Driver;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

#pragma warning disable GHCP001 // Explicit homologation of the pinned experimental SDK.
internal sealed partial class CopilotProductToolManualTests
{
    [Test, Category("MongoReal"), NonParallelizable, CancelAfter(240_000),
        Explicit("Copilot oficial e mongod descartável: uma inferência sintética com get_document, grant exato, confirmação pontual e auditoria durável; execução individual autorizada.")]
    public async Task OfficialModelReadsDisposableMongoDocumentWithOneCallApproval()
    {
        Assert.That(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), Is.True, "Requer runtime oficial em Windows ou Linux; executar não implica homologação.");
        using var syntheticDirectory = new SyntheticDirectory();
        const string toolName = AgentToolRegistry.GetDocumentToolName;
        using var server = await DisposableCopilotMongoServer.StartAsync(syntheticDirectory.Path);
        var database = "copilot_fixture_" + Guid.NewGuid().ToString("N");
        const string collection = "synthetic-items";
        const string label = "synthetic-copilot-document";
        var documentId = ObjectId.Parse("64b000000000000000000001");
        using (var seedClient = new MongoClient(server.Uri))
        {
            Assert.That(await seedClient.ListDatabaseNames().ToListAsync(), Does.Not.Contain(database));
            await seedClient.GetDatabase(database).GetCollection<BsonDocument>(collection).InsertOneAsync(
                new BsonDocument { ["_id"] = documentId, ["label"] = label,
                    ["big"] = new BsonInt64(9_007_199_254_740_993L) });
        }
        using var owner = new LiteDbConnectionProfileRepository(Path.Combine(syntheticDirectory.Path, "workspace.db"));
        using var pool = new MongoClientPool();
        // The only source is the loopback mongod process created for this test.
        var profile = ConnectionProfile.Create("Synthetic Copilot fixture", server.Uri) with
        {
            SourceGenerationId = Guid.NewGuid()
        };
        await owner.SaveAsync(profile);
        IConnectionProfileRepository profiles = owner;
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var policy = new SinglePolicyProvider();
        IAgentAuditRepository audit = owner;
        var find = new ObservedMongoFindSource(new MongoAgentFindSource(new SessionConnectionSecretStore(), null, pool));
        var prompt = new PreventiveMongoApprovalPrompt(find, audit);
        var nativeScopes = new AgentNativeChatTurnScopeRegistry();
        var permissions = AgentProviderPermissions.Default(CopilotSubscriptionAgentProvider.Id) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            EnabledReadTools = [toolName],
            ConnectionScope = AgentConnectionScope.Selected,
            SelectedConnectionIds = [profile.Id],
            DataSending = new AgentDataSendingPermissions { ActiveFile = false, MongoDocuments = true },
            EditProposals = new AgentEditProposalPermissions { ActiveFile = false, OtherWorkspaceFiles = false },
            ConfirmationCategories = AgentConfirmationCategories.MongoDocumentRead
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions,
            new AgentPlatformFacts(false, true, false));
        Assert.That(plan.ProductTools, Is.EqualTo([toolName]),
            "The synthetic tool test must disable unrelated default metadata, workspace, and editor tools.");
        var destination = AgentOutputDestination.ProviderExternal(CopilotSubscriptionAgentProvider.Id);
        var registry = new AgentToolRegistry(profiles, policy, new AgentPermissionEvaluator(policy), audit,
            find: find, exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
            principalAuthority: new TestAgentPrincipalAuthority(),
            sessionTools: new AgentSessionToolPorts(new AgentMcpSessionRegistry())
            {
                NativeChatTurnScopes = nativeScopes,
                ConfirmationPrompt = prompt,
                ApprovalTimeout = TimeSpan.FromSeconds(45)
            },
            copilotExposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));
        Assert.That(registry.FindInProcessDescriptor(CopilotSubscriptionAgentProvider.Id, toolName), Is.Not.Null,
            "The production registry fixture must release this tool only through Copilot exposure.");
        Assert.That(registry.GetInProcessInputSchemaJson(CopilotSubscriptionAgentProvider.Id, toolName), Is.Not.Null,
            "The production registry fixture must expose the closed input schema to the Copilot adapter.");

        string modelId;
        using var setupTimeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CurrentContext.CancellationToken);
        setupTimeout.CancelAfter(TimeSpan.FromSeconds(45));
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(setupTimeout.Token);
            var auth = await accountClient.GetAuthStatusAsync(setupTimeout.Token);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "A CLI oficial precisa reportar uma conta Copilot de usuário.");
            modelId = (await accountClient.ListModelsAsync(setupTimeout.Token))
                .Select(static model => model.Id)
                .FirstOrDefault(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                ?? throw new AssertionException("Nenhum modelo Copilot elegível foi retornado.");
        }

        await using var store = new CopilotVolatileSessionFsStore();
        var binding = new TrustedTestBinding(principal, AgentOutputDataScope.DocumentValues);
        var provider = new OfficialSessionProvider(registry, store);
        await using var runtime = new AgentRuntime([provider], toolRegistry: registry,
            toolBindings: binding, principalAuthority: new TestPrincipalAuthority(), nativeChatTurnScopes: nativeScopes);
        string? providerSessionId = null;
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(provider.ProviderId, modelId,
            syntheticDirectory.Path)
        {
            PersistProviderSession = false,
            ProviderSessionObserver = update =>
            {
                if (update.Change == AgentProviderSessionChange.Established)
                    providerSessionId = update.ProviderSessionId;
            },
        }, setupTimeout.Token);
        var turnId = AgentTurnId.New();
        var sessionKey = Guid.ParseExact(sessionId.Value, "N");
        var turnKey = Guid.ParseExact(turnId.Value, "N");
        policy.Set(principal.Id, 1,
        [
            new AgentPermissionGrant(principal.Id, AgentInvocationScope.ForTurn(sessionKey, turnKey),
                profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries,
                AgentNamespaceScope.ForCollection(profile.Id, database, collection), destination,
                AgentOutputDataScope.DocumentValues),
            new AgentPermissionGrant(principal.Id, AgentInvocationScope.ForTurn(sessionKey, turnKey),
                profile.SourceGenerationId.Value, AgentPermission.ReadDocuments,
                AgentNamespaceScope.ForCollection(profile.Id, database, collection), destination,
                AgentOutputDataScope.DocumentValues)
        ]);

        try
        {
            Assert.That(find.Calls, Is.Zero, "Conta, catálogo e criação de sessão não consultam o MongoDB.");
            using var turnTimeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CurrentContext.CancellationToken);
            turnTimeout.CancelAfter(TimeSpan.FromMinutes(2));
            var request = new AgentTurnRequest(turnId,
                $"Call get_document exactly once with connectionId '{profile.Id:D}', database '{database}', collection '{collection}' and idEjson {{\"$oid\":\"64b000000000000000000001\"}}. Do not call any other tool. After the tool returns, say only that the synthetic document was received.",
                "synthetic-copilot-approved-document-read", 1)
            {
                Plan = plan,
                Permissions = permissions,
                ConversationId = Guid.NewGuid()
            };
            var events = new List<AgentEvent>();
            await foreach (var item in runtime.RunTurnAsync(sessionId, request, turnTimeout.Token))
                events.Add(item);
            var safeEventSummary = string.Join(", ", events.Select(item =>
                $"{item.Sequence}:{item.Kind}:{item.ToolName ?? "-"}:{item.ErrorCode ?? "-"}"));

            var durableEvents = await audit.GetRecentAsync(100);
            Assert.That(find.LastPage, Is.Not.Null, "The real source must return a page.");
            var returnedDocument = BsonDocument.Parse(find.LastPage!.DocumentsEjson.Single());
            var ledgerJson = JsonSerializer.Serialize(durableEvents);
            Assert.Multiple(() =>
            {
                Assert.That(returnedDocument["_id"].AsObjectId, Is.EqualTo(documentId));
                Assert.That(returnedDocument["big"].BsonType, Is.EqualTo(BsonType.Int64));
                Assert.That(returnedDocument["big"].AsInt64, Is.EqualTo(9_007_199_254_740_993L));
                Assert.That(returnedDocument["label"].AsString, Is.EqualTo(label));
                Assert.That(ledgerJson, Does.Not.Contain(server.Uri).And.Not.Contain(label)
                    .And.Not.Contain("9007199254740993"));
                Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1), safeEventSummary);
                Assert.That(find.Calls, Is.EqualTo(1), "The registry must dispatch exactly once to the real Mongo source.");
                Assert.That(find.LastProfile?.Id, Is.EqualTo(profile.Id));
                Assert.That(find.LastByIdQuery?.Database, Is.EqualTo(database));
                Assert.That(find.LastByIdQuery?.Collection, Is.EqualTo("synthetic-items"));
                Assert.That(find.LastByIdQuery?.IdEjson, Does.Contain("64b000000000000000000001"));
                Assert.That(prompt.Requests, Has.Count.EqualTo(1));
                Assert.That(prompt.Requests[0].ProviderId, Is.EqualTo(CopilotSubscriptionAgentProvider.Id));
                Assert.That(prompt.Requests[0].ToolName, Is.EqualTo(toolName));
                Assert.That(prompt.Requests[0].Category, Is.EqualTo(AgentConfirmationCategories.MongoDocumentRead));
                Assert.That(prompt.Requests[0].InputJson, Does.Contain("synthetic-items"));
                Assert.That(durableEvents.Any(item => item.Outcome == AgentAuditOutcome.Succeeded &&
                    item.ApprovalState == AgentAuditApprovalState.ApprovedOnce && item.ToolName == toolName &&
                    item.TurnId == turnKey), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested && item.ToolName == toolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted && item.ToolName == toolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
            });
            Assert.That(await audit.GetPendingAsync(), Is.Empty);
            TestContext.Progress.WriteLine("Copilot oficial executou get_document no registry e MongoDB descartável; confirmação ApprovedOnce precedeu dispatch e auditoria durável encerrou o pedido. Nenhum prompt/resposta foi registrado.");
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await runtime.CloseSessionAsync(sessionId, cleanupTimeout.Token);
                if (providerSessionId is { } nativeId)
                    await runtime.DeleteProviderSessionAsync(provider.ProviderId, nativeId, cleanupTimeout.Token);
            }
            finally
            {
                await store.DisposeAsync();
            }
        }
    }

    private sealed class ObservedMongoFindSource(IAgentMongoFindSource source) : IAgentMongoFindSource
    {
        public int Calls { get; private set; }
        public ConnectionProfile? LastProfile { get; private set; }
        public AgentMongoFindByIdQuery? LastByIdQuery { get; private set; }
        public AgentMongoFindPage? LastPage { get; private set; }
        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken) => throw new AssertionException("Somente get_document foi autorizado.");
        public async Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastProfile = profile;
            LastByIdQuery = query;
            return LastPage = await source.FindByIdAsync(profile, query, cancellationToken);
        }
    }

    private sealed class PreventiveMongoApprovalPrompt(ObservedMongoFindSource source, IAgentAuditRepository audit)
        : IAgentToolConfirmationPrompt
    {
        public List<AgentToolConfirmationRequest> Requests { get; } = [];
        public async Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.That(source.Calls, Is.Zero, "A confirmação deve ocorrer antes de qualquer consulta ao MongoDB.");
            Assert.That((await audit.GetPendingAsync(cancellationToken: cancellationToken))
                .Any(item => item.ToolName == request.ToolName && item.Outcome == AgentAuditOutcome.Intent), Is.True,
                "A intenção de confirmação precisa estar durável antes da decisão.");
            Requests.Add(request);
            return AgentToolConfirmationDecision.ApprovedOnce;
        }
    }
}
#pragma warning restore GHCP001
