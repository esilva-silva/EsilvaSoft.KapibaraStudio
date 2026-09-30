using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Lote 2 gates: closed exposure, versioned schemas, channel binding, ingress equivalence and ledger.</summary>
[TestFixture]
public sealed class AgentToolRegistryGateTests
{
    private static readonly Guid InternalPrincipalId = Guid.Parse("0f2f7c0e-5a55-4c0b-9df4-7cb1e3a8e001");
    private static readonly Guid ExternalPrincipalId = Guid.Parse("0f2f7c0e-5a55-4c0b-9df4-7cb1e3a8e002");
    private static readonly Guid SessionId = Guid.Parse("9d1d9d7a-2b35-4e47-8f71-08a1a7e2a101");
    private static readonly Guid TurnId = Guid.Parse("9d1d9d7a-2b35-4e47-8f71-08a1a7e2a102");
    private static readonly Guid McpClientId = Guid.Parse("9d1d9d7a-2b35-4e47-8f71-08a1a7e2a103");
    private const string DocumentEjson =
        "{\"_id\":{\"$binary\":{\"base64\":\"ABEiM0RVZneImaq7zN3u/w==\",\"subType\":\"04\"}},\"big\":{\"$numberLong\":\"9007199254740993\"}}";

    private static readonly AgentAuditOutcome[] IntentThenDenied = [AgentAuditOutcome.Intent, AgentAuditOutcome.Denied];
    private static readonly string[] OnlyListConnections = ["list_connections"];
    private static readonly string[] ChatThenMcpIdentifiers = ["openai", "openai", "claude-code", "claude-code"];
    private static readonly (AgentAuditOutcome, AgentAuditChannel)[] TwoExternalCalls =
    [
        (AgentAuditOutcome.Intent, AgentAuditChannel.ProviderExternal),
        (AgentAuditOutcome.Succeeded, AgentAuditChannel.ProviderExternal),
        (AgentAuditOutcome.Intent, AgentAuditChannel.McpExternal),
        (AgentAuditOutcome.Succeeded, AgentAuditChannel.McpExternal)
    ];

    private static readonly string[] AllReadTools =
    [
        "list_connections", "list_databases", "list_collections", "get_collection_schema", "mongo_find",
        "mongo_count", "sample_documents", "mongo_find_one", "get_document", "mongo_distinct", "get_indexes",
        "mongo_explain"
    ];

    [Test]
    public async Task RegistryIsClosedByDefaultAndUnreleasedToolsTouchNothing()
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        var audit = new MemoryAudit();
        var find = new CountingFind();
        var registry = new AgentToolRegistry(profiles, policies, new AgentPermissionEvaluator(policies), audit,
            metadata: new NoMetadata(), find: find);

        var result = await registry.InvokeAsync(Internal(1), Context(), AgentOutputDestination.Local(),
            AgentOutputDataScope.Metadata, AgentToolRegistry.ListConnectionsToolName, "{}");

        Assert.Multiple(() =>
        {
            Assert.That(registry.ExposureStage, Is.EqualTo(AgentToolExposureStage.None));
            Assert.That(registry.GetDescriptors(), Is.Empty);
            Assert.That(AllReadTools.Select(registry.FindDescriptor), Is.All.Null);
            Assert.That(AllReadTools.Select(registry.GetInputSchemaJson), Is.All.Null);
            Assert.That(AllReadTools.Select(registry.GetOutputSchemaJson), Is.All.Null);
            Assert.That(result.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(profiles.Calls + policies.Calls + audit.Events.Count + find.Calls, Is.Zero);
        });
    }

    // ADR-056: get_indexes (allowlisted index metadata) belongs to the Metadata stage.
    [TestCase(AgentToolExposureStage.Metadata, new[] { "list_connections", "list_databases", "list_collections", "get_indexes" })]
    [TestCase(AgentToolExposureStage.LiteralQueries,
        new[] { "list_connections", "list_databases", "list_collections", "get_indexes", "mongo_find", "mongo_count" })]
    public async Task ExposureFollowsPlanOrderAndDeniesLaterStagesBeforeAnySource(
        AgentToolExposureStage stage, string[] expected)
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        var audit = new MemoryAudit();
        var find = new CountingFind();
        var registry = FullRegistry(profiles, policies, audit, find, AgentToolExposure.Through(stage));

        var later = await registry.InvokeAsync(Internal(1), Context(), AgentOutputDestination.Local(),
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindOneToolName,
            FindArguments(profile, "{}", includeLimit: false));

        Assert.Multiple(() =>
        {
            Assert.That(registry.GetDescriptors().Select(item => item.Name), Is.EquivalentTo(expected));
            Assert.That(later.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(profiles.Calls + policies.Calls + audit.Events.Count + find.Calls, Is.Zero);
        });
    }

    [Test]
    public void ToolsWithoutComposedHandlersAreNotDiscoverableEvenWhenReleased()
    {
        var policies = new MapPolicyProvider();
        var registry = new AgentToolRegistry(new CountingProfiles(), policies, new AgentPermissionEvaluator(policies),
            new MemoryAudit(), exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
            principalAuthority: new TestAgentPrincipalAuthority());

        Assert.That(registry.GetDescriptors().Select(item => item.Name), Is.EqualTo(OnlyListConnections));
        Assert.That(AgentToolExposure.StageOf("run_command"), Is.Null);
        Assert.That(AgentToolExposure.Through(AgentToolExposureStage.DerivedReads).Exposes("run_command"), Is.False);
    }

    [Test]
    public void EveryReleasedSchemaIsVersionedAndClosedAtEveryObjectLevel()
    {
        var registry = FullRegistry(new CountingProfiles(), new MapPolicyProvider(), new MemoryAudit(), new CountingFind(),
            AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));
        var descriptors = registry.GetDescriptors();

        Assert.That(descriptors.Select(item => item.Name), Is.EquivalentTo(AllReadTools));
        foreach (var descriptor in descriptors)
        {
            foreach (var (kind, json) in new[]
                     { ("input", registry.GetInputSchemaJson(descriptor.Name)),
                       ("output", registry.GetOutputSchemaJson(descriptor.Name)) })
            {
                Assert.That(json, Is.Not.Null, descriptor.Name);
                using var schema = JsonDocument.Parse(json!);
                var root = schema.RootElement;
                Assert.Multiple(() =>
                {
                    Assert.That(root.GetProperty("$id").GetString(), Is.EqualTo(
                        $"urn:esilvasoft:kapibarastudio:agent-tool:{descriptor.Name}:v{descriptor.Version}:{kind}"));
                    Assert.That(root.GetProperty("type").GetString(), Is.EqualTo("object"), descriptor.Name);
                    Assert.That(OpenObjectPaths(root, "$"), Is.Empty, $"{descriptor.Name} {kind}");
                });
            }
            Assert.That(descriptor.Risk, Is.EqualTo(AgentToolRisk.ReadOnly), descriptor.Name);
        }
    }

    [TestCase(AgentPrincipalOrigin.External, false, true)]
    [TestCase(AgentPrincipalOrigin.External, true, false)]
    [TestCase(AgentPrincipalOrigin.Internal, true, true)]
    public async Task PrincipalOriginMustMatchAuthenticatedChannelBeforeAnyAccess(
        AgentPrincipalOrigin origin, bool withClient, bool externalDestination)
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        var audit = new MemoryAudit();
        var find = new CountingFind();
        var registry = FullRegistry(profiles, policies, audit, find,
            AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));
        var principalId = origin == AgentPrincipalOrigin.Internal ? InternalPrincipalId : ExternalPrincipalId;
        var destination = externalDestination
            ? AgentOutputDestination.McpExternal("claude-code")
            : AgentOutputDestination.Local();
        policies.Set(principalId, 3, FindGrants(principalId, profile, destination));

        var result = await registry.InvokeAsync(new AgentPrincipal(principalId, origin, 3),
            new AgentInvocationContext(externalDestination ? "claude-code" : null, withClient ? McpClientId : null,
                SessionId, TurnId), destination, AgentOutputDataScope.DocumentValues,
            AgentToolRegistry.MongoFindToolName, FindArguments(profile, "{}"));

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(profiles.Calls + policies.Calls + audit.Events.Count + find.Calls, Is.Zero);
    }

    /// <summary>An MCP client cannot be audited as a chat provider, and the runtime cannot reach the MCP channel.</summary>
    [TestCase(AgentPrincipalOrigin.External, true, false)]
    [TestCase(AgentPrincipalOrigin.Internal, false, true)]
    public async Task McpAndProviderChannelsCannotBeSwapped(AgentPrincipalOrigin origin, bool withClient, bool mcpDestination)
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        var audit = new MemoryAudit();
        var find = new CountingFind();
        var registry = FullRegistry(profiles, policies, audit, find,
            AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));
        var principalId = origin == AgentPrincipalOrigin.Internal ? InternalPrincipalId : ExternalPrincipalId;
        var destination = mcpDestination
            ? AgentOutputDestination.McpExternal("claude-code")
            : AgentOutputDestination.ProviderExternal("claude-code");
        policies.Set(principalId, 3, FindGrants(principalId, profile, destination));

        var result = await registry.InvokeAsync(new AgentPrincipal(principalId, origin, 3),
            new AgentInvocationContext("claude-code", withClient ? McpClientId : null, SessionId, TurnId), destination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(profile, "{}"));

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(profiles.Calls + policies.Calls + audit.Events.Count + find.Calls, Is.Zero);
    }

    [Test]
    public async Task InternalChatAndExternalMcpShareHandlersPolicyAndOutput()
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        var audit = new MemoryAudit();
        var find = new CountingFind { Documents = [DocumentEjson] };
        var registry = FullRegistry(profiles, policies, audit, find,
            AgentToolExposure.Through(AgentToolExposureStage.LiteralQueries));
        var chatDestination = AgentOutputDestination.ProviderExternal("openai");
        var mcpDestination = AgentOutputDestination.McpExternal("claude-code");
        policies.Set(InternalPrincipalId, 5, FindGrants(InternalPrincipalId, profile, chatDestination));
        policies.Set(ExternalPrincipalId, 8, FindGrants(ExternalPrincipalId, profile, mcpDestination));
        const string filter = "{\"big\":{\"$numberLong\":\"9007199254740993\"}}";

        var chat = await registry.InvokeAsync(Internal(5), new AgentInvocationContext("openai", null, SessionId, TurnId),
            chatDestination, AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName,
            FindArguments(profile, filter));
        var chatQuery = find.LastQuery;
        var mcp = await registry.InvokeAsync(External(8),
            new AgentInvocationContext("claude-code", McpClientId, SessionId, TurnId), mcpDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(profile, filter));

        Assert.Multiple(() =>
        {
            Assert.That(chat.Succeeded && mcp.Succeeded, Is.True, $"{chat.ErrorCode} / {mcp.ErrorCode}");
            Assert.That(mcp.StructuredContentJson, Is.EqualTo(chat.StructuredContentJson));
            using var output = JsonDocument.Parse(mcp.StructuredContentJson!);
            Assert.That(output.RootElement.GetProperty("documentsEjson")[0].GetString(), Is.EqualTo(DocumentEjson));
            Assert.That(find.LastQuery, Is.EqualTo(chatQuery));
            Assert.That(find.Calls, Is.EqualTo(2));
            Assert.That(audit.Events.Select(item => (item.Outcome, item.Channel)), Is.EqualTo(TwoExternalCalls));
            Assert.That(audit.Events.Select(item => item.ExternalIdentifier),
                Is.EqualTo(ChatThenMcpIdentifiers));
        });
    }

    [Test]
    public async Task CopilotDocumentReadUsesItsSeparateExposureAndRequiresBothTurnOptIns()
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        var audit = new MemoryAudit();
        var find = new CountingFind { Documents = [DocumentEjson] };
        var turns = new AgentNativeChatTurnScopeRegistry();
        var destination = AgentOutputDestination.ProviderExternal(AgentProviderIds.GitHubCopilotSubscription);
        var permissions = AgentProviderPermissions.Default(AgentProviderIds.GitHubCopilotSubscription) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            EnabledReadTools = [AgentToolRegistry.MongoFindToolName],
            ConnectionScope = AgentConnectionScope.Selected,
            SelectedConnectionIds = [profile.Id],
            DataSending = new AgentDataSendingPermissions { MongoDocuments = true }
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions,
            new AgentPlatformFacts(false, true, false));
        Assert.That(plan.ProductTools, Does.Contain(AgentToolRegistry.MongoFindToolName));
        Assert.That(turns.Register(new AgentNativeChatTurnScope(SessionId, TurnId,
            AgentProviderIds.GitHubCopilotSubscription, plan, permissions, null)), Is.True);
        policies.Set(InternalPrincipalId, 5, [.. new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(InternalPrincipalId,
                AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value, permission,
                AgentNamespaceScope.ForCollection(profile.Id, "app", "items"), destination,
                AgentOutputDataScope.DocumentValues))]);
        var registry = new AgentToolRegistry(profiles, policies, new AgentPermissionEvaluator(policies), audit,
            metadata: new NoMetadata(), schemaSamplingConsent: new DenySchemaConsent(), find: find,
            count: new CountingCount(), distinct: new CountingDistinct(), indexes: new NoIndexes(), explain: new NoExplain(), exposure: AgentToolExposure.None,
            principalAuthority: new TestAgentPrincipalAuthority(),
            sessionTools: new AgentSessionToolPorts(new AgentMcpSessionRegistry())
            {
                NativeChatTurnScopes = turns
            }, copilotExposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));

        var copilotDocumentTools = AgentProductToolNames.ReadTools.Where(AgentProductToolNames.IsCopilotDocumentRead).ToArray();
        Assert.That(copilotDocumentTools.All(name => registry.FindInProcessDescriptor(
            AgentProviderIds.GitHubCopilotSubscription, name) is not null), Is.True,
            "A etapa Copilot DerivedReads anuncia cada leitura opt-in com handler disponível.");
        Assert.That(registry.FindInProcessDescriptor(AgentProviderIds.GitHubCopilotSubscription,
            AgentToolRegistry.GetCollectionSchemaToolName), Is.Null,
            "A amostragem ao vivo não deve ser declarada ao Copilot sem UI de consentimento local dedicada.");
        Assert.That(copilotDocumentTools.All(name => registry.FindDescriptor(name) is null), Is.True,
            "A exposição Copilot DerivedReads não altera o registry MCP/externo.");

        var result = await registry.InvokeAsync(Internal(5),
            new AgentInvocationContext(AgentProviderIds.GitHubCopilotSubscription, null, SessionId, TurnId),
            destination, AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName,
            FindArguments(profile, "{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.ErrorCode);
            Assert.That(find.Calls, Is.EqualTo(1));
            Assert.That(audit.Events.Last().Outcome, Is.EqualTo(AgentAuditOutcome.Succeeded));
            Assert.That(registry.FindDescriptor(AgentToolRegistry.MongoFindToolName), Is.Null,
                "O limite de Copilot não altera o catálogo geral/externo.");
        });
    }

    [Test]
    public async Task CopilotPlannedDocumentReadRequiresOneCallHumanApprovalAndAuditsItBeforeDispatch()
    {
        var rig = CopilotReadRig(AgentConfirmationCategories.MongoDocumentRead,
            new RecordingConfirmation(AgentToolConfirmationDecision.ApprovedOnce));
        var result = await rig.Registry.InvokeAsync(Internal(5), rig.Context, rig.Destination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(rig.Profile, "{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.ErrorCode);
            Assert.That(rig.Find.Calls, Is.EqualTo(1));
            Assert.That(rig.Confirmation!.Requests.Single().Category, Is.EqualTo(AgentConfirmationCategories.MongoDocumentRead));
            Assert.That(rig.Audit.Events.Select(item => item.Outcome), Is.EqualTo(new[]
                { AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded, AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded }));
            Assert.That(rig.Audit.Events[1].ApprovalState, Is.EqualTo(AgentAuditApprovalState.ApprovedOnce));
            Assert.That(rig.Audit.Events[1].ToolName, Is.EqualTo(AgentToolRegistry.MongoFindToolName));
        });
    }

    [TestCase(AgentToolConfirmationDecision.Rejected)]
    [TestCase(AgentToolConfirmationDecision.ApprovedThisSession)]
    public async Task CopilotReadDenialOrSessionDecisionNeverDispatches(AgentToolConfirmationDecision decision)
    {
        var rig = CopilotReadRig(AgentConfirmationCategories.MongoDocumentRead, new RecordingConfirmation(decision));
        var result = await rig.Registry.InvokeAsync(Internal(5), rig.Context, rig.Destination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(rig.Profile, "{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("ConfirmationRejected"),
                "A declined prompt or unsupported session-wide choice is a confirmation outcome, not a missing permission.");
            Assert.That(rig.Find.Calls, Is.Zero);
            Assert.That(rig.Confirmation!.Requests, Has.Count.EqualTo(1));
            Assert.That(rig.Audit.Events.Select(item => item.Outcome), Is.EqualTo(new[]
                { AgentAuditOutcome.Intent, AgentAuditOutcome.Denied }));
        });
    }

    [Test]
    public async Task CopilotConfirmationFailsClosedWithoutPromptAndDoesNotDispatch()
    {
        var rig = CopilotReadRig(AgentConfirmationCategories.MongoDocumentRead, null);
        var result = await rig.Registry.InvokeAsync(Internal(5), rig.Context, rig.Destination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(rig.Profile, "{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("ConfirmationUnavailable"));
            Assert.That(rig.Find.Calls, Is.Zero);
            Assert.That(rig.Audit.Events.Select(item => item.Outcome), Is.EqualTo(new[]
                { AgentAuditOutcome.Intent, AgentAuditOutcome.Denied }));
        });
    }

    [Test]
    public async Task CopilotConfirmationTimeoutHasItsOwnOutcomeAndDoesNotDispatch()
    {
        var rig = CopilotReadRig(AgentConfirmationCategories.MongoDocumentRead, new DelayedConfirmation(),
            approvalTimeout: TimeSpan.FromMilliseconds(20));
        var result = await rig.Registry.InvokeAsync(Internal(5), rig.Context, rig.Destination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(rig.Profile, "{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("ConfirmationExpired"));
            Assert.That(rig.Find.Calls, Is.Zero);
            Assert.That(rig.Audit.Events.Last().DecisionReason, Is.EqualTo(AgentAuditDecisionReason.ApprovalExpired));
        });
    }

    [Test]
    public async Task CopilotConfirmationFailsClosedWhenAuditCannotRecordIntent()
    {
        var prompt = new RecordingConfirmation(AgentToolConfirmationDecision.ApprovedOnce);
        var rig = CopilotReadRig(AgentConfirmationCategories.MongoDocumentRead, prompt, new UnavailableAudit());
        var result = await rig.Registry.InvokeAsync(Internal(5), rig.Context, rig.Destination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(rig.Profile, "{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.ErrorCode, Is.EqualTo("ConfirmationUnavailable"));
            Assert.That(rig.Find.Calls, Is.Zero);
            Assert.That(prompt.Requests, Is.Empty, "The human is not prompted when the durable intent cannot be recorded.");
        });
    }

    [Test]
    public async Task CopilotReadOutsideConfirmationPlanDoesNotPromptAndUnplannedToolIsUnknown()
    {
        var rig = CopilotReadRig(AgentConfirmationCategories.None, new RecordingConfirmation(AgentToolConfirmationDecision.Rejected));
        var allowedWithoutPrompt = await rig.Registry.InvokeAsync(Internal(5), rig.Context, rig.Destination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(rig.Profile, "{}"));
        var unplanned = await rig.Registry.InvokeAsync(Internal(5), rig.Context, rig.Destination,
            AgentToolOutputScopes.For(AgentToolRegistry.MongoCountToolName), AgentToolRegistry.MongoCountToolName,
            JsonSerializer.Serialize(new { connectionId = rig.Profile.Id, database = "app", collection = "items" }));
        var otherProviderDestination = AgentOutputDestination.ProviderExternal("openai");
        var otherProvider = await rig.Registry.InvokeAsync(Internal(5),
            new AgentInvocationContext("openai", null, SessionId, TurnId), otherProviderDestination,
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(rig.Profile, "{}"));

        Assert.Multiple(() =>
        {
            Assert.That(allowedWithoutPrompt.Succeeded, Is.True, allowedWithoutPrompt.ErrorCode);
            Assert.That(unplanned.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(otherProvider.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(rig.Confirmation!.Requests, Is.Empty);
            Assert.That(rig.Find.Calls, Is.EqualTo(1));
        });
    }

    private static CopilotReadScenario CopilotReadRig(AgentConfirmationCategories confirmations,
        IAgentToolConfirmationPrompt? prompt, IAgentAuditRepository? auditOverride = null, TimeSpan? approvalTimeout = null)
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        var audit = new MemoryAudit();
        var find = new CountingFind { Documents = [DocumentEjson] };
        var turns = new AgentNativeChatTurnScopeRegistry();
        var destination = AgentOutputDestination.ProviderExternal(AgentProviderIds.GitHubCopilotSubscription);
        var permissions = AgentProviderPermissions.Default(AgentProviderIds.GitHubCopilotSubscription) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            EnabledReadTools = [AgentToolRegistry.MongoFindToolName],
            ConnectionScope = AgentConnectionScope.Selected,
            SelectedConnectionIds = [profile.Id],
            DataSending = new AgentDataSendingPermissions { MongoDocuments = true },
            ConfirmationCategories = confirmations
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new AgentPlatformFacts(false, true, false));
        turns.Register(new AgentNativeChatTurnScope(SessionId, TurnId, AgentProviderIds.GitHubCopilotSubscription,
            plan, permissions, null) { ConversationId = Guid.NewGuid() });
        policies.Set(InternalPrincipalId, 5, [.. new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }
            .Select(permission => new AgentPermissionGrant(InternalPrincipalId,
                AgentInvocationScope.ForTurn(SessionId, TurnId), profile.SourceGenerationId!.Value, permission,
                AgentNamespaceScope.ForCollection(profile.Id, "app", "items"), destination,
                AgentOutputDataScope.DocumentValues))]);
        var registry = new AgentToolRegistry(profiles, policies, new AgentPermissionEvaluator(policies), auditOverride ?? audit,
            metadata: new NoMetadata(), schemaSamplingConsent: new DenySchemaConsent(), find: find,
            count: new CountingCount(), distinct: new CountingDistinct(), indexes: new NoIndexes(), explain: new NoExplain(),
            exposure: AgentToolExposure.None, principalAuthority: new TestAgentPrincipalAuthority(),
            sessionTools: new AgentSessionToolPorts(new AgentMcpSessionRegistry())
            {
                NativeChatTurnScopes = turns,
                ConfirmationPrompt = prompt,
                ApprovalTimeout = approvalTimeout ?? TimeSpan.FromSeconds(45)
            }, copilotExposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));
        var context = new AgentInvocationContext(AgentProviderIds.GitHubCopilotSubscription, null, SessionId, TurnId);
        return new CopilotReadScenario(profile, destination, context, registry, find, audit, prompt as RecordingConfirmation);
    }

    private sealed record CopilotReadScenario(ConnectionProfile Profile, AgentOutputDestination Destination,
        AgentInvocationContext Context, AgentToolRegistry Registry, CountingFind Find, MemoryAudit Audit,
        RecordingConfirmation? Confirmation);

    private sealed class RecordingConfirmation(AgentToolConfirmationDecision decision) : IAgentToolConfirmationPrompt
    {
        public List<AgentToolConfirmationRequest> Requests { get; } = [];
        public Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(decision);
        }
    }

    private sealed class DelayedConfirmation : IAgentToolConfirmationPrompt
    {
        public async Task<AgentToolConfirmationDecision> ConfirmAsync(AgentToolConfirmationRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return AgentToolConfirmationDecision.ApprovedOnce;
        }
    }

    [Test]
    public async Task ConnectedProviderDoesNotConsentToSendingDataWithoutDestinationGrant()
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        var audit = new MemoryAudit();
        var find = new CountingFind { Documents = [DocumentEjson] };
        var registry = FullRegistry(profiles, policies, audit, find,
            AgentToolExposure.Through(AgentToolExposureStage.LiteralQueries));
        // The user granted local reads only. An authenticated provider session is not a data-egress grant.
        policies.Set(InternalPrincipalId, 2, FindGrants(InternalPrincipalId, profile, AgentOutputDestination.Local()));

        var external = await registry.InvokeAsync(Internal(2),
            new AgentInvocationContext("openai", null, SessionId, TurnId),
            AgentOutputDestination.ProviderExternal("openai"), AgentOutputDataScope.DocumentValues,
            AgentToolRegistry.MongoFindToolName, FindArguments(profile, "{}"));
        var wrongScope = await registry.InvokeAsync(Internal(2), Context(), AgentOutputDestination.Local(),
            AgentOutputDataScope.Schema, AgentToolRegistry.MongoFindToolName, FindArguments(profile, "{}"));
        var local = await registry.InvokeAsync(Internal(2), Context(), AgentOutputDestination.Local(),
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(profile, "{}"));

        Assert.Multiple(() =>
        {
            Assert.That(external.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(wrongScope.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(local.Succeeded, Is.True, local.ErrorCode);
            Assert.That(find.Calls, Is.EqualTo(1));
            Assert.That(audit.Events[1].Outcome, Is.EqualTo(AgentAuditOutcome.Denied));
            Assert.That(audit.Events[1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.PermissionMissing));
        });
    }

    [TestCase("{\"$where\":\"sleep(1000)\"}")]
    [TestCase("{\"$expr\":{\"$function\":{\"body\":\"return 1\",\"args\":[],\"lang\":\"js\"}}}")]
    [TestCase("{\"a\":{\"$eq\":{\"$code\":\"x\"}}}")]
    [TestCase("{\"$or\":[{\"a\":{\"$unknown\":1}}]}")]
    [TestCase("{\"$expr\":{\"$in\":[\"admin\",\"$$USER_ROLES.role\"]}}")]
    public async Task EveryFilterTakingToolUsesTheClosedCodecBeforeTheSource(string filter)
    {
        var profile = Connection();
        var policies = new MapPolicyProvider();
        var find = new CountingFind();
        var count = new CountingCount();
        var distinct = new CountingDistinct();
        var registry = new AgentToolRegistry(new CountingProfiles(profile), policies,
            new AgentPermissionEvaluator(policies), new MemoryAudit(), find: find, count: count, distinct: distinct,
            exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads),
            principalAuthority: new TestAgentPrincipalAuthority());
        var baseArguments = new Dictionary<string, object>
        {
            ["connectionId"] = profile.Id, ["database"] = "app", ["collection"] = "items", ["filterEjson"] = filter
        };

        foreach (var (tool, extra) in new (string, (string, object)?)[]
                 { (AgentToolRegistry.MongoFindToolName, null), (AgentToolRegistry.MongoCountToolName, null),
                   (AgentToolRegistry.MongoFindOneToolName, null),
                   (AgentToolRegistry.MongoDistinctToolName, ("field", "a")) })
        {
            var arguments = new Dictionary<string, object>(baseArguments);
            if (extra is { } pair) arguments[pair.Item1] = pair.Item2;
            var result = await registry.InvokeAsync(Internal(1), Context(), AgentOutputDestination.Local(),
                AgentOutputDataScope.DocumentValues, tool, JsonSerializer.Serialize(arguments));
            Assert.That(result.ErrorCode, Is.EqualTo("InvalidArguments"), tool);
        }
        Assert.That(find.Calls + count.Calls + distinct.Calls, Is.Zero);
    }

    [Test]
    public async Task DurableLedgerKeepsIntentPendingWhenOutcomeCannotBeRecordedAndNeverReplays()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slop-agent-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var repository = new LiteDbConnectionProfileRepository(Path.Combine(directory, "workspace.db"));
            IAgentAuthorizationPolicyRepository policies = repository;
            IAgentAuditRepository owner = repository;
            var profile = Connection();
            var local = AgentOutputDestination.Local();
            await policies.SaveAsync(InternalPrincipalId, FindGrants(InternalPrincipalId, profile, local), 0);
            var ledger = new TerminalFailingAudit(owner);
            var find = new CountingFind { Documents = [DocumentEjson] };
            var registry = new AgentToolRegistry(new CountingProfiles(profile), policies,
                new AgentPermissionEvaluator(policies), ledger, find: find,
                exposure: AgentToolExposure.Through(AgentToolExposureStage.LiteralQueries),
                principalAuthority: new TestAgentPrincipalAuthority());

            var suppressed = await registry.InvokeAsync(Internal(1), Context(), local,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(profile, "{}"));
            var pending = await owner.GetPendingAsync();

            ledger.FailTerminals = false;
            var next = await registry.InvokeAsync(Internal(1), Context(), local,
                AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(profile, "{}"));
            var recent = await owner.GetRecentAsync();

            Assert.Multiple(() =>
            {
                Assert.That(suppressed.Succeeded, Is.False);
                Assert.That(suppressed.StructuredContentJson, Is.Null);
                Assert.That(pending, Has.Count.EqualTo(1));
                Assert.That(pending[0].ToolName, Is.EqualTo(AgentToolRegistry.MongoFindToolName));
                Assert.That(next.Succeeded, Is.True, next.ErrorCode);
                // One read per call: the unrecorded outcome is left for reconciliation, never re-executed.
                Assert.That(find.Calls, Is.EqualTo(2));
                Assert.That(recent.Count(item => item.Outcome == AgentAuditOutcome.Intent), Is.EqualTo(2));
                Assert.That(recent.Count(item => item.Outcome == AgentAuditOutcome.Succeeded), Is.EqualTo(1));
            });
            Assert.That(await owner.GetPendingAsync(), Has.Count.EqualTo(1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public async Task DurableLedgerUnavailableDeniesBeforePolicyProfileOrSource()
    {
        var profile = Connection();
        var profiles = new CountingProfiles(profile);
        var policies = new MapPolicyProvider();
        policies.Set(InternalPrincipalId, 1, FindGrants(InternalPrincipalId, profile, AgentOutputDestination.Local()));
        var find = new CountingFind { Documents = [DocumentEjson] };
        var registry = FullRegistry(profiles, policies, new UnavailableAudit(), find,
            AgentToolExposure.Through(AgentToolExposureStage.LiteralQueries));

        var result = await registry.InvokeAsync(Internal(1), Context(), AgentOutputDestination.Local(),
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(profile, "{}"));

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(profiles.Calls + policies.Calls + find.Calls, Is.Zero);
    }

    [Test]
    public void RegistryWithoutChannelAuthorityExposesNothing()
    {
        var policies = new MapPolicyProvider();
        var registry = new AgentToolRegistry(new CountingProfiles(), policies, new AgentPermissionEvaluator(policies),
            new MemoryAudit(), find: new CountingFind(),
            exposure: AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));

        Assert.That(registry.GetDescriptors(), Is.Empty);
        Assert.That(registry.GetInputSchemaJson(AgentToolRegistry.ListConnectionsToolName), Is.Null);
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    public async Task RevokedChannelOrUnavailableAuthorityDeniesBeforeDispatchWithAuditedOutcome(
        bool authorityFails, bool revokedAfterDispatch)
    {
        var profile = Connection();
        var policies = new MapPolicyProvider();
        policies.Set(InternalPrincipalId, 1, FindGrants(InternalPrincipalId, profile, AgentOutputDestination.Local()));
        var audit = new MemoryAudit();
        var find = new CountingFind { Documents = [DocumentEjson] };
        var authority = new TestAgentPrincipalAuthority();
        if (authorityFails) authority.Failure = new IOException("authority store locked");
        else if (revokedAfterDispatch) authority.IsCurrent = _ => authority.Checks == 1;
        else authority.IsCurrent = _ => false;
        var registry = FullRegistry(new CountingProfiles(profile), policies, audit, find,
            AgentToolExposure.Through(AgentToolExposureStage.LiteralQueries), authority);

        var result = await registry.InvokeAsync(Internal(1), Context(), AgentOutputDestination.Local(),
            AgentOutputDataScope.DocumentValues, AgentToolRegistry.MongoFindToolName, FindArguments(profile, "{}"));

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            // Revocation after dispatch withholds the already-read page; it is never re-executed.
            Assert.That(find.Calls, Is.EqualTo(revokedAfterDispatch ? 1 : 0));
            Assert.That(audit.Events.Select(item => item.Outcome), Is.EqualTo(IntentThenDenied));
            Assert.That(audit.Events[1].DecisionReason, Is.EqualTo(authorityFails
                ? AgentAuditDecisionReason.PolicyUnavailable : AgentAuditDecisionReason.PermissionMissing));
        });
    }

    private static IEnumerable<string> OpenObjectPaths(JsonElement node, string path)
    {
        if (node.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in node.EnumerateArray())
                foreach (var open in OpenObjectPaths(item, $"{path}[{index++}]")) yield return open;
            yield break;
        }
        if (node.ValueKind != JsonValueKind.Object) yield break;
        var isObject = node.TryGetProperty("type", out var type) &&
            (type.ValueKind == JsonValueKind.String && type.GetString() == "object" ||
             type.ValueKind == JsonValueKind.Array && type.EnumerateArray().Any(item => item.GetString() == "object"));
        if (isObject && (!node.TryGetProperty("additionalProperties", out var additional) ||
                         additional.ValueKind != JsonValueKind.False))
            yield return path;
        foreach (var property in node.EnumerateObject())
            foreach (var open in OpenObjectPaths(property.Value, path + "." + property.Name)) yield return open;
    }

    private static AgentToolRegistry FullRegistry(IConnectionProfileRepository profiles,
        IAgentAuthorizationPolicyProvider policies, IAgentAuditRepository audit, IAgentMongoFindSource find,
        AgentToolExposure exposure, IAgentPrincipalAuthority? authority = null) =>
        new(profiles, policies, new AgentPermissionEvaluator(policies), audit, metadata: new NoMetadata(),
            schemaSamplingConsent: new DenySchemaConsent(), find: find, count: new CountingCount(),
            distinct: new CountingDistinct(), indexes: new NoIndexes(), explain: new NoExplain(), exposure: exposure,
            principalAuthority: authority ?? new TestAgentPrincipalAuthority());

    private static ConnectionProfile Connection() =>
        ConnectionProfile.Create("Gate", "mongodb://localhost:27017") with { SourceGenerationId = Guid.NewGuid() };

    private static AgentPrincipal Internal(long revision) =>
        new(InternalPrincipalId, AgentPrincipalOrigin.Internal, revision);

    private static AgentPrincipal External(long revision) =>
        new(ExternalPrincipalId, AgentPrincipalOrigin.External, revision);

    private static AgentInvocationContext Context() => new(null, null, SessionId, TurnId);

    private static AgentPermissionGrant[] FindGrants(Guid principalId, ConnectionProfile profile,
        AgentOutputDestination destination) =>
        [.. new[] { AgentPermission.ExecuteReadQueries, AgentPermission.ReadDocuments }.Select(permission =>
            new AgentPermissionGrant(principalId, AgentInvocationScope.ForSession(SessionId),
                profile.SourceGenerationId!.Value, permission,
                AgentNamespaceScope.ForCollection(profile.Id, "app", "items"), destination,
                AgentOutputDataScope.DocumentValues))];

    private static string FindArguments(ConnectionProfile profile, string filter, bool includeLimit = true) =>
        includeLimit
            ? JsonSerializer.Serialize(new { connectionId = profile.Id, database = "app", collection = "items",
                filterEjson = filter, limit = 5 })
            : JsonSerializer.Serialize(new { connectionId = profile.Id, database = "app", collection = "items",
                filterEjson = filter });

    private sealed class CountingProfiles(params ConnectionProfile[] profiles) : IConnectionProfileRepository
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<ConnectionProfile>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<IReadOnlyList<ConnectionProfile>>(profiles);
        }

        public Task SaveAsync(ConnectionProfile profile, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MapPolicyProvider : IAgentAuthorizationPolicyProvider
    {
        private readonly Dictionary<Guid, AgentAuthorizationPolicySnapshot> _policies = [];
        public int Calls { get; private set; }

        public void Set(Guid principalId, long revision, AgentPermissionGrant[] grants) =>
            _policies[principalId] = AgentAuthorizationPolicySnapshot.Load(principalId,
                AgentAuthorizationPolicySnapshot.CurrentSchemaVersion, revision, grants);

        public Task<AgentAuthorizationPolicySnapshot?> LoadAsync(Guid principalId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(_policies.GetValueOrDefault(principalId));
        }
    }

    private sealed class MemoryAudit : IAgentAuditRepository
    {
        public List<AgentAuditEvent> Events { get; } = [];

        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default)
        {
            Events.Add(entry.Validate());
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentAuditEvent>>(Events.TakeLast(maximum).ToArray());

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnavailableAudit : IAgentAuditRepository
    {
        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default) =>
            throw new IOException("ledger locked");

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => throw new IOException("ledger locked");

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => throw new IOException("ledger locked");
    }

    // Forwards to the real LiteDB ledger but fails every terminal append, simulating a crash after the read.
    private sealed class TerminalFailingAudit(IAgentAuditRepository inner) : IAgentAuditRepository
    {
        public bool FailTerminals { get; set; } = true;

        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default) =>
            FailTerminals && entry.Outcome != AgentAuditOutcome.Intent
                ? throw new IOException("terminal append lost")
                : inner.AppendAsync(entry, cancellationToken);

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => inner.GetRecentAsync(maximum, cancellationToken);

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => inner.GetPendingAsync(maximum, cancellationToken);
    }

    private sealed class CountingFind : IAgentMongoFindSource
    {
        public int Calls { get; private set; }
        public AgentMongoFindQuery? LastQuery { get; private set; }
        public IReadOnlyList<string> Documents { get; init; } = [];

        public Task<AgentMongoFindPage> FindAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastQuery = query;
            return Task.FromResult(new AgentMongoFindPage(Documents, false, false, true, false));
        }

        public Task<AgentMongoFindPage> FindByIdAsync(ConnectionProfile profile, AgentMongoFindByIdQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentMongoFindPage(Documents, false, false, true, false));
        }
    }

    private sealed class CountingCount : IAgentMongoCountSource
    {
        public int Calls { get; private set; }

        public Task<AgentMongoCountResult> CountAsync(ConnectionProfile profile, AgentMongoCountQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentMongoCountResult("{\"$numberLong\":\"0\"}", true));
        }
    }

    private sealed class CountingDistinct : IAgentMongoDistinctSource
    {
        public int Calls { get; private set; }

        public Task<AgentMongoDistinctPage> DistinctAsync(ConnectionProfile profile, AgentMongoDistinctQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new AgentMongoDistinctPage([], false, true, false));
        }
    }

    private sealed class NoIndexes : IAgentMongoIndexSource
    {
        public Task<AgentMongoIndexPage> GetIndexesAsync(ConnectionProfile profile, string database,
            string collection, TimeSpan maximumExecutionTime, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class NoExplain : IAgentMongoExplainSource
    {
        public Task<AgentMongoExplainResult> ExplainAsync(ConnectionProfile profile, AgentMongoFindQuery query,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class DenySchemaConsent : IAgentSchemaSamplingConsentProvider
    {
        public Task<bool> HasLocalConsentAsync(AgentSchemaSamplingRequest request,
            CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class NoMetadata : IMongoMetadataSource
    {
        public Task<IReadOnlyList<string>> ListDatabaseNamesAsync(ConnectionProfile profile, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BoundedMetadataResult<string>> ListDatabaseNamesBoundedAsync(ConnectionProfile profile, int maximum, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionEntry>> ListCollectionNamesAsync(ConnectionProfile profile, string database, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<BoundedMetadataResult<CollectionEntry>> ListCollectionNamesBoundedAsync(ConnectionProfile profile, string database, int maximum, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionDefinition?> GetCollectionDefinitionAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<IndexInfo>> ListIndexesAsync(ConnectionProfile profile, string database, string collection, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<SampledDocument>> SampleSchemaAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ConcreteCollectionSchemaSampleResult> SampleConcreteCollectionSchemaBoundedAsync(ConnectionProfile profile, string database, string collection, SchemaSampleOptions options, int maximumProjectedBytes, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
