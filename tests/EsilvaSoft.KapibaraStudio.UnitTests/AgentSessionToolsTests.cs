using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents.Broker;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// P7-CLP-3-01: registry of the integrated Claude Code agent. Metadata stage, per-session exposure by turn plan and
/// connections, get_cached_schema without touching MongoDB, get_workspace_context, propose_file_edit (never writes to
/// disk) and the permission-prompt tool.
/// </summary>
[TestFixture, Category("Unit")]
public sealed class AgentSessionToolsTests
{
    private static readonly string[] MetadataStageTools =
        ["list_connections", "list_databases", "list_collections", "get_indexes", "get_search_indexes"];

    private static readonly string[] OriginalHunkLines = ["linha 2"];
    private static readonly string[] ProposedHunkLines = ["linha dois", "linha 2b"];
    private static readonly string[] ConfirmationDecisions = ["Rejected", "ApprovedOnce", "ApprovedThisSession"];

    private static readonly string[] SessionTools =
        ["get_cached_schema", "get_workspace_context", "propose_file_edit", "approve", "get_query_results", "get_query_diagnostics"];

    [Test]
    public void MetadataStageReleasesMetadataAndSessionToolsButNoDocumentOrWriteTool()
    {
        using var rig = new AgentSessionToolsTestRig();

        Assert.Multiple(() =>
        {
            Assert.That(AgentToolExposure.StageOf("get_indexes"), Is.EqualTo(AgentToolExposureStage.Metadata));
            Assert.That(SessionTools.Select(AgentToolExposure.StageOf), Is.All.EqualTo(AgentToolExposureStage.Metadata));
            Assert.That(AgentToolExposure.StageOf("get_collection_schema"), Is.Null);
            // In-process providers never see per-session tools; the MCP broker does and filters per channel.
            Assert.That(rig.Registry.GetDescriptors().Select(item => item.Name), Is.EquivalentTo(MetadataStageTools));
            Assert.That(rig.Registry.GetChannelDescriptors().Select(item => item.Name),
                Is.EquivalentTo(MetadataStageTools.Concat(SessionTools)));
            Assert.That(rig.Registry.GetChannelDescriptors().Select(item => item.Risk), Is.All.EqualTo(AgentToolRisk.ReadOnly));
            Assert.That(rig.Registry.FindDescriptor("get_collection_schema"), Is.Null);
            Assert.That(rig.Registry.FindDescriptor("insert_one"), Is.Null);
            Assert.That(rig.Registry.FindDescriptor("get_indexes")!.Version, Is.EqualTo(2), "Schema de saída mudou.");
        });
    }

    [Test]
    public void SessionSchemasAreVersionedAndClosedExceptTheOpaqueApprovalInput()
    {
        using var rig = new AgentSessionToolsTestRig();
        foreach (var tool in SessionTools.Append("get_indexes"))
        {
            foreach (var (kind, json) in new[] { ("input", rig.Registry.GetInputSchemaJson(tool)), ("output", rig.Registry.GetOutputSchemaJson(tool)) })
            {
                using var schema = JsonDocument.Parse(json!);
                var open = OpenObjectPaths(schema.RootElement, "$").ToArray();
                Assert.That(schema.RootElement.GetProperty("$id").GetString(), Does.EndWith($":{kind}"), tool);
                // approve echoes the CLI's tool input untouched; it is opaque data, never executed by the registry.
                var expected = tool == "approve" ? (kind == "input" ? new[] { "$.properties.input" } : ["$.properties.updatedInput"]) : [];
                Assert.That(open, Is.EquivalentTo(expected), $"{tool} {kind}");
            }
        }
    }

    [Test]
    public async Task SessionToolsAreUnknownForPrincipalsWithoutASessionScope()
    {
        using var rig = new AgentSessionToolsTestRig();
        var internalPrincipal = new AgentPrincipal(rig.PrincipalId, AgentPrincipalOrigin.Internal, 1);
        var otherExternal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.External, 1);

        foreach (var tool in SessionTools)
        {
            var external = await rig.CallRawAsync(tool, "{}", otherExternal);
            Assert.That(external.ErrorCode, Is.EqualTo("UnknownTool"), tool);
        }
        var runtime = await rig.Registry.InvokeAsync(internalPrincipal,
            new AgentInvocationContext(null, null, Guid.NewGuid(), Guid.NewGuid()), AgentOutputDestination.Local(),
            AgentOutputDataScope.Metadata, "get_workspace_context", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(runtime.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(rig.Audit.Events, Is.Empty, "Recusa por exposição acontece antes da auditoria.");
            Assert.That(rig.Workspace.Captures, Is.Zero);
        });
    }

    [Test]
    public async Task NativeChatWorkspaceContextRequiresExactTurnPlanAndTabMetadataPermission()
    {
        using var rig = new AgentSessionToolsTestRig();
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var sessionId = Guid.NewGuid();
        var turnId = Guid.NewGuid();
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions,
            new AgentPlatformFacts(true, true));
        var snapshot = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder,
            ConnectionId: rig.Profile.Id.ToString("D"), ConnectionName: rig.Profile.Name,
            DatabaseName: "db-test", CollectionName: "collection-test");
        var scope = new AgentNativeChatTurnScope(sessionId, turnId, "claude-code", plan, rig.Permissions, snapshot);
        Assert.That(rig.NativeChatScopes.Register(scope), Is.True);

        var allowed = await InvokeNativeAsync(rig, principal, sessionId, turnId);
        var missingTurn = await InvokeNativeAsync(rig, principal, sessionId, Guid.NewGuid());
        var noToolPlan = plan with { ProductTools = [] };
        var noPlanTurn = Guid.NewGuid();
        rig.NativeChatScopes.Register(scope with { TurnId = noPlanTurn, Plan = noToolPlan });
        var noPlan = await InvokeNativeAsync(rig, principal, sessionId, noPlanTurn);
        var noTabPermissionTurn = Guid.NewGuid();
        rig.NativeChatScopes.Register(scope with
        {
            TurnId = noTabPermissionTurn,
            Permissions = rig.Permissions with { DataSending = rig.Permissions.DataSending with { TabMetadata = false } }
        });
        var noTabPermission = await InvokeNativeAsync(rig, principal, sessionId, noTabPermissionTurn);

        Assert.Multiple(() =>
        {
            Assert.That(allowed.ErrorCode, Is.Null);
            Assert.That(allowed.StructuredContentJson, Does.Contain("db-test"));
            Assert.That(missingTurn.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(noPlan.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(noTabPermission.ErrorCode, Is.EqualTo("UnknownTool"));
        });
    }

    [Test]
    public async Task CopilotWorkspaceContextRequiresConsentCheckboxTabMetadataAndExactTurnScope()
    {
        using var rig = new AgentSessionToolsTestRig();
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var provider = AgentProviderIds.GitHubCopilotSubscription;
        var permissions = rig.Permissions with
        {
            ProviderId = provider,
            Workspace = new AgentWorkspacePermissions { UseFilesFolder = true },
            DataSending = rig.Permissions.DataSending with { TabMetadata = true }
        };
        var snapshot = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder,
            ConnectionId: rig.Profile.Id.ToString("D"), ConnectionName: rig.Profile.Name,
            DatabaseName: "db-copilot", CollectionName: "collection-copilot");
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new AgentPlatformFacts(true, true))
            with { AllowedConnectionIds = [rig.Profile.Id] };

        async Task<AgentToolInvocationResult> Invoke(AgentProviderPermissions currentPermissions, AgentTurnPlan currentPlan,
            string? contextProvider = null, Guid? invocationTurn = null)
        {
            var session = Guid.NewGuid();
            var turn = Guid.NewGuid();
            rig.NativeChatScopes.Register(new AgentNativeChatTurnScope(session, turn, provider, currentPlan,
                currentPermissions, snapshot));
            return await rig.Registry.InvokeAsync(principal,
                new AgentInvocationContext(contextProvider ?? provider, null, session, invocationTurn ?? turn),
                AgentOutputDestination.ProviderExternal(contextProvider ?? provider),
                AgentToolOutputScopes.For("get_workspace_context"), "get_workspace_context", "{}");
        }

        var allowed = await Invoke(permissions, plan);
        var noCheckbox = await Invoke(permissions with
        {
            EnabledReadTools = permissions.EnabledReadTools.Where(name => name != "get_workspace_context").ToArray()
        }, plan);
        var noTabConsent = await Invoke(permissions with
        {
            DataSending = permissions.DataSending with { TabMetadata = false }
        }, plan);
        var noExternalConsent = await Invoke(permissions with { ExternalDestinationConsentAt = null },
            AgentModePolicy.Plan(AgentOperationMode.Agent, permissions with { ExternalDestinationConsentAt = null },
                new AgentPlatformFacts(true, true)));
        var wrongTurn = await Invoke(permissions, plan, invocationTurn: Guid.NewGuid());
        var wrongProvider = await Invoke(permissions, plan, contextProvider: "other-provider");

        Assert.Multiple(() =>
        {
            Assert.That(allowed.Succeeded, Is.True, allowed.ErrorCode);
            Assert.That(allowed.StructuredContentJson, Does.Contain("db-copilot"));
            Assert.That(noCheckbox.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(noTabConsent.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(noExternalConsent.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(wrongTurn.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(wrongProvider.ErrorCode, Is.EqualTo("UnknownTool"));
        });
    }

    [Test]
    public async Task NativeChatCanProposeEditToCapturedUntitledBufferWithConversationBinding()
    {
        using var rig = new AgentSessionToolsTestRig();
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var sessionId = Guid.NewGuid();
        var turnId = Guid.NewGuid();
        const string original = "db.collection.find().limit(10);\n";
        var context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, WorkspaceFolder: null,
            ActiveFileName: "Aba sem título", TabId: "tab-native", DocumentVersion: 0, BufferText: original);
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions,
            new AgentPlatformFacts(true, true));
        rig.NativeChatScopes.Register(new AgentNativeChatTurnScope(sessionId, turnId, "claude-code", plan,
            rig.Permissions, context)
        {
            ConversationId = rig.ConversationId,
            ActiveFileAttachmentResolved = true,
            ActiveFileAttachmentMatchesSnapshot = true,
        });

        var result = await rig.Registry.InvokeAsync(principal,
            new AgentInvocationContext("claude-code", null, sessionId, turnId),
            AgentOutputDestination.ProviderExternal("claude-code"),
            AgentToolOutputScopes.For("propose_file_edit"), "propose_file_edit",
            """{"target":"active_buffer","edits":[{"old_text":"limit(10)","new_text":"limit(5)"}]}""");

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        var proposal = rig.Sink.Proposals.Single();
        Assert.Multiple(() =>
        {
            Assert.That(proposal.ConversationId, Is.EqualTo(rig.ConversationId));
            Assert.That(proposal.TargetPath, Is.Null);
            Assert.That(proposal.TabId, Is.EqualTo("tab-native"));
            Assert.That(proposal.ProposedText, Does.Contain("limit(5)"));
        });
    }

    [Test]
    public async Task NativeChatActiveBufferProposalRequiresResolvedAttachmentForThatTurn()
    {
        using var rig = new AgentSessionToolsTestRig();
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        const string provider = AgentProviderIds.GitHubCopilotSubscription;
        const string original = "db.collection.find().limit(10);\n";
        var permissions = rig.Permissions with
        {
            ProviderId = provider,
            Workspace = new AgentWorkspacePermissions { UseFilesFolder = true },
            DataSending = rig.Permissions.DataSending with { WorkspaceFiles = true },
            EditProposals = rig.Permissions.EditProposals with { OtherWorkspaceFiles = true },
        };
        var context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, WorkspaceFolder: rig.WorkspaceFolder,
            ActiveFileName: "Aba sem título", TabId: "tab-copilot", DocumentVersion: 7, BufferText: original);
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions,
            new AgentPlatformFacts(HasWorkspaceFolder: false, ProductToolsAvailable: true, NativeToolsAvailable: false));
        Assert.That(plan.ProductTools, Does.Contain(AgentToolRegistry.ProposeFileEditToolName));

        var sessionId = Guid.NewGuid();
        var removedChipTurn = Guid.NewGuid();
        var reattachedChipTurn = Guid.NewGuid();
        var mismatchedAttachmentTurn = Guid.NewGuid();
        var workspaceFileTurn = Guid.NewGuid();
        rig.NativeChatScopes.Register(new AgentNativeChatTurnScope(sessionId, removedChipTurn, provider,
            plan, permissions, context)
        {
            ConversationId = rig.ConversationId,
            ActiveFileAttachmentResolved = false,
        });
        rig.NativeChatScopes.Register(new AgentNativeChatTurnScope(sessionId, reattachedChipTurn, provider,
            plan, permissions, context)
        {
            ConversationId = rig.ConversationId,
            ActiveFileAttachmentResolved = true,
            ActiveFileAttachmentMatchesSnapshot = true,
        });
        rig.NativeChatScopes.Register(new AgentNativeChatTurnScope(sessionId, mismatchedAttachmentTurn, provider,
            plan, permissions, context)
        {
            ConversationId = rig.ConversationId,
            ActiveFileAttachmentResolved = true,
            ActiveFileAttachmentMatchesSnapshot = false,
        });
        rig.NativeChatScopes.Register(new AgentNativeChatTurnScope(sessionId, workspaceFileTurn, provider,
            plan, permissions, context)
        {
            ConversationId = rig.ConversationId,
            ActiveFileAttachmentResolved = false,
        });

        async Task<AgentToolInvocationResult> Invoke(Guid turnId, string arguments) => await rig.Registry.InvokeAsync(principal,
            new AgentInvocationContext(provider, null, sessionId, turnId),
            AgentOutputDestination.ProviderExternal(provider), AgentToolOutputScopes.For("propose_file_edit"),
            "propose_file_edit", arguments);

        var activeBufferArguments = """{"target":"active_buffer","edits":[{"old_text":"limit(10)","new_text":"limit(5)"}]}""";
        var denied = await Invoke(removedChipTurn, activeBufferArguments);
        Assert.Multiple(() =>
        {
            Assert.That(denied.ErrorCode, Is.EqualTo("ActiveFileNotAttached"));
            Assert.That(rig.Sink.Proposals, Is.Empty, "A removed ActiveFile chip must not register a proposal.");
        });

        var allowed = await Invoke(reattachedChipTurn, activeBufferArguments);
        Assert.That(allowed.Succeeded, Is.True, allowed.ErrorCode);
        Assert.That(rig.Sink.Proposals, Has.Count.EqualTo(1));
        Assert.That(rig.Sink.Proposals.Single().TabId, Is.EqualTo("tab-copilot"));

        var mismatch = await Invoke(mismatchedAttachmentTurn, activeBufferArguments);
        Assert.Multiple(() =>
        {
            Assert.That(mismatch.ErrorCode, Is.EqualTo("ActiveFileSnapshotMismatch"));
            Assert.That(rig.Sink.Proposals, Has.Count.EqualTo(1), "A mismatched attachment must never register a proposal.");
        });

        const string workspaceOriginal = "const value = 1;\n";
        rig.WriteFile("query.js", workspaceOriginal);
        var workspaceProposal = await Invoke(workspaceFileTurn,
            """{"path":"query.js","edits":[{"old_text":"value = 1","new_text":"value = 2"}]}""");
        Assert.That(workspaceProposal.Succeeded, Is.True, workspaceProposal.ErrorCode,
            "Removing the active-buffer attachment does not revoke an independently permitted workspace-file proposal.");
        Assert.That(rig.Sink.Proposals, Has.Count.EqualTo(2));
        Assert.That(rig.Sink.Proposals.Last().TargetPath, Is.EqualTo(Path.Combine(rig.WorkspaceFolder, "query.js")));
    }

    [Test]
    public async Task CopilotWorkspaceOnlyProposalPermissionDoesNotRequireActiveFileAccess()
    {
        using var rig = new AgentSessionToolsTestRig();
        const string provider = AgentProviderIds.GitHubCopilotSubscription;
        var permissions = rig.Permissions with
        {
            ProviderId = provider,
            Workspace = new AgentWorkspacePermissions { UseFilesFolder = true },
            DataSending = rig.Permissions.DataSending with { ActiveFile = false, WorkspaceFiles = true },
            EditProposals = new AgentEditProposalPermissions { ActiveFile = false, OtherWorkspaceFiles = true },
        };
        var context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, WorkspaceFolder: rig.WorkspaceFolder,
            ActiveFilePath: Path.Combine(rig.WorkspaceFolder, "teste.md"), ActiveFileName: "teste.md",
            TabId: "tab-active", BufferText: "active-file-canary");
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions,
            new AgentPlatformFacts(true, true, NativeToolsAvailable: false));
        var session = Guid.NewGuid();
        var turn = Guid.NewGuid();
        rig.NativeChatScopes.Register(new AgentNativeChatTurnScope(session, turn, provider, plan, permissions, context)
        {
            ConversationId = rig.ConversationId,
            ActiveFileAttachmentResolved = true,
            ActiveFileAttachmentMatchesSnapshot = true,
        });
        rig.WriteFile("report.md", "value = 1\n");
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        async Task<AgentToolInvocationResult> Invoke(string arguments) => await rig.Registry.InvokeAsync(principal,
            new AgentInvocationContext(provider, null, session, turn), AgentOutputDestination.ProviderExternal(provider),
            AgentToolOutputScopes.For("propose_file_edit"), "propose_file_edit", arguments);

        var workspace = await Invoke("""{"path":"report.md","edits":[{"old_text":"value = 1","new_text":"value = 2"}]}""");
        Assert.That(workspace.Succeeded, Is.True, workspace.ErrorCode);
        var activeBuffer = await Invoke("""{"target":"active_buffer","new_content":"changed"}""");
        var activePath = await Invoke("""{"path":"teste.md","new_content":"changed"}""");
        var absent = await Invoke("""{"path":"new-report.md","new_content":"changed"}""");
        Assert.Multiple(() =>
        {
            Assert.That(activeBuffer.ErrorCode, Is.EqualTo("TargetNotPermitted"));
            Assert.That(activePath.ErrorCode, Is.EqualTo("TargetNotPermitted"));
            Assert.That(absent.ErrorCode, Is.EqualTo("NotFound"));
            Assert.That(rig.Sink.Proposals, Has.Count.EqualTo(1));
            Assert.That(rig.Sink.Proposals.Single().TargetPath, Is.EqualTo(Path.Combine(rig.WorkspaceFolder, "report.md")));
            Assert.That(rig.Sink.Proposals.Single().ProposedText, Is.EqualTo("value = 2\n"));
        });
    }

    [Test]
    public async Task CopilotProposalSharesDiscoveryQuotaAndRecoversOnlyInNewTurn()
    {
        using var rig = new AgentSessionToolsTestRig();
        const string provider = AgentProviderIds.GitHubCopilotSubscription;
        var permissions = rig.Permissions with { ProviderId = provider, MaximumToolCallsPerTurn = 20 };
        var workspace = new AgentWorkspaceContext(DateTimeOffset.UtcNow, ActiveFileName: "teste.md",
            TabId: "tab-report", BufferText: "base\n");
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, new AgentPlatformFacts(false, true, false));
        var session = Guid.NewGuid();
        var turn = Guid.NewGuid();
        void Register(Guid turnId) => rig.NativeChatScopes.Register(new AgentNativeChatTurnScope(session, turnId,
            provider, plan, permissions, workspace)
        {
            ConversationId = rig.ConversationId,
            ActiveFileAttachmentResolved = true,
            ActiveFileAttachmentMatchesSnapshot = true,
        });
        Register(turn);
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        async Task<AgentToolInvocationResult> Invoke(Guid turnId, string tool, string arguments) =>
            await rig.Registry.InvokeAsync(principal, new AgentInvocationContext(provider, null, session, turnId),
                AgentOutputDestination.ProviderExternal(provider), AgentToolOutputScopes.For(tool), tool, arguments);
        for (var call = 0; call < 20; call++)
        {
            var discovery = await Invoke(turn, "get_workspace_context", "{}");
            Assert.That(discovery.Succeeded, Is.True, discovery.ErrorCode);
        }
        const string proposal = """{"target":"active_buffer","new_content":"report\n"}""";
        var refused = await Invoke(turn, "propose_file_edit", proposal);
        Assert.Multiple(() =>
        {
            Assert.That(refused.ErrorCode, Is.EqualTo("ToolCallLimitExceeded"));
            Assert.That(rig.Sink.Proposals, Is.Empty);
            Assert.That(rig.Audit.Events[^1].Outcome, Is.EqualTo(AgentAuditOutcome.Denied));
            Assert.That(rig.Audit.Events[^1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.LimitExceeded));
        });
        var newTurn = Guid.NewGuid();
        Register(newTurn);
        var recovered = await Invoke(newTurn, "propose_file_edit", proposal);
        Assert.That(recovered.Succeeded, Is.True, recovered.ErrorCode);
        Assert.That(rig.Sink.Proposals, Has.Count.EqualTo(1));
        Assert.That(rig.Sink.Proposals.Single().TabId, Is.EqualTo("tab-report"));
    }

    [Test]
    public async Task NativeChatWorkspaceScopesDoNotShareSnapshotsAcrossSessions()
    {
        using var rig = new AgentSessionToolsTestRig();
        var principal = new AgentPrincipal(Guid.NewGuid(), AgentPrincipalOrigin.Internal, 1);
        var firstSession = Guid.NewGuid();
        var secondSession = Guid.NewGuid();
        var firstTurn = Guid.NewGuid();
        var secondTurn = Guid.NewGuid();
        var firstFolder = Path.Combine(rig.WorkspaceFolder, "first");
        var secondFolder = Path.Combine(rig.WorkspaceFolder, "second");
        rig.Files.AddDirectory(firstFolder);
        rig.Files.AddDirectory(secondFolder);
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions,
            new AgentPlatformFacts(true, true));
        rig.NativeChatScopes.Register(new AgentNativeChatTurnScope(firstSession, firstTurn, "claude-code", plan,
            rig.Permissions with { Workspace = new AgentWorkspacePermissions { UseFilesFolder = true } },
            new AgentWorkspaceContext(DateTimeOffset.UtcNow, firstFolder)));
        rig.NativeChatScopes.Register(new AgentNativeChatTurnScope(secondSession, secondTurn, "claude-code", plan,
            rig.Permissions with { Workspace = new AgentWorkspacePermissions { UseFilesFolder = true } },
            new AgentWorkspaceContext(DateTimeOffset.UtcNow, secondFolder)));

        var first = await InvokeNativeAsync(rig, principal, firstSession, firstTurn);
        var second = await InvokeNativeAsync(rig, principal, secondSession, secondTurn);

        using var firstJson = JsonDocument.Parse(first.StructuredContentJson!);
        using var secondJson = JsonDocument.Parse(second.StructuredContentJson!);
        Assert.That(firstJson.RootElement.GetProperty("workspaceFolder").GetString(), Is.EqualTo(firstFolder));
        Assert.That(firstJson.RootElement.GetProperty("workspaceFolder").GetString(), Is.Not.EqualTo(secondFolder));
        Assert.That(secondJson.RootElement.GetProperty("workspaceFolder").GetString(), Is.EqualTo(secondFolder));
        Assert.That(secondJson.RootElement.GetProperty("workspaceFolder").GetString(), Is.Not.EqualTo(firstFolder));
    }

    private static Task<AgentToolInvocationResult> InvokeNativeAsync(AgentSessionToolsTestRig rig,
        AgentPrincipal principal, Guid sessionId, Guid turnId) => rig.Registry.InvokeAsync(principal,
        new AgentInvocationContext("claude-code", null, sessionId, turnId),
        AgentOutputDestination.ProviderExternal("claude-code"),
        AgentToolOutputScopes.For("get_workspace_context"), "get_workspace_context", "{}");

    [Test]
    public async Task TurnPlanLimitsToolsAndConnectionsNullMeansAllEmptyMeansNone()
    {
        using var rig = new AgentSessionToolsTestRig();
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions, new AgentPlatformFacts(true, true));

        var all = await ListConnectionIdsAsync(rig);
        rig.BindPlan(plan with { AllowedConnectionIds = [rig.Profile.Id] });
        var selected = await ListConnectionIdsAsync(rig);
        var outside = await rig.CallAsync("list_databases", new { connectionId = rig.Other.Id });
        rig.BindPlan(plan with { AllowedConnectionIds = [] });
        var none = await ListConnectionIdsAsync(rig);
        rig.BindPlan(plan with { ProductTools = ["list_connections"] });
        var notPlanned = await rig.CallAsync("list_databases", new { connectionId = rig.Profile.Id });

        Assert.Multiple(() =>
        {
            Assert.That(all, Is.EquivalentTo(new[] { rig.Profile.Id, rig.Other.Id }));
            Assert.That(selected, Is.EqualTo(new[] { rig.Profile.Id }));
            Assert.That(outside.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(none, Is.Empty, "Lista vazia significa nenhuma conexão, não todas.");
            Assert.That(notPlanned.ErrorCode, Is.EqualTo("UnknownTool"));
        });
    }

    [Test]
    public async Task GetIndexesReturnsStructuredMetadataWithoutFilterValues()
    {
        using var rig = new AgentSessionToolsTestRig();
        var result = await rig.CallAsync("get_indexes", new { connectionId = rig.Profile.Id, database = "app", collection = "items" });

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        var ttl = json.RootElement.GetProperty("indexes")[1];
        Assert.Multiple(() =>
        {
            Assert.That(ttl.GetProperty("ttlSeconds").GetInt64(), Is.EqualTo(3600));
            Assert.That(ttl.GetProperty("keyDirections")[0].GetString(), Is.EqualTo("-1"));
            Assert.That(ttl.GetProperty("partialFilterFields")[0].GetString(), Is.EqualTo("status"));
            Assert.That(json.RootElement.GetProperty("indexes")[0].TryGetProperty("ttlSeconds", out _), Is.False);
        });
    }

    [Test]
    public async Task CachedSchemaReadsOnlyTheCacheWithPeekAndNeverSamples()
    {
        using var rig = new AgentSessionToolsTestRig();
        rig.Cache.Schema = new SchemaBuilder().AddDocuments(
        [
            "{\"name\":\"Ana\",\"age\":30,\"address\":{\"city\":\"Recife\"}}",
            "{\"name\":\"Bia\",\"age\":\"31\"}"
        ]).Build();

        var result = await rig.CallAsync("get_cached_schema", new { connectionId = rig.Profile.Id, database = "app", collection = "people" });

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        var text = result.StructuredContentJson!;
        using var json = JsonDocument.Parse(text);
        var fields = json.RootElement.GetProperty("fields").EnumerateArray().ToArray();
        var age = fields.Single(field => field.GetProperty("path").GetString() == "age");
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("available").GetBoolean(), Is.True);
            Assert.That(json.RootElement.GetProperty("source").GetString(), Is.EqualTo("sampled"));
            Assert.That(fields.Select(field => field.GetProperty("path").GetString()), Does.Contain("address.city"));
            Assert.That(age.GetProperty("types").GetArrayLength(), Is.EqualTo(2), "Tipos com contagem.");
            Assert.That(rig.Cache.LastAccess, Is.EqualTo(MetadataAccess.Peek));
            Assert.That(rig.Cache.ForbiddenCalls, Is.Zero, "Nenhuma carga, amostragem ou escrita no cache.");
            Assert.That(rig.Metadata.Calls, Is.Zero, "MongoDB nunca é consultado.");
            Assert.That(text, Does.Not.Contain("Ana").And.Not.Contain("Recife"), "Somente nomes e tipos, nunca valores.");
            Assert.That(text, Does.Not.Contain(AgentSessionToolsTestRig.UriCanary).And.Not.Contain("mongodb://"));
        });
    }

    [Test]
    public async Task CachedSchemaWithoutCacheAnswersStructuredEmptyAndFallsBackToLearnedSchema()
    {
        using var rig = new AgentSessionToolsTestRig();
        var arguments = new { connectionId = rig.Profile.Id, database = "app", collection = "people" };
        var empty = await rig.CallAsync("get_cached_schema", arguments);

        var key = LearnedSchemaKey.Create(rig.Profile.Id, "app", "people");
        var observed = DateTimeOffset.UtcNow.AddHours(-1);
        rig.Learned.Result = new LearnedSchemaHydrationResult(LearnedSchemaHydrationState.Available,
            new LearnedSchemaSnapshot(key, 1, 3, null, observed, observed, 10, 1, 0, false,
            [
                new LearnedFieldStatistics(new LearnedFieldPath(["email"]), 9, 10,
                    new Dictionary<string, long> { ["string"] = 9 }, observed, observed)
            ]), null);
        var learned = await rig.CallAsync("get_cached_schema", arguments);
        rig.Learned.Result = new LearnedSchemaHydrationResult(LearnedSchemaHydrationState.Unavailable, null, "future");
        var unavailable = await rig.CallAsync("get_cached_schema", arguments);

        Assert.Multiple(() =>
        {
            Assert.That(empty.StructuredContentJson, Does.Contain("\"available\":false").And.Contain("\"reason\":\"NoCachedSchema\""));
            Assert.That(learned.StructuredContentJson, Does.Contain("\"source\":\"learned\"").And.Contain("\"path\":\"email\""));
            Assert.That(learned.StructuredContentJson, Does.Contain("\"occurrence\":0.9"));
            Assert.That(unavailable.StructuredContentJson, Does.Contain("LearnedSchemaUnavailable"));
            Assert.That(rig.Metadata.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task CachedSchemaIsBoundedInFieldsAndDepth()
    {
        using var rig = new AgentSessionToolsTestRig();
        var wide = "{" + string.Join(',', Enumerable.Range(0, 600).Select(index => $"\"f{index:D3}\":1")) + "}";
        var deep = "{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{\"g\":{\"h\":{\"i\":{\"j\":1}}}}}}}}}}";
        rig.Cache.Schema = new SchemaBuilder().AddDocuments([wide, deep]).Build();

        var result = await rig.CallAsync("get_cached_schema", new { connectionId = rig.Profile.Id, database = "app", collection = "wide" });

        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        var paths = json.RootElement.GetProperty("fields").EnumerateArray().Select(field => field.GetProperty("path").GetString()!).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(paths, Has.Length.LessThanOrEqualTo(AgentToolRegistry.MaximumCachedSchemaFields));
            Assert.That(paths.Max(path => path.Split('.').Length), Is.LessThanOrEqualTo(AgentToolRegistry.MaximumCachedSchemaDepth));
            Assert.That(json.RootElement.GetProperty("truncated").GetBoolean(), Is.True);
        });
    }

    [TestCase(false, true, "fresh")]
    [TestCase(false, false, "stale")]
    [TestCase(true, false, null)]
    public async Task CachedSchemaHonorsLearnedOriginAndSessionTrust(bool superseded, bool sessionConnected, string? expectedFreshness)
    {
        using var rig = new AgentSessionToolsTestRig();
        rig.Cache.IsConnectedValue = sessionConnected;
        var key = LearnedSchemaKey.Create(rig.Profile.Id, "app", "people");
        var observed = DateTimeOffset.UtcNow.AddHours(-1);
        var snapshot = new LearnedSchemaSnapshot(key, 1, 3,
            superseded ? Guid.NewGuid() : rig.Profile.SourceGenerationId,
            observed, observed, 10, 1, 0, false,
            [new LearnedFieldStatistics(new LearnedFieldPath(["origin-field-canary"]), 9, 10,
                new Dictionary<string, long> { ["string"] = 9 }, observed, observed)]);
        rig.Learned.Result = new LearnedSchemaHydrationResult(LearnedSchemaHydrationState.Available, snapshot, null);

        var result = await rig.CallAsync("get_cached_schema",
            new { connectionId = rig.Profile.Id, database = "app", collection = "people" });

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("available").GetBoolean(), Is.EqualTo(!superseded));
            Assert.That(json.RootElement.GetProperty("source").GetString(), Is.EqualTo(superseded ? "none" : "learned"));
            Assert.That(json.RootElement.GetProperty("fields").GetArrayLength(), Is.EqualTo(superseded ? 0 : 1));
            Assert.That(result.StructuredContentJson!.Contains("origin-field-canary", StringComparison.Ordinal), Is.EqualTo(!superseded));
            Assert.That(json.RootElement.TryGetProperty("freshness", out var freshness)
                ? freshness.GetString() : null, Is.EqualTo(expectedFreshness));
            Assert.That(rig.Learned.Result.Snapshot, Is.SameAs(snapshot), "A superseded snapshot remains untouched for recovery.");
            Assert.That(rig.Cache.Reads, Is.EqualTo(1));
            Assert.That(rig.Cache.LastAccess, Is.EqualTo(MetadataAccess.Peek));
            Assert.That(rig.Cache.ForbiddenCalls, Is.Zero, "Do not connect, load, refresh, sample or invalidate.");
            Assert.That(rig.Metadata.Calls, Is.Zero, "No MongoDB access to replace a superseded origin.");
        });
        if (superseded)
            Assert.That(json.RootElement.GetProperty("reason").GetString(), Is.EqualTo("NoCachedSchema"));
    }

    [Test]
    public async Task CachedSchemaNeedsTheSchemaGrantAndThePlan()
    {
        using var rig = new AgentSessionToolsTestRig();
        rig.Policies.Set(rig.PrincipalId, 1, rig.GrantsFor(rig.Profile).Where(grant => grant.Permission != AgentPermission.ReadSchema));
        var withoutGrant = await rig.CallAsync("get_cached_schema", new { connectionId = rig.Profile.Id, database = "app", collection = "c" });

        using var noConsent = new AgentSessionToolsTestRig(permissions: permissions => permissions with
        {
            DataSending = permissions.DataSending with { InferredSchema = false }
        });
        var withoutPermission = await noConsent.CallAsync("get_cached_schema",
            new { connectionId = noConsent.Profile.Id, database = "app", collection = "c" });

        Assert.Multiple(() =>
        {
            Assert.That(withoutGrant.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(withoutPermission.ErrorCode, Is.EqualTo("UnknownTool"), "DataSending.InferredSchema desligado: fora do plano.");
            Assert.That(rig.Cache.Reads + noConsent.Cache.Reads, Is.Zero);
        });
    }

    [Test]
    public async Task WorkspaceContextDescribesFolderFileAndTabWithoutContentOrOutOfScopeConnection()
    {
        using var rig = new AgentSessionToolsTestRig();
        var file = rig.WriteFile("scripts/clientes.json", "{\"segredo\":\"nao-enviar\"}");
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder, file, "clientes.json",
            "tab-1", 3, "BUFFER-CANARY", rig.Profile.Id.ToString("D"), "Principal", "CakeShop", "orders");
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions, new AgentPlatformFacts(true, true)));

        var inScope = await rig.CallRawAsync("get_workspace_context", "{}");
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions, new AgentPlatformFacts(true, true));
        rig.BindPlan(plan with { AllowedConnectionIds = [rig.Other.Id] });
        var outOfScope = await rig.CallRawAsync("get_workspace_context", "{}");
        var extra = await rig.CallRawAsync("get_workspace_context", "{\"path\":\"x\"}");

        using var json = JsonDocument.Parse(inScope.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("workspaceFolder").GetString(), Is.EqualTo(rig.WorkspaceFolder));
            Assert.That(json.RootElement.GetProperty("activeFile").GetProperty("relativePath").GetString(), Is.EqualTo("scripts/clientes.json"));
            Assert.That(json.RootElement.GetProperty("tab").GetProperty("database").GetString(), Is.EqualTo("CakeShop"));
            Assert.That(inScope.StructuredContentJson, Does.Not.Contain("BUFFER-CANARY").And.Not.Contain("nao-enviar"));
            Assert.That(outOfScope.StructuredContentJson, Does.Contain("\"connectionInScope\":false")
                .And.Not.Contain(rig.Profile.Id.ToString("D")).And.Not.Contain("Principal").And.Not.Contain("CakeShop"));
            Assert.That(extra.ErrorCode, Is.EqualTo("InvalidArguments"));
        });
    }

    [Test]
    public async Task WorkspaceContextSuppressesMetadataWhenTerminalAuditWriteFails()
    {
        using var rig = new AgentSessionToolsTestRig();
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder,
            ActiveFileName: "private-query.js", ConnectionId: rig.Profile.Id.ToString("D"),
            ConnectionName: "private-profile", DatabaseName: "private-database",
            CollectionName: "private-collection");
        rig.Audit.AppendHandler = (entry, _) => entry.Outcome == AgentAuditOutcome.Succeeded
            ? throw new IOException("audit storage failure")
            : Task.CompletedTask;

        var result = await rig.CallRawAsync("get_workspace_context", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(rig.Audit.Events.Select(static item => item.Outcome),
                Is.EqualTo(new[] { AgentAuditOutcome.Intent }));
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("private-"));
            Assert.That(rig.Files.Reads, Is.Zero);
            Assert.That(rig.Metadata.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task WorkspaceContextSuppressesCapturedSnapshotWhenItChangesBeforePublication()
    {
        using var rig = new AgentSessionToolsTestRig();
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions,
            new AgentPlatformFacts(true, true));
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder,
            ActiveFileName: "old-snapshot-canary.js");
        rig.BindPlan(plan);
        var auditEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAudit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Audit.AppendHandler = async (entry, _) =>
        {
            if (entry.Outcome != AgentAuditOutcome.Succeeded) return;
            auditEntered.TrySetResult();
            await releaseAudit.Task;
        };

        var pending = rig.CallRawAsync("get_workspace_context", "{}");
        try
        {
            await auditEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder,
                ActiveFileName: "new-snapshot-canary.js");
            rig.BindPlan(plan);
        }
        finally
        {
            releaseAudit.TrySetResult();
        }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(result.StructuredContentJson, Is.Null);
            Assert.That(rig.Audit.Events.Select(static item => item.Outcome),
                Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded, AgentAuditOutcome.Denied }));
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("old-snapshot-canary"));
            Assert.That(rig.Files.Reads, Is.Zero);
            Assert.That(rig.Metadata.Calls, Is.Zero);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task WorkspaceContextDoesNotPublishWhenCallerCancelsOrDeadlineExpiresDuringTerminalAudit(bool deadline)
    {
        using var rig = new AgentSessionToolsTestRig(executionTimeout: deadline
            ? TimeSpan.FromMilliseconds(500) : null);
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder,
            ActiveFileName: "pending-metadata-canary.js");
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions,
            new AgentPlatformFacts(true, true)));
        var auditEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAudit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Audit.AppendHandler = async (entry, _) =>
        {
            if (entry.Outcome != AgentAuditOutcome.Succeeded) return;
            auditEntered.TrySetResult();
            await releaseAudit.Task;
        };
        using var callerCancellation = new CancellationTokenSource();
        var pending = rig.Registry.InvokeAsync(rig.Principal,
            new AgentInvocationContext(AgentBrokerProtocol.McpProviderId, rig.ChannelId, rig.ChannelId, Guid.NewGuid()),
            AgentSessionToolsTestRig.Destination, AgentToolOutputScopes.For("get_workspace_context"),
            "get_workspace_context", "{}", callerCancellation.Token);
        try
        {
            await auditEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (deadline)
                await Task.Delay(TimeSpan.FromMilliseconds(650));
            else
                callerCancellation.Cancel();
        }
        finally
        {
            releaseAudit.TrySetResult();
        }

        if (deadline)
        {
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
                Assert.That(result.StructuredContentJson, Is.Null);
            });
        }
        else
        {
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        }

        Assert.Multiple(() =>
        {
            Assert.That(rig.Audit.Events.Select(static item => item.Outcome),
                Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded, AgentAuditOutcome.Cancelled }));
            Assert.That(rig.Audit.Events[^1].ItemCount, Is.Zero);
            Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
            Assert.That(JsonSerializer.Serialize(rig.Audit.Events), Does.Not.Contain("pending-metadata-canary"));
            Assert.That(rig.Files.Reads, Is.Zero);
            Assert.That(rig.Metadata.Calls, Is.Zero);
        });
    }

    [Test]
    public async Task WorkspaceContextOmitsPathsLongerThanPublishedSchemaBounds()
    {
        using var rig = new AgentSessionToolsTestRig();
        var permissions = rig.Permissions with
        {
            Workspace = new AgentWorkspacePermissions { UseFilesFolder = true }
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions,
            new AgentPlatformFacts(true, true));
        var longRelativePath = string.Join(Path.DirectorySeparatorChar,
            Enumerable.Repeat(new string('d', 200), 6)) + Path.DirectorySeparatorChar + "query.js";
        var longRoot = Path.Combine(rig.WorkspaceFolder, longRelativePath);
        Assert.That(longRoot.Length, Is.GreaterThan(1_024));
        rig.Files.AddDirectory(longRoot);
        var activeBelowLongRoot = Path.Combine(longRoot, "query.js");
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, longRoot,
            activeBelowLongRoot, "query.js");
        rig.BindPlan(plan, permissions);

        var longRootResult = await rig.CallRawAsync("get_workspace_context", "{}");

        var activePath = Path.Combine(rig.WorkspaceFolder, longRelativePath);
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder,
            activePath, "query.js");
        rig.BindPlan(plan, permissions);
        var longRelativeResult = await rig.CallRawAsync("get_workspace_context", "{}");

        using var rootOutput = JsonDocument.Parse(longRootResult.StructuredContentJson!);
        using var relativeOutput = JsonDocument.Parse(longRelativeResult.StructuredContentJson!);
        var rootActiveFile = rootOutput.RootElement.GetProperty("activeFile");
        var activeFile = relativeOutput.RootElement.GetProperty("activeFile");
        Assert.Multiple(() =>
        {
            Assert.That(longRootResult.Succeeded, Is.True, longRootResult.ErrorCode);
            Assert.That(rootOutput.RootElement.GetProperty("workspaceFolder").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(rootActiveFile.GetProperty("insideWorkspace").GetBoolean(), Is.True,
                "Suppressing a long root path must not suppress the internal containment check.");
            Assert.That(rootActiveFile.GetProperty("relativePath").GetString(), Is.EqualTo("query.js"));
            Assert.That(longRelativeResult.Succeeded, Is.True, longRelativeResult.ErrorCode);
            Assert.That(activeFile.GetProperty("insideWorkspace").GetBoolean(), Is.True);
            Assert.That(activeFile.GetProperty("relativePath").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(rig.Files.Reads, Is.Zero, "Paths are metadata; no file contents are read.");
        });
    }

    [Test]
    public async Task WorkspaceContextDoesNotCallAnUnprovenLinkTargetInsideTheWorkspace()
    {
        using var rig = new AgentSessionToolsTestRig();
        var file = rig.WriteFile("linked.js", "synthetic content\n");
        rig.Files.SetLink(file);
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder, file,
            "linked.js", "tab-linked", 3, "synthetic content\n");
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions,
            new AgentPlatformFacts(true, true)));

        var result = await rig.CallRawAsync("get_workspace_context", "{}");

        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        var activeFile = json.RootElement.GetProperty("activeFile");
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.ErrorCode);
            Assert.That(activeFile.GetProperty("insideWorkspace").GetBoolean(), Is.False,
                "Um caminho lexical interno com travessia de link não prova que o destino está dentro da raiz.");
            Assert.That(activeFile.GetProperty("relativePath").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(rig.Files.Reads, Is.Zero, "get_workspace_context não lê conteúdo do arquivo.");
        });
    }

    [Test]
    public async Task WorkspaceToolsUseTheSnapshotBoundToTheTurnEvenAfterTheActiveWorkspaceChanges()
    {
        using var rig = new AgentSessionToolsTestRig();
        var firstFile = rig.WriteFile("first.js", "const first = true;\n");
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder, firstFile,
            "first.js", "tab-first", 4, "const first = true;\n", Guid.NewGuid().ToString("D"),
            "Conexão fora do escopo", "db-original", "collection-original");
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions, new AgentPlatformFacts(true, true))
            with { AllowedConnectionIds = [rig.Profile.Id] });
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow,
            Path.Combine(rig.WorkspaceFolder, "second"), Path.Combine(rig.WorkspaceFolder, "second.js"),
            "second.js", "tab-second", 1, "const second = true;\n");

        var result = await rig.CallRawAsync("get_workspace_context", "{}");
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(json.RootElement.GetProperty("activeFile").GetProperty("name").GetString(), Is.EqualTo("first.js"), result.StructuredContentJson);
            Assert.That(json.RootElement.GetProperty("tab").GetProperty("connectionInScope").GetBoolean(), Is.False, result.StructuredContentJson);
            Assert.That(result.StructuredContentJson, Does.Not.Contain("second.js"));
            Assert.That(rig.Workspace.Captures, Is.EqualTo(0), "The MCP handler must not recapture mutable UI state.");
        });
    }

    [Test]
    public async Task ProposeFileEditRegistersHunksWithoutMutatingSource()
    {
        using var rig = new AgentSessionToolsTestRig();
        const string original = "linha 1\nlinha 2\nlinha 3\nlinha 4\n";
        var file = rig.WriteFile("dados/clientes.json", original);
        var before = rig.Files.Revision;

        var result = await rig.CallAsync("propose_file_edit", new
        {
            path = "dados/clientes.json",
            edits = new[] { new { old_text = "linha 2\n", new_text = "linha dois\nlinha 2b\n" } }
        });

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        var proposal = rig.Sink.Proposals.Single();
        using var receipt = JsonDocument.Parse(result.StructuredContentJson!);
        Assert.Multiple(() =>
        {
            Assert.That(rig.Files.GetText(file), Is.EqualTo(original), "A proposta não altera a fonte.");
            Assert.That(rig.Files.Revision, Is.EqualTo(before));
            Assert.That(receipt.RootElement.GetProperty("status").GetString(), Is.EqualTo("registered"));
            Assert.That(receipt.RootElement.GetProperty("proposalId").GetGuid(), Is.EqualTo(proposal.Id));
            Assert.That(receipt.RootElement.GetProperty("added").GetInt32(), Is.EqualTo(2));
            Assert.That(receipt.RootElement.GetProperty("removed").GetInt32(), Is.EqualTo(1));
            Assert.That(proposal.ConversationId, Is.EqualTo(rig.ConversationId));
            Assert.That(proposal.TargetPath, Is.EqualTo(Path.Combine(rig.WorkspaceFolder, "dados", "clientes.json")));
            Assert.That(proposal.ProposedText, Is.EqualTo("linha 1\nlinha dois\nlinha 2b\nlinha 3\nlinha 4\n"));
            Assert.That(proposal.BaseTextSha256, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(original)))));
            Assert.That(proposal.Hunks, Has.Count.EqualTo(1));
            Assert.That(proposal.Hunks[0].OriginalLines, Is.EqualTo(OriginalHunkLines));
            Assert.That(proposal.Hunks[0].ProposedLines, Is.EqualTo(ProposedHunkLines));
            Assert.That(result.StructuredContentJson, Does.Not.Contain("linha"), "O recibo não repete o conteúdo.");
        });
    }

    [Test]
    public async Task ProposeFileEditUsesTheUnsavedBufferOfTheActiveFileAndKeepsItsLineEndings()
    {
        using var rig = new AgentSessionToolsTestRig();
        var file = rig.WriteFile("app.js", "disco\r\n");
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder, file, "app.js",
            "tab-7", 9, "db.a.find()\r\ndb.b.find()\r\n");
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions, new AgentPlatformFacts(true, true)));

        var result = await rig.CallAsync("propose_file_edit", new
        {
            path = file, edits = new[] { new { old_text = "db.a.find()\ndb.b.find()", new_text = "db.a.find({})\ndb.b.find()" } }
        });

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        var proposal = rig.Sink.Proposals.Single();
        Assert.Multiple(() =>
        {
            Assert.That(proposal.TabId, Is.EqualTo("tab-7"));
            Assert.That(proposal.OriginalText, Is.EqualTo("db.a.find()\r\ndb.b.find()\r\n"), "Base é o buffer, não o disco.");
            Assert.That(proposal.ProposedText, Is.EqualTo("db.a.find({})\r\ndb.b.find()\r\n"));
            Assert.That(rig.Files.GetText(file), Is.EqualTo("disco\r\n"));
        });
    }

    [Test]
    public async Task ProductRegistryRegistersProposalInProductionDesktopStoreWithoutApplyingIt()
    {
        var files = new MemoryAgentFiles();
        var store = new AgentEditProposalStore(static action => action(), files);
        using var rig = new AgentSessionToolsTestRig(proposalSink: store, files: files);
        const string original = "db.syntheticItems.find({}).limit(10);\n";
        var file = rig.WriteFile("consulta-sintetica.js", original);
        var writeTime = rig.Files.Revision;

        var result = await rig.CallAsync("propose_file_edit", new
        {
            path = file,
            edits = new[] { new { old_text = ".limit(10)", new_text = ".limit(5)" } }
        });

        using var receipt = JsonDocument.Parse(result.StructuredContentJson!);
        var proposalId = receipt.RootElement.GetProperty("proposalId").GetGuid();
        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        Assert.That(store.TryGet(proposalId, out var entry), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(entry.Proposal.TargetPath, Is.EqualTo(file));
            Assert.That(entry.Proposal.OriginalText, Is.EqualTo(original));
            Assert.That(entry.Proposal.ProposedText, Is.EqualTo("db.syntheticItems.find({}).limit(5);\n"));
            Assert.That(entry.Status, Is.EqualTo(AgentEditProposalStatus.Registered), "Registration must wait for explicit review.");
            Assert.That(rig.Files.GetText(file), Is.EqualTo(original), "The production proposal store never changes the source.");
            Assert.That(rig.Files.Revision, Is.EqualTo(writeTime));
        });
    }

    [Test]
    public async Task ProposeFileEditCanTargetAnUntitledActiveBufferWithoutWorkspaceFolder()
    {
        using var rig = new AgentSessionToolsTestRig();
        const string original = "db.getCollection(\"Customers\").find({}).limit(100);\n";
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow,
            WorkspaceFolder: null, ActiveFilePath: null, ActiveFileName: "Aba sem título",
            TabId: "active-tab", DocumentVersion: 3, BufferText: original);
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions,
            new AgentPlatformFacts(true, true)));

        var result = await rig.CallAsync("propose_file_edit", new
        {
            target = "active_buffer",
            edits = new[] { new { old_text = ".limit(100)", new_text = ".limit(25)" } }
        });

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        var proposal = rig.Sink.Proposals.Single();
        Assert.Multiple(() =>
        {
            Assert.That(proposal.TargetPath, Is.Null);
            Assert.That(proposal.TargetName, Is.EqualTo("Aba sem título"));
            Assert.That(proposal.TabId, Is.EqualTo("active-tab"));
            Assert.That(proposal.OriginalText, Is.EqualTo(original));
            Assert.That(proposal.ProposedText, Is.EqualTo(original.Replace(".limit(100)", ".limit(25)", StringComparison.Ordinal)));
        });
    }

    [TestCase("inexistente\n")]
    [TestCase("linha\n")]
    [TestCase("linha\nlinha\n")]
    public async Task ProposeFileEditUsesOneSafeErrorForMissingAmbiguousOrNoChange(string oldText)
    {
        using var rig = new AgentSessionToolsTestRig();
        rig.WriteFile("a.txt", "linha\nlinha\n");
        var newText = oldText == "linha\nlinha\n" ? oldText : "x\n";

        var result = await rig.CallAsync("propose_file_edit", new { path = "a.txt", edits = new[] { new { old_text = oldText, new_text = newText } } });

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo("EditNotApplicable"), "A tool não deve revelar se o texto-base estava ausente, repetido ou sem mudança.");
            Assert.That(rig.Sink.Proposals, Is.Empty);
        });
    }

    [TestCase("../fora.txt", "OutsideWorkspace")]
    [TestCase(".env", "Excluded")]
    [TestCase("config/secrets/token.txt", "Excluded")]
    [TestCase("nao-existe.txt", "NotFound")]
    [TestCase("a.txt:stream", "InvalidPath")]
    public async Task ProposeFileEditRefusesTargetsOutsideTheRules(string path, string expected)
    {
        using var rig = new AgentSessionToolsTestRig();
        rig.WriteFile(".env", "TOKEN=1\n");
        rig.WriteFile("config/secrets/token.txt", "x\n");
        rig.Files.Set(Path.Combine(Path.GetDirectoryName(rig.WorkspaceFolder)!, "fora.txt"), Encoding.UTF8.GetBytes("x\n"));

        var result = await rig.CallAsync("propose_file_edit", new { path, new_content = "novo\n" });

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(expected));
            Assert.That(rig.Sink.Proposals, Is.Empty);
        });
    }

    [Test]
    public async Task ProposeFileEditRejectsWorkspaceLinkIntroducedWhileReadingTheBase()
    {
        var files = new MemoryAgentFiles();
        var io = new LinkIntroducedDuringRead(files);
        using var rig = new AgentSessionToolsTestRig(files: files, proposalFileReader: io, pathProbe: io);
        var target = rig.WriteFile("safe.txt", "before\n");
        var invocation = rig.CallAsync("propose_file_edit", new
        {
            path = "safe.txt",
            edits = new[] { new { old_text = "outside secret fixture\n", new_text = "stolen fixture\n" } }
        });

        await io.ReadEntered.Task;
        io.InstallLink();
        io.ReleaseRead.TrySetResult();
        var result = await invocation;

        Assert.Multiple(() =>
        {
            Assert.That(io.ExternalFixtureWasRead, Is.True,
                "The synthetic reader models a link switch to an outside fixture during the awaited read.");
            Assert.That(result.ErrorCode, Is.EqualTo("OutsideWorkspace"));
            Assert.That(rig.Sink.Proposals, Is.Empty, "A reparse-point target must not produce a proposal.");
            Assert.That(files.GetText(target), Is.EqualTo("before\n"), "Proposal generation never writes its source.");
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ProposeFileEditDoesNotRegisterLateBaseReadAfterCallerCancellationOrDeadline(bool deadline)
    {
        var files = new MemoryAgentFiles();
        var reader = new LateProposalBaseReader(files);
        using var rig = new AgentSessionToolsTestRig(files: files, proposalFileReader: reader,
            executionTimeout: deadline ? TimeSpan.FromMilliseconds(500) : null);
        const string original = "const value = 1;\n";
        var path = rig.WriteFile("safe.js", original);
        using var callerCancellation = new CancellationTokenSource();
        var pending = rig.Registry.InvokeAsync(rig.Principal,
            new AgentInvocationContext(AgentBrokerProtocol.McpProviderId, rig.ChannelId, rig.ChannelId, Guid.NewGuid()),
            AgentSessionToolsTestRig.Destination, AgentToolOutputScopes.For("propose_file_edit"),
            "propose_file_edit", "{\"path\":\"safe.js\",\"edits\":[{\"old_text\":\"value = 1\",\"new_text\":\"value = 2\"}]}",
            callerCancellation.Token);
        try
        {
            await reader.ReadEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (deadline)
                await Task.Delay(TimeSpan.FromMilliseconds(650));
            else
                callerCancellation.Cancel();
        }
        finally
        {
            reader.ReleaseRead.TrySetResult();
        }

        if (deadline)
        {
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Multiple(() =>
            {
                Assert.That(result.ErrorCode, Is.EqualTo("DeadlineExceeded"));
                Assert.That(result.StructuredContentJson, Is.Null);
            });
        }
        else
        {
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        }

        Assert.Multiple(() =>
        {
            Assert.That(reader.Calls, Is.EqualTo(1), "The late completion must not start another read.");
            Assert.That(rig.Sink.Proposals, Is.Empty, "A cancelled/deadline read cannot register its late proposal.");
            Assert.That(files.GetText(path), Is.EqualTo(original), "A proposal path never writes the target file.");
            Assert.That(rig.Audit.Events.Select(static item => item.Outcome),
                Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Cancelled }));
            Assert.That(rig.Audit.Events[^1].ItemCount, Is.Zero);
            Assert.That(rig.Audit.Events[^1].OutputBytes, Is.Zero);
        });
    }

    [Test]
    public async Task ProposeFileEditHonorsTargetPermissionsAndRedactionMarkers()
    {
        using var rig = new AgentSessionToolsTestRig(permissions: permissions => permissions with
        {
            EditProposals = new AgentEditProposalPermissions { ActiveFile = true, OtherWorkspaceFiles = false }
        });
        rig.WriteFile("outro.txt", "a\n");
        var active = rig.WriteFile("ativo.js", "const uri = 'x';\n");
        rig.Workspace.Context = new AgentWorkspaceContext(DateTimeOffset.UtcNow, rig.WorkspaceFolder, active, "ativo.js",
            "tab", 1, "const uri = 'mongodb://u:p@h';\n");
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Agent, rig.Permissions, new AgentPlatformFacts(true, true)));

        var other = await rig.CallAsync("propose_file_edit", new { path = "outro.txt", new_content = "b\n" });
        var marker = await rig.CallAsync("propose_file_edit", new { path = "ativo.js", new_content = "const uri = '[segredo removido]';\n" });

        Assert.Multiple(() =>
        {
            Assert.That(other.ErrorCode, Is.EqualTo("TargetNotPermitted"));
            Assert.That(marker.ErrorCode, Is.EqualTo("RedactionMarkerIntroduced"), "Nunca sobrescrever um segredo com o marcador.");
            Assert.That(rig.Sink.Proposals, Is.Empty);
        });
    }

    [Test]
    public async Task ProposeFileEditIsNotExposedInPlanningModeAndSinkRefusalIsTyped()
    {
        using var planning = new AgentSessionToolsTestRig(AgentOperationMode.Planning);
        planning.WriteFile("a.txt", "a\n");
        var hidden = await planning.CallAsync("propose_file_edit", new { path = "a.txt", new_content = "b\n" });

        using var rig = new AgentSessionToolsTestRig();
        rig.WriteFile("a.txt", "a\n");
        rig.Sink.Answer = new AgentEditProposalSubmission(AgentEditProposalSubmissionStatus.BaseChanged);
        var refused = await rig.CallAsync("propose_file_edit", new { path = "a.txt", new_content = "b\n" });
        var malformed = await rig.CallRawAsync("propose_file_edit", "{\"path\":\"a.txt\",\"new_content\":\"b\",\"edits\":[]}");

        Assert.Multiple(() =>
        {
            Assert.That(hidden.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(refused.ErrorCode, Is.EqualTo("BaseChanged"));
            Assert.That(malformed.ErrorCode, Is.EqualTo("InvalidArguments"));
        });
    }

    [Test]
    public async Task ApproveAllowsOnceWithTheExactInputOnlyAfterAHumanDecision()
    {
        using var rig = new AgentSessionToolsTestRig(AgentOperationMode.AskConfirmations);
        const string input = "{\"connectionId\":\"9d1d9d7a-2b35-4e47-8f71-08a1a7e2a101\",\"nested\":{\"n\":{\"$numberLong\":\"9007199254740993\"}}}";

        var result = await rig.CallRawAsync("approve",
            "{\"tool_name\":\"mcp__kapibarastudio__list_databases\",\"input\":" + input + ",\"tool_use_id\":\"toolu_1\",\"extra\":1}");

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        var request = rig.Confirmation!.Requests.Single();
        Assert.Multiple(() =>
        {
            // Contract of --permission-prompt-tool (Claude Code CLI): {"behavior":"allow","updatedInput":<input>}.
            Assert.That(result.StructuredContentJson, Is.EqualTo("{\"behavior\":\"allow\",\"updatedInput\":" + input + "}"));
            Assert.That(request.ToolName, Is.EqualTo("mcp__kapibarastudio__list_databases"));
            Assert.That(request.Category, Is.EqualTo(AgentConfirmationCategories.MongoMetadataRead));
            Assert.That(request.ConversationId, Is.EqualTo(rig.ConversationId));
            Assert.That(request.ToolUseId, Is.EqualTo("toolu_1"));
            Assert.That(Enum.GetNames<AgentToolConfirmationDecision>(), Is.EquivalentTo(ConfirmationDecisions));
        });
    }

    [Test]
    public async Task SessionApprovalIsLimitedToExactReadOnlyArgumentsAndClearedWhenPermissionsChange()
    {
        using var rig = new AgentSessionToolsTestRig(AgentOperationMode.AskConfirmations);
        rig.Confirmation!.Answer = static (_, _) => Task.FromResult(AgentToolConfirmationDecision.ApprovedThisSession);
        const string input = "{\"tool_name\":\"mcp__kapibarastudio__list_databases\",\"input\":{\"connectionId\":\"9d1d9d7a-2b35-4e47-8f71-08a1a7e2a101\"}}";
        var first = await rig.CallRawAsync("approve", input);
        var second = await rig.CallRawAsync("approve", input);
        var changedArguments = await rig.CallRawAsync("approve",
            "{\"tool_name\":\"mcp__kapibarastudio__list_databases\",\"input\":{\"connectionId\":\"a81d9f50-1f7f-4ee6-bb56-10c8aeb34292\"}}");
        var promptCount = rig.Confirmation.Requests.Count;

        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.AskConfirmations,
            rig.Permissions with { KeepHistory = false }, new AgentPlatformFacts(true, true)),
            rig.Permissions with { KeepHistory = false });
        var afterPolicyChange = await rig.CallRawAsync("approve", input);
        Assert.Multiple(() =>
        {
            Assert.That(Behavior(first), Is.EqualTo("allow"), first.StructuredContentJson);
            Assert.That(Behavior(second), Is.EqualTo("allow"));
            Assert.That(Behavior(changedArguments), Is.EqualTo("allow"));
            Assert.That(promptCount, Is.EqualTo(2), "Repetição exata é liberada só na sessão; argumentos diferentes pedem confirmação.");
            Assert.That(Behavior(afterPolicyChange), Is.EqualTo("allow"));
            Assert.That(rig.Confirmation.Requests, Has.Count.EqualTo(3), "Mudança de permissão revoga a concessão da sessão.");
        });
    }

    [Test]
    public async Task DelayedApprovalIsDeniedWhenTurnScopeChangesBeforeTheUserDecisionReturns()
    {
        using var rig = new AgentSessionToolsTestRig(AgentOperationMode.AskConfirmations);
        var decision = new TaskCompletionSource<AgentToolConfirmationDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        rig.Confirmation!.Answer = (_, _) => decision.Task;
        const string request = "{\"tool_name\":\"mcp__kapibarastudio__list_databases\",\"input\":{\"connectionId\":\"9d1d9d7a-2b35-4e47-8f71-08a1a7e2a101\"}}";
        var pending = rig.CallRawAsync("approve", request);
        while (rig.Confirmation.Requests.Count == 0) await Task.Yield();

        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.AskConfirmations, rig.Permissions with { KeepHistory = false },
            new AgentPlatformFacts(true, true)), rig.Permissions with { KeepHistory = false });
        decision.SetResult(AgentToolConfirmationDecision.ApprovedThisSession);

        var result = await pending;
        Assert.That(Behavior(result), Is.EqualTo("deny"), "Aprovação pendente não pode sobreviver à revogação do escopo.");
    }

    [Test]
    public async Task SessionApprovalCannotAuthorizeCommandsOrFileMutations()
    {
        using var rig = new AgentSessionToolsTestRig(AgentOperationMode.Automatic, value => value with
        {
            NativeCommandExecution = true
        });
        rig.BindPlan(AgentModePolicy.Plan(AgentOperationMode.Automatic, rig.Permissions, new AgentPlatformFacts(true, true)));
        rig.Confirmation!.Answer = static (_, _) => Task.FromResult(AgentToolConfirmationDecision.ApprovedThisSession);
        var result = await rig.CallRawAsync("approve", "{\"tool_name\":\"Bash\",\"input\":{\"command\":\"dotnet test\"}}");
        Assert.That(Behavior(result), Is.EqualTo("deny"));
    }

    [Test]
    public async Task ApproveDeniesOnRejectionTimeoutMissingPortAndToolsOutsideThePlan()
    {
        using var rig = new AgentSessionToolsTestRig(AgentOperationMode.AskConfirmations, approvalTimeout: TimeSpan.FromMilliseconds(200));
        const string request = "{\"tool_name\":\"Read\",\"input\":{\"file_path\":\"a.txt\"}}";
        rig.Confirmation!.Answer = static (_, _) => Task.FromResult(AgentToolConfirmationDecision.Rejected);
        var rejected = await rig.CallRawAsync("approve", request);
        rig.Confirmation.Answer = static async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return AgentToolConfirmationDecision.ApprovedOnce;
        };
        var timedOut = await rig.CallRawAsync("approve", request);
        var prompts = rig.Confirmation.Requests.Count;
        var bash = await rig.CallRawAsync("approve", "{\"tool_name\":\"Bash\",\"input\":{\"command\":\"rm -rf /\"}}");
        var invalid = await rig.CallRawAsync("approve", "{\"tool_name\":\"Read\"}");

        using var noPort = new AgentSessionToolsTestRig(AgentOperationMode.AskConfirmations, withConfirmationPort: false);
        var unavailable = await noPort.CallRawAsync("approve", request);
        using var agentMode = new AgentSessionToolsTestRig(AgentOperationMode.Agent);
        var notRequired = await agentMode.CallRawAsync("approve", request);

        Assert.Multiple(() =>
        {
            Assert.That(Behavior(rejected), Is.EqualTo("deny"));
            Assert.That(Behavior(timedOut), Is.EqualTo("deny"));
            Assert.That(timedOut.StructuredContentJson, Does.Contain("expirou"));
            Assert.That(Behavior(bash), Is.EqualTo("deny"));
            Assert.That(rig.Confirmation.Requests, Has.Count.EqualTo(prompts), "Tool fora do plano não incomoda o usuário.");
            Assert.That(Behavior(invalid), Is.EqualTo("deny"));
            Assert.That(Behavior(unavailable), Is.EqualTo("deny"));
            Assert.That(notRequired.ErrorCode, Is.EqualTo("UnknownTool"), "Sem confirmações no plano, approve não existe no canal.");
            Assert.That(rig.Audit.Events, Is.All.Matches<AgentAuditEvent>(audit =>
                audit.ConnectionId is null && audit.NamespaceKind == AgentAuditNamespaceKind.None),
                "A decisão pode ser auditada, mas nunca registra acesso ou namespace MongoDB.");
        });
    }

    private static string? Behavior(AgentToolInvocationResult result)
    {
        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        return json.RootElement.GetProperty("behavior").GetString();
    }

    private static async Task<Guid[]> ListConnectionIdsAsync(AgentSessionToolsTestRig rig)
    {
        var result = await rig.CallRawAsync("list_connections", "{}");
        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        Assert.That(result.StructuredContentJson, Does.Not.Contain(AgentSessionToolsTestRig.UriCanary));
        using var json = JsonDocument.Parse(result.StructuredContentJson!);
        return [.. json.RootElement.GetProperty("connections").EnumerateArray().Select(item => item.GetProperty("id").GetGuid())];
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

    private sealed class LateProposalBaseReader(MemoryAgentFiles files) : IAgentBoundedFileReader
    {
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AgentFileReadResult Read(string fullPath, int maximumBytes) => files.Read(fullPath, maximumBytes);

        public async Task<AgentFileReadResult> ReadAsync(string fullPath, int maximumBytes, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            ReadEntered.TrySetResult();
            await ReleaseRead.Task; // Deliberately ignores cancellation to model a late completed bounded reader.
            return await files.ReadAsync(fullPath, maximumBytes, CancellationToken.None);
        }
    }

    private sealed class LinkIntroducedDuringRead(MemoryAgentFiles files) : IAgentBoundedFileReader, IAgentWorkspacePathProbe
    {
        private volatile bool _linkInstalled;
        public TaskCompletionSource ReadEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool ExternalFixtureWasRead { get; private set; }

        public AgentFileReadResult Read(string fullPath, int maximumBytes) => files.Read(fullPath, maximumBytes);

        public async Task<AgentFileReadResult> ReadAsync(string fullPath, int maximumBytes, CancellationToken cancellationToken)
        {
            ReadEntered.TrySetResult();
            await ReleaseRead.Task.WaitAsync(cancellationToken);
            if (_linkInstalled)
            {
                ExternalFixtureWasRead = true;
                return new AgentFileReadResult(AgentFileReadState.Read, Encoding.UTF8.GetBytes("outside secret fixture\n"));
            }

            return await files.ReadAsync(fullPath, maximumBytes, cancellationToken);
        }

        public void InstallLink() => _linkInstalled = true;

        public bool DirectoryExists(string fullPath) => files.DirectoryExists(fullPath);

        public bool FileExists(string fullPath) => files.FileExists(fullPath);

        public bool TraversesLink(string fullPath, string? workspaceRoot) =>
            _linkInstalled || files.TraversesLink(fullPath, workspaceRoot);
    }
}
