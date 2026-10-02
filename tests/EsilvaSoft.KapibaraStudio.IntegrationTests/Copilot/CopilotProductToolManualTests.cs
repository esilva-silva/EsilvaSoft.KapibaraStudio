using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using Avalonia.Headless;
using System.Text.Json;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

#pragma warning disable GHCP001 // Homologação manual do SDK experimental pinado, em sessão isolada.
[TestFixture, Category("Integration"), Category("OfficialManual"), Category("OfficialCli")]
internal sealed partial class CopilotProductToolManualTests
{
    private const string ToolName = "get_workspace_context";
    private const string ToolPayload = "{\"context\":\"synthetic workspace context; no real paths or files\"}";

    [Test, Explicit("Homologação manual Windows/Linux com Copilot: o modelo chama uma ferramenta sintética, sem banco nem dados do workspace real.")]
    public async Task OfficialModelCallsSyntheticProductToolThroughAgentRuntime()
    {
        Assert.That(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), Is.True, "Requer runtime oficial em Windows ou Linux; executar não implica homologação.");
        using var syntheticDirectory = new SyntheticDirectory();

        string modelId;
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(CancellationToken.None);
            var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "A CLI oficial precisa reportar uma conta Copilot de usuário.");
            modelId = (await accountClient.ListModelsAsync(CancellationToken.None))
                .Select(static model => model.Id)
                .FirstOrDefault(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                ?? throw new AssertionException("Nenhum modelo Copilot elegível foi retornado.");
        }

        await using var store = new CopilotVolatileSessionFsStore();
        var registry = new SyntheticWorkspaceToolRegistry();
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var binding = new TrustedTestBinding(principal);
        var provider = new OfficialSessionProvider(registry, store);
        await using var runtime = new AgentRuntime([provider], toolRegistry: registry,
            toolBindings: binding, principalAuthority: new TestPrincipalAuthority());
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
        }, CancellationToken.None);

        try
        {
            using var turnTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var request = new AgentTurnRequest(AgentTurnId.New(),
                "Use the get_workspace_context tool now with scope 'active'. Do not invent context. After the tool returns, state only that the synthetic context was received.",
                "synthetic-copilot-tool-check", 1)
            {
                Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [ToolName],
                    AgentProposalHandling.Disabled, false, AgentConfirmationCategories.WorkspaceContextRead),
            };

            var events = new List<AgentEvent>();
            await foreach (var item in runtime.RunTurnAsync(sessionId, request, turnTimeout.Token))
                events.Add(item);
            Assert.Multiple(() =>
            {
                Assert.That(registry.Invocations, Is.EqualTo(1), "The real model must request the declared synthetic product tool once.");
                Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1), "Tool dispatch must pass through the trusted runtime binding.");
                Assert.That(registry.LastPrincipal, Is.EqualTo(principal));
                Assert.That(registry.LastDestination, Is.EqualTo(AgentOutputDestination.ProviderExternal(provider.ProviderId)));
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested && item.ToolName == ToolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted && item.ToolName == ToolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
            });
            TestContext.Progress.WriteLine("Runtime Copilot oficial chamou a ferramenta sintética através de AgentRuntime; nenhum texto de prompt/resposta, caminho ou dado real foi registrado.");
        }
        finally
        {
            try
            {
                await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
                if (providerSessionId is { } nativeId)
                    await runtime.DeleteProviderSessionAsync(provider.ProviderId, nativeId, CancellationToken.None);
            }
            finally
            {
                await store.DisposeAsync();
            }
        }
    }

    [Test, Explicit("Homologação manual Windows/Linux com conta Copilot autenticada: modelo oficial chama get_workspace_context no registry real usando somente uma pasta temporária sintética.")]
    public async Task OfficialModelCallsWorkspaceContextThroughProductionRegistry()
    {
        Assert.That(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), Is.True, "Requer runtime oficial em Windows ou Linux; executar não implica homologação.");
        using var syntheticDirectory = new SyntheticDirectory();
        using var rig = new CopilotProductToolTestRig();
        var permissions = AgentProviderPermissions.Default(CopilotSubscriptionAgentProvider.Id) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            DataSending = new AgentDataSendingPermissions { TabMetadata = true },
            Workspace = new AgentWorkspacePermissions { UseFilesFolder = true }
        };
        var plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [ToolName],
            AgentProposalHandling.Disabled, false, AgentConfirmationCategories.WorkspaceContextRead);
        string modelId;
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(CancellationToken.None);
            var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "A CLI oficial precisa reportar uma conta Copilot de usuário.");
            modelId = (await accountClient.ListModelsAsync(CancellationToken.None))
                .Select(static model => model.Id)
                .FirstOrDefault(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                ?? throw new AssertionException("Nenhum modelo Copilot elegível foi retornado.");
        }

        await using var store = new CopilotVolatileSessionFsStore();
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var binding = new TrustedTestBinding(principal);
        var provider = new OfficialSessionProvider(rig.Registry, store);
        await using var runtime = new AgentRuntime([provider], toolRegistry: rig.Registry,
            toolBindings: binding, principalAuthority: new TestPrincipalAuthority(),
            nativeChatTurnScopes: rig.NativeChatScopes);
        string? providerSessionId = null;
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(provider.ProviderId, modelId,
            rig.WorkspaceFolder)
        {
            PersistProviderSession = false,
            ProviderSessionObserver = update =>
            {
                if (update.Change == AgentProviderSessionChange.Established)
                    providerSessionId = update.ProviderSessionId;
            },
        }, CancellationToken.None);

        try
        {
            using var turnTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var request = new AgentTurnRequest(AgentTurnId.New(),
                "Call get_workspace_context exactly once. It contains only a temporary synthetic workspace path and no database connection. After it returns, say only that the synthetic context was received.",
                "synthetic-copilot-workspace-tool-check", 1)
            {
                Plan = plan,
                Permissions = permissions,
                WorkspaceContext = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder),
                ConversationId = Guid.NewGuid()
            };

            var events = new List<AgentEvent>();
            await foreach (var item in runtime.RunTurnAsync(sessionId, request, turnTimeout.Token))
                events.Add(item);

            Assert.Multiple(() =>
            {
                Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1));
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested && item.ToolName == ToolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted && item.ToolName == ToolName), Is.True,
                    "The production AgentToolRegistry must authorize and execute the planned tool.");
                Assert.That(rig.Audit.Events, Is.Not.Empty, "The production registry must audit the tool invocation.");
                Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
            });
            TestContext.Progress.WriteLine("Modelo oficial Copilot chamou get_workspace_context no registry real com plano, permissão e snapshot temporário sintético; nenhum banco ou arquivo real foi usado.");
        }
        finally
        {
            try
            {
                await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
                if (providerSessionId is { } nativeId)
                    await runtime.DeleteProviderSessionAsync(provider.ProviderId, nativeId, CancellationToken.None);
            }
            finally
            {
                await store.DisposeAsync();
            }
        }
    }

    [Test, Explicit("Homologação manual Windows/Linux: modelo Copilot chama list_connections no registry real; somente perfil sintético autorizado, sem abrir MongoDB.")]
    public async Task OfficialModelCallsMongoMetadataToolWithScopedPermission()
    {
        Assert.That(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), Is.True, "Requer runtime oficial em Windows ou Linux; executar não implica homologação.");
        using var syntheticDirectory = new SyntheticDirectory();
        using var rig = new CopilotProductToolTestRig();
        var providerId = CopilotSubscriptionAgentProvider.Id;
        var outputDestination = AgentOutputDestination.ProviderExternal(providerId);
        var permissions = AgentProviderPermissions.Default(providerId) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            DataSending = new AgentDataSendingPermissions { TabMetadata = true }
        };
        var plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [AgentToolRegistry.ListConnectionsToolName],
            AgentProposalHandling.Disabled, false, AgentConfirmationCategories.MongoMetadataRead);
        string modelId;
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(CancellationToken.None);
            var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "A CLI oficial precisa reportar uma conta Copilot de usuário.");
            modelId = (await accountClient.ListModelsAsync(CancellationToken.None))
                .Select(static model => model.Id)
                .FirstOrDefault(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                ?? throw new AssertionException("Nenhum modelo Copilot elegível foi retornado.");
        }

        await using var store = new CopilotVolatileSessionFsStore();
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var binding = new TrustedTestBinding(principal);
        var provider = new OfficialSessionProvider(rig.Registry, store);
        await using var runtime = new AgentRuntime([provider], toolRegistry: rig.Registry,
            toolBindings: binding, principalAuthority: new TestPrincipalAuthority());
        string? providerSessionId = null;
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(provider.ProviderId, modelId,
            rig.WorkspaceFolder)
        {
            PersistProviderSession = false,
            ProviderSessionObserver = update =>
            {
                if (update.Change == AgentProviderSessionChange.Established)
                    providerSessionId = update.ProviderSessionId;
            },
        }, CancellationToken.None);

        try
        {
            var sessionKey = Guid.ParseExact(sessionId.Value, "N");
            rig.Policies.Set(principal.Id, 1,
            [
                new AgentPermissionGrant(principal.Id, AgentInvocationScope.ForSession(sessionKey),
                    rig.Profile.SourceGenerationId!.Value, AgentPermission.ReadMetadata,
                    AgentNamespaceScope.ForConnection(rig.Profile.Id), outputDestination, AgentOutputDataScope.Metadata)
            ]);
            var authorizedProfiles = await rig.Registry.InvokeAsync(principal,
                new AgentInvocationContext(provider.ProviderId, null, sessionKey, Guid.NewGuid()), outputDestination,
                AgentOutputDataScope.Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");
            Assert.That(authorizedProfiles.Succeeded, Is.True, authorizedProfiles.ErrorCode);
            using (var authorizedJson = JsonDocument.Parse(authorizedProfiles.StructuredContentJson!))
            {
                var visible = authorizedJson.RootElement.GetProperty("connections").EnumerateArray().ToArray();
                Assert.That(visible, Has.Length.EqualTo(1), "Perfil sem grant precisa ficar invisível.");
                Assert.That(Guid.Parse(visible[0].GetProperty("id").GetString()!), Is.EqualTo(rig.Profile.Id));
            }
            using var turnTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var request = new AgentTurnRequest(AgentTurnId.New(),
                "Use list_connections exactly once. The output is synthetic and may contain only the authorized connection. Do not call any database or collection tool. Then report the number of visible connections.",
                "synthetic-copilot-list-connections", 1)
            {
                Plan = plan,
                Permissions = permissions,
                ConversationId = Guid.NewGuid()
            };
            var events = new List<AgentEvent>();
            await foreach (var item in runtime.RunTurnAsync(sessionId, request, turnTimeout.Token))
                events.Add(item);

            Assert.Multiple(() =>
            {
                Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1));
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested &&
                    item.ToolName == AgentToolRegistry.ListConnectionsToolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted &&
                    item.ToolName == AgentToolRegistry.ListConnectionsToolName), Is.True,
                    "The production registry must apply the session-scoped permission grant.");
                Assert.That(rig.Audit.Events, Is.Not.Empty, "The production registry must audit the metadata read.");
                Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
            });
            TestContext.Progress.WriteLine("Modelo oficial Copilot executou list_connections pelo registry de produção; grant restrito ao perfil sintético, sem conexão MongoDB nem URI na resposta.");
        }
        finally
        {
            try
            {
                await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
                if (providerSessionId is { } nativeId)
                    await runtime.DeleteProviderSessionAsync(provider.ProviderId, nativeId, CancellationToken.None);
            }
            finally
            {
                await store.DisposeAsync();
            }
        }
    }

    [Test, Explicit("Homologação manual Windows/Linux com Copilot oficial: get_document exige ApprovedOnce e grant exato do turno; a origem Mongo é fake e não conecta a servidor.")]
    public async Task OfficialModelCallsSyntheticDocumentReadWithOneCallApproval()
    {
        Assert.That(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), Is.True, "Requer runtime oficial em Windows ou Linux; executar não implica homologação.");
        using var syntheticDirectory = new SyntheticDirectory();
        const string toolName = AgentToolRegistry.GetDocumentToolName;
        const string syntheticDocument = "{\"_id\":{\"$oid\":\"64b000000000000000000001\"},\"label\":\"synthetic-copilot-document\"}";

        // The profile string is intentionally non-routable, and the registry receives only SyntheticFindSource.
        // No MongoDB driver/source is constructed by this test.
        var profile = ConnectionProfile.Create("Synthetic Copilot fixture", "mongodb://synthetic.invalid:27017") with
        {
            SourceGenerationId = Guid.NewGuid()
        };
        var profiles = new SingleProfileRepository(profile);
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var policy = new SinglePolicyProvider();
        var audit = new SyntheticAudit();
        var find = new SyntheticFindSource(syntheticDocument);
        var prompt = new ApproveOncePrompt();
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
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(CancellationToken.None);
            var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "A CLI oficial precisa reportar uma conta Copilot de usuário.");
            modelId = (await accountClient.ListModelsAsync(CancellationToken.None))
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
        }, CancellationToken.None);
        var turnId = AgentTurnId.New();
        var sessionKey = Guid.ParseExact(sessionId.Value, "N");
        var turnKey = Guid.ParseExact(turnId.Value, "N");
        policy.Set(principal.Id, 1,
        [
            new AgentPermissionGrant(principal.Id, AgentInvocationScope.ForTurn(sessionKey, turnKey),
                profile.SourceGenerationId!.Value, AgentPermission.ExecuteReadQueries,
                AgentNamespaceScope.ForCollection(profile.Id, "synthetic-db", "synthetic-items"), destination,
                AgentOutputDataScope.DocumentValues),
            new AgentPermissionGrant(principal.Id, AgentInvocationScope.ForTurn(sessionKey, turnKey),
                profile.SourceGenerationId.Value, AgentPermission.ReadDocuments,
                AgentNamespaceScope.ForCollection(profile.Id, "synthetic-db", "synthetic-items"), destination,
                AgentOutputDataScope.DocumentValues)
        ]);

        try
        {
            using var turnTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var request = new AgentTurnRequest(turnId,
                $"Call get_document exactly once with connectionId '{profile.Id:D}', database 'synthetic-db', collection 'synthetic-items' and idEjson {{\"$oid\":\"64b000000000000000000001\"}}. Do not call any other tool. After the tool returns, say only that the synthetic document was received.",
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

            Assert.Multiple(() =>
            {
                Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1), safeEventSummary);
                Assert.That(find.Calls, Is.EqualTo(1), "The registry must dispatch exactly once to the fake source.");
                Assert.That(find.LastProfile?.Id, Is.EqualTo(profile.Id));
                Assert.That(find.LastByIdQuery?.Database, Is.EqualTo("synthetic-db"));
                Assert.That(find.LastByIdQuery?.Collection, Is.EqualTo("synthetic-items"));
                Assert.That(find.LastByIdQuery?.IdEjson, Does.Contain("64b000000000000000000001"));
                Assert.That(prompt.Requests, Has.Count.EqualTo(1));
                Assert.That(prompt.Requests[0].ProviderId, Is.EqualTo(CopilotSubscriptionAgentProvider.Id));
                Assert.That(prompt.Requests[0].ToolName, Is.EqualTo(toolName));
                Assert.That(prompt.Requests[0].Category, Is.EqualTo(AgentConfirmationCategories.MongoDocumentRead));
                Assert.That(prompt.Requests[0].InputJson, Does.Contain("synthetic-items"));
                Assert.That(audit.Events.Any(item => item.Outcome == AgentAuditOutcome.Succeeded &&
                    item.ApprovalState == AgentAuditApprovalState.ApprovedOnce && item.ToolName == toolName &&
                    item.TurnId == turnKey), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested && item.ToolName == toolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted && item.ToolName == toolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
            });
            TestContext.Progress.WriteLine("Runtime Copilot oficial pediu get_document; grant restrito ao turno e prompt fake ApprovedOnce; dispatch confirmado somente contra origem Mongo sintética.");
        }
        finally
        {
            try
            {
                await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
                if (providerSessionId is { } nativeId)
                    await runtime.DeleteProviderSessionAsync(provider.ProviderId, nativeId, CancellationToken.None);
            }
            finally
            {
                await store.DisposeAsync();
            }
        }
    }

    [Test, Explicit("Homologação manual Windows/Linux com Copilot oficial: envia uma consulta sintética como anexo e verifica proposta active_buffer no sink fake; não edita disco nem acessa MongoDB.")]
    public async Task OfficialModelProposesEditToSyntheticActiveBuffer()
    {
        Assert.That(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), Is.True, "Requer runtime oficial em Windows ou Linux; executar não implica homologação.");
        using var syntheticDirectory = new SyntheticDirectory();
        const string original = "db.syntheticItems.find({}).limit(10);\n";
        const string toolName = AgentToolRegistry.ProposeFileEditToolName;
        var providerId = CopilotSubscriptionAgentProvider.Id;
        var permissions = AgentProviderPermissions.Default(providerId) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            EnabledReadTools = [],
            DataSending = new AgentDataSendingPermissions
            {
                ActiveFile = true,
                WorkspaceFiles = false,
                ExternalAttachments = false,
                TabMetadata = false,
                InferredSchema = false,
                MongoDocuments = false
            },
            Workspace = new AgentWorkspacePermissions { UseFilesFolder = false },
            EditProposals = new AgentEditProposalPermissions { ActiveFile = true, OtherWorkspaceFiles = false }
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions,
            new AgentPlatformFacts(HasWorkspaceFolder: false, ProductToolsAvailable: true, NativeToolsAvailable: false));
        Assert.That(plan.ProductTools, Is.EqualTo([toolName]), "The official model must receive only the active-buffer proposal tool.");

        var activeBuffer = new AgentWorkspaceContext(DateTimeOffset.UtcNow, WorkspaceFolder: null,
            ActiveFileName: "consulta-sintetica.js", TabId: "synthetic-query-tab", DocumentVersion: 7,
            BufferText: original);
        var workspace = new CopilotProductToolTestRig.FakeWorkspace { Context = activeBuffer };
        var sink = new CopilotProductToolTestRig.FakeSink();
        var profile = ConnectionProfile.Create("Synthetic Copilot profile", "mongodb://synthetic.invalid:27017") with
        {
            SourceGenerationId = Guid.NewGuid()
        };
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var policy = new CopilotProductToolTestRig.MapPolicies();
        var audit = new CopilotProductToolTestRig.MemoryAudit();
        var nativeScopes = new AgentNativeChatTurnScopeRegistry();
        var registry = new AgentToolRegistry(new SingleProfileRepository(profile), policy,
            new AgentPermissionEvaluator(policy), audit,
            exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
            principalAuthority: new TestAgentPrincipalAuthority(),
            sessionTools: new AgentSessionToolPorts(new AgentMcpSessionRegistry())
            {
                NativeChatTurnScopes = nativeScopes,
                WorkspaceContext = workspace,
                ProposalSink = sink
            },
            copilotExposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));
        Assert.That(registry.FindInProcessDescriptor(providerId, toolName), Is.Not.Null);
        Assert.That(registry.GetInProcessInputSchemaJson(providerId, toolName), Is.Not.Null);

        string modelId;
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(CancellationToken.None);
            var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "A CLI oficial precisa reportar uma conta Copilot de usuário.");
            modelId = (await accountClient.ListModelsAsync(CancellationToken.None))
                .Select(static model => model.Id)
                .FirstOrDefault(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                ?? throw new AssertionException("Nenhum modelo Copilot elegível foi retornado.");
        }

        await using var store = new CopilotVolatileSessionFsStore();
        var binding = new TrustedTestBinding(principal);
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
        }, CancellationToken.None);

        try
        {
            using var turnTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var request = new AgentTurnRequest(AgentTurnId.New(),
                "The attached active editor buffer is synthetic test data. Change only `.limit(10)` to `.limit(5)` by calling propose_file_edit exactly once with target `active_buffer` and no path. Do not call any other tool and do not claim the edit was applied; after the tool returns, state that a review proposal was created.",
                "synthetic-copilot-active-buffer-edit", 7)
            {
                Plan = plan,
                Permissions = permissions,
                WorkspaceContext = activeBuffer,
                ConversationId = Guid.NewGuid(),
                Attachments = [new AgentContextAttachment(AgentAttachmentKind.ActiveFile,
                    "consulta-sintetica.js", null, original, System.Text.Encoding.UTF8.GetByteCount(original),
                    Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original))))]
            };
            var events = new List<AgentEvent>();
            await foreach (var item in runtime.RunTurnAsync(sessionId, request, turnTimeout.Token))
                events.Add(item);
            var safeEventSummary = string.Join(", ", events.Select(item =>
                $"{item.Sequence}:{item.Kind}:{item.ToolName ?? "-"}:{item.ErrorCode ?? "-"}"));

            Assert.Multiple(() =>
            {
                Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1), safeEventSummary);
                Assert.That(sink.Proposals, Has.Count.EqualTo(1), "The registry must register one synthetic edit proposal.");
                Assert.That(sink.Proposals[0].TargetPath, Is.Null);
                Assert.That(sink.Proposals[0].TabId, Is.EqualTo("synthetic-query-tab"));
                Assert.That(sink.Proposals[0].OriginalText, Is.EqualTo(original));
                Assert.That(sink.Proposals[0].ProposedText, Is.EqualTo("db.syntheticItems.find({}).limit(5);\n"));
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested && item.ToolName == toolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted && item.ToolName == toolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
                Assert.That(audit.Events, Is.Not.Empty, "The registry must audit the proposal tool call.");
            });
            TestContext.Progress.WriteLine("Copilot oficial propôs a edição de um buffer sintético via registry; sink fake, sem gravar em disco ou conectar ao MongoDB.");
        }
        finally
        {
            try
            {
                await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
                if (providerSessionId is { } nativeId)
                    await runtime.DeleteProviderSessionAsync(provider.ProviderId, nativeId, CancellationToken.None);
            }
            finally
            {
                await store.DisposeAsync();
            }
        }
    }

    [Test, Explicit("Homologação manual Windows/Linux: runtime oficial propõe alteração na aba sintética ativa pelo WorkspaceViewModel e store Desktop local; não aplica, salva nem conecta ao MongoDB.")]
    public async Task OfficialModelProposesEditIntoActiveWorkspaceViewModelStore()
    {
        Assert.That(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), Is.True, "Requer runtime oficial em Windows ou Linux; executar não implica homologação.");
        using var syntheticDirectory = new SyntheticDirectory();
        var headless = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        await headless.Dispatch(async () =>
        {
            const string original = "db.syntheticItems.find({}).limit(10);\n";
            using var workspaceContext = new WorkspaceTestContext();
            var store = new AgentEditProposalStore();
            var workspaceSource = new DesktopAgentWorkspaceContextSource();
            using var workspace = new WorkspaceViewModel(workspaceContext.Workspace, workspaceContext.Repository,
                agentWorkspaceContext: workspaceSource, agentEditProposals: store);
            await workspace.InitializeAsync();
            var originTab = workspace.ActiveTab!;
            originTab.Text = original;
            var activeBuffer = workspace.CaptureWorkspace();
            var tabId = originTab.Id.ToString("N");
            Assert.Multiple(() =>
            {
                Assert.That(activeBuffer.TabId, Is.EqualTo(tabId));
                Assert.That(activeBuffer.BufferText, Is.EqualTo(original));
                Assert.That(originTab.FilePath, Is.Null.Or.Empty);
            });

            var providerId = CopilotSubscriptionAgentProvider.Id;
            var permissions = AgentProviderPermissions.Default(providerId) with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                EnabledReadTools = [],
                DataSending = new AgentDataSendingPermissions { ActiveFile = true },
                Workspace = new AgentWorkspacePermissions { UseFilesFolder = false },
                EditProposals = new AgentEditProposalPermissions { ActiveFile = true, OtherWorkspaceFiles = false }
            };
            var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions,
                new AgentPlatformFacts(HasWorkspaceFolder: false, ProductToolsAvailable: true, NativeToolsAvailable: false));
            Assert.That(plan.ProductTools, Is.EqualTo([AgentToolRegistry.ProposeFileEditToolName]));
            var nativeScopes = new AgentNativeChatTurnScopeRegistry();
            var policies = new SinglePolicyProvider();
            var audit = new SyntheticAudit();
            var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
            var registry = new AgentToolRegistry(new SingleProfileRepository(ConnectionProfile.Create(
                    "Synthetic Copilot profile", "mongodb://synthetic.invalid:27017") with { SourceGenerationId = Guid.NewGuid() }),
                policies, new AgentPermissionEvaluator(policies), audit,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
                principalAuthority: new TestPrincipalAuthority(),
                sessionTools: new AgentSessionToolPorts(new AgentMcpSessionRegistry())
                {
                    NativeChatTurnScopes = nativeScopes,
                    WorkspaceContext = workspaceSource,
                    ProposalSink = store
                },
                copilotExposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));
            Assert.That(registry.FindInProcessDescriptor(providerId, AgentToolRegistry.ProposeFileEditToolName), Is.Not.Null);

            // Use only the official CLI's allowlisted auth status and model list. No credential files are read.
            string modelId;
            await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
            {
                await accountClient.StartAsync(CancellationToken.None);
                var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
                Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                    Is.True, "A inferência foi interrompida antes de iniciar: a CLI não confirmou AuthStatus user nesta sessão.");
                modelId = (await accountClient.ListModelsAsync(CancellationToken.None))
                    .Select(static model => model.Id)
                    .FirstOrDefault(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                    ?? throw new AssertionException("Nenhum modelo Copilot elegível foi retornado.");
            }

            await using var sessionStore = new CopilotVolatileSessionFsStore();
            var binding = new TrustedTestBinding(principal);
            var provider = new OfficialSessionProvider(registry, sessionStore);
            await using var runtime = new AgentRuntime([provider], toolRegistry: registry,
                toolBindings: binding, principalAuthority: new TestPrincipalAuthority(), nativeChatTurnScopes: nativeScopes);
            string? providerSessionId = null;
            var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(provider.ProviderId, modelId, syntheticDirectory.Path)
            {
                PersistProviderSession = false,
                ProviderSessionObserver = update =>
                {
                    if (update.Change == AgentProviderSessionChange.Established) providerSessionId = update.ProviderSessionId;
                }
            }, CancellationToken.None);
            var conversationId = Guid.NewGuid();
            try
            {
                using var turnTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var request = new AgentTurnRequest(AgentTurnId.New(),
                    "The attached active editor buffer is synthetic test data. Change only `.limit(10)` to `.limit(5)` by calling propose_file_edit exactly once with target `active_buffer` and no path. Do not call any other tool and do not claim the edit was applied; after the tool returns, state that a review proposal was created.",
                    tabId, activeBuffer.DocumentVersion ?? 0)
                {
                    Plan = plan,
                    Permissions = permissions,
                    WorkspaceContext = activeBuffer,
                    ConversationId = conversationId,
                    Attachments = [new AgentContextAttachment(AgentAttachmentKind.ActiveFile, activeBuffer.ActiveFileName!, null,
                        original, System.Text.Encoding.UTF8.GetByteCount(original),
                        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(original))))]
                };
                var events = new List<AgentEvent>();
                await foreach (var item in runtime.RunTurnAsync(sessionId, request, turnTimeout.Token)) events.Add(item);
                var proposals = store.ForConversation(conversationId);
                Assert.That(proposals, Has.Count.EqualTo(1), string.Join(" | ", events.Select(item => $"{item.Kind}:{item.ErrorCode}")));
                Assert.Multiple(() =>
                {
                    Assert.That(proposals[0].Status, Is.EqualTo(AgentEditProposalStatus.Registered));
                    Assert.That(proposals[0].Proposal.TargetPath, Is.Null);
                    Assert.That(proposals[0].Proposal.TabId, Is.EqualTo(tabId));
                    Assert.That(proposals[0].Proposal.OriginalText, Is.EqualTo(original));
                    Assert.That(proposals[0].Proposal.ProposedText, Is.EqualTo("db.syntheticItems.find({}).limit(5);\n"));
                    Assert.That(originTab.Text, Is.EqualTo(original), "Registering a proposal cannot apply it to the active editor.");
                    Assert.That(binding.Resolutions, Is.GreaterThanOrEqualTo(1));
                    Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolCompleted &&
                        item.ToolName == AgentToolRegistry.ProposeFileEditToolName && item.ToolStatus == AgentToolResultStatus.Succeeded), Is.True);
                    Assert.That(events.Any(item => item.Kind == AgentEventKind.TaskCompleted), Is.True);
                });
                TestContext.Progress.WriteLine("Modelo Copilot oficial produziu proposta no store Desktop associado à aba sintética; buffer permaneceu inalterado e nenhum MongoDB foi acessado.");
            }
            finally
            {
                await runtime.CloseSessionAsync(sessionId, CancellationToken.None);
                if (providerSessionId is { } nativeId)
                    await runtime.DeleteProviderSessionAsync(provider.ProviderId, nativeId, CancellationToken.None);
            }
            return true;
        }, CancellationToken.None);
    }

    private sealed class OfficialSessionProvider(IAgentToolRegistry registry, CopilotVolatileSessionFsStore store)
        : IAgentProvider, IAgentProviderSessionCleanup
    {
        private string? _workingDirectory;
        public string ProviderId => CopilotSubscriptionAgentProvider.Id;

        public Task<IAgentSession> CreateSessionAsync(AgentSessionOptions options, CancellationToken cancellationToken)
        {
            _workingDirectory = options.WorkingDirectory;
            var sessionFs = CopilotVolatileSessionFsStore.CreateConfiguration(
                CopilotRuntimeSettings.ResolveWorkingDirectory(options.WorkingDirectory));
            var client = new CopilotClient(CopilotRuntimeSettings.SessionClientOptions(options.WorkingDirectory, sessionFs));
            return Task.FromResult<IAgentSession>(new CopilotSubscriptionAgentSession(registry, options, client, store));
        }

        public async Task DeleteProviderSessionAsync(string providerSessionId, CancellationToken cancellationToken)
        {
            var sessionFs = CopilotVolatileSessionFsStore.CreateConfiguration(_workingDirectory ?? throw new InvalidOperationException("No session created."));
            await using var client = new CopilotClient(CopilotRuntimeSettings.SessionClientOptions(
                _workingDirectory, sessionFs));
            await client.StartAsync(cancellationToken);
            if (await client.GetSessionMetadataAsync(providerSessionId, cancellationToken) is not null)
                await client.DeleteSessionAsync(providerSessionId, cancellationToken);
            await store.DeleteSessionAsync(providerSessionId, cancellationToken);
        }
    }

    private sealed class SyntheticWorkspaceToolRegistry : IAgentToolRegistry
    {
        private static readonly AgentToolDescriptor Descriptor = new(ToolName, 1, AgentToolRisk.ReadOnly,
            [AgentPermission.ReadMetadata]);
        public int Invocations { get; private set; }
        public AgentPrincipal? LastPrincipal { get; private set; }
        public AgentOutputDestination? LastDestination { get; private set; }
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [Descriptor];
        public AgentToolDescriptor? FindDescriptor(string? name) => name == ToolName ? Descriptor : null;
        public string? GetInputSchemaJson(string? name) => name == ToolName
            ? "{\"type\":\"object\",\"properties\":{\"scope\":{\"type\":\"string\"}},\"required\":[\"scope\"]}" : null;
        public string? GetOutputSchemaJson(string? name) => name == ToolName
            ? "{\"type\":\"object\",\"properties\":{\"context\":{\"type\":\"string\"}}}" : null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal,
            AgentInvocationContext? invocationContext, AgentOutputDestination? destination,
            AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default)
        {
            Assert.That(name, Is.EqualTo(ToolName));
            Assert.That(argumentsJson, Does.Contain("active"));
            Invocations++;
            LastPrincipal = principal;
            LastDestination = destination;
            return Task.FromResult(AgentToolInvocationResult.Success(ToolPayload));
        }
    }

    private sealed class TrustedTestBinding(AgentPrincipal principal,
        AgentOutputDataScope outputDataScope = AgentOutputDataScope.Metadata) : IAgentToolBindingProvider
    {
        public int Resolutions { get; private set; }
        public Task<AgentToolBinding?> ResolveAsync(AgentSessionId sessionId, AgentTurnId turnId,
            string providerId, string toolName, CancellationToken cancellationToken)
        {
            Resolutions++;
            return Task.FromResult<AgentToolBinding?>(new AgentToolBinding(principal,
                AgentOutputDestination.ProviderExternal(providerId), outputDataScope));
        }
    }

    private sealed class TestPrincipalAuthority : IAgentPrincipalAuthority
    {
        public Task<bool> IsCurrentAsync(AgentPrincipal principal, CancellationToken cancellationToken = default) =>
            Task.FromResult(true);
        public Task<AgentPrincipalIssueResult> IssueInternalAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Guid> GetInternalPrincipalIdAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentChannelEnrollmentResult> EnrollExternalChannelAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<AgentPrincipalIssueResult> AuthenticateExternalAsync(Guid channelId, string proof,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentChannelRevocationStatus> RevokeExternalChannelAsync(Guid channelId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> RecoverPendingChannelsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class SingleProfileRepository(ConnectionProfile profile) : IConnectionProfileRepository
    {
        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ConnectionProfile>>([profile]);
        public Task SaveAsync(ConnectionProfile item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SinglePolicyProvider : IAgentAuthorizationPolicyProvider
    {
        private readonly Dictionary<Guid, AgentAuthorizationPolicySnapshot> _snapshots = [];
        public void Set(Guid principalId, long revision, IEnumerable<AgentPermissionGrant> grants) =>
            _snapshots[principalId] = AgentAuthorizationPolicySnapshot.Load(principalId,
                AgentAuthorizationPolicySnapshot.CurrentSchemaVersion, revision, grants);
        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken) =>
            Task.FromResult(_snapshots.GetValueOrDefault(principalId));
    }

    private sealed class SyntheticAudit : IAgentAuditRepository
    {
        public List<AgentAuditEvent> Events { get; } = [];
        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default)
        {
            Events.Add(entry.Validate());
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentAuditEvent>>(Events.TakeLast(maximum).ToArray());
        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentAuditEvent>>(Events.Where(item => item.Outcome == AgentAuditOutcome.Intent).ToArray());
    }

    private sealed class SyntheticFindSource(string document) : IAgentMongoFindSource
    {
        public int Calls { get; private set; }
        public ConnectionProfile? LastProfile { get; private set; }
        public AgentMongoFindQuery? LastQuery { get; private set; }
        public AgentMongoFindByIdQuery? LastByIdQuery { get; private set; }
        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastProfile = profile;
            LastQuery = query;
            return Task.FromResult(new AgentMongoFindPage([document], false, false, true, false));
        }
        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastProfile = profile;
            LastByIdQuery = query;
            return Task.FromResult(new AgentMongoFindPage([document], false, false, true, false));
        }
    }

    private sealed class ApproveOncePrompt : IAgentToolConfirmationPrompt
    {
        public List<AgentToolConfirmationRequest> Requests { get; } = [];
        public Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(request);
            return Task.FromResult(AgentToolConfirmationDecision.ApprovedOnce);
        }
    }
}
#pragma warning restore GHCP001
