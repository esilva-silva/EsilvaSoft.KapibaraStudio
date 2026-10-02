using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Mode × permissions × platform → turn plan (P7-CLP-1). The policy is the only place these rules live.</summary>
[TestFixture]
public sealed class AgentModePolicyTests
{
    private static readonly AgentPlatformFacts Windows = new(HasWorkspaceFolder: true, ProductToolsAvailable: true);
    private static readonly AgentPlatformFacts Linux = new(HasWorkspaceFolder: true, ProductToolsAvailable: false);

    private static readonly string[] MetadataTools =
        ["list_connections", "list_databases", "list_collections", "get_indexes", "get_workspace_context"];

    private static AgentProviderPermissions Consented() =>
        AgentProviderPermissions.Default("other-provider") with { ExternalDestinationConsentAt = DateTimeOffset.UnixEpoch };

    [Test]
    public void DefaultPermissionsHaveNoConsentAndConservativeData()
    {
        var permissions = AgentProviderPermissions.Default("claude-code");
        Assert.Multiple(() =>
        {
            Assert.That(permissions.HasExternalDestinationConsent, Is.False);
            Assert.That(permissions.KeepHistory, Is.True);
            Assert.That(permissions.DataSending.ExternalAttachments, Is.False);
            Assert.That(permissions.DataSending.InferredSchema, Is.False);
            Assert.That(permissions.DataSending.MongoDocuments, Is.False);
            Assert.That(permissions.EditProposals.OtherWorkspaceFiles, Is.False);
            Assert.That(permissions.Workspace.Exclusions, Is.EquivalentTo([".env", "*.pem", "*.key", "**/secrets/**"]));
            Assert.That(permissions.FormatVersion, Is.EqualTo(AgentProviderPermissions.CurrentFormatVersion));
        });
    }

    [Test]
    public void MongoDocumentToolsRequireCopilotOrClaudeOptInAndAreNeverEnabledByDefault()
    {
        var tools = AgentProductToolNames.ReadTools.Where(AgentProductToolNames.IsCopilotDocumentRead).ToArray();
        var permissions = Consented() with { EnabledReadTools = tools };
        var claudeDefault = AgentModePolicy.Plan(AgentOperationMode.Agent,
            AgentProviderPermissions.Default(AgentProviderIds.ClaudeCodeSubscription) with
            { ExternalDestinationConsentAt = DateTimeOffset.UnixEpoch }, Windows);
        var denied = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, Windows);
        Assert.Multiple(() =>
        {
            Assert.That(denied.ProductTools.Intersect(tools), Is.Empty);
            Assert.That(claudeDefault.ProductTools.Intersect(tools), Is.Empty,
                "Opt-in MongoDocuments e seleção de ferramenta começam desligados para Claude.");
        });

        var allowed = AgentModePolicy.Plan(AgentOperationMode.Agent,
            permissions with { ProviderId = AgentProviderIds.GitHubCopilotSubscription,
                DataSending = permissions.DataSending with { MongoDocuments = true } }, Windows);
        Assert.That(allowed.ProductTools, Is.SupersetOf(tools));

        var otherProvider = AgentModePolicy.Plan(AgentOperationMode.Agent,
            permissions with { ProviderId = "other-provider", DataSending = permissions.DataSending with { MongoDocuments = true } }, Windows);
        var claude = AgentModePolicy.Plan(AgentOperationMode.Agent,
            permissions with { ProviderId = AgentProviderIds.ClaudeCodeSubscription,
                DataSending = permissions.DataSending with { MongoDocuments = true } }, Windows);
        Assert.Multiple(() =>
        {
            Assert.That(otherProvider.ProductTools.Intersect(tools), Is.Empty, "Outros providers não herdam a capacidade.");
            Assert.That(claude.ProductTools, Is.SupersetOf(tools), "Claude precisa do opt-in persistente de documentos.");
            Assert.That(claude.ProductTools.Any(name => AgentToolExposure.WriteReleaseOf(name) != AgentWriteToolRelease.None), Is.False);
        });
    }

    [Test]
    public void LiveCollectionSchemaSamplingStaysClosedUntilDedicatedLocalConsentExists()
    {
        var permissions = Consented() with
        {
            ProviderId = AgentProviderIds.GitHubCopilotSubscription,
            EnabledReadTools = [AgentProductToolNames.GetCollectionSchema, AgentProductToolNames.GetCachedSchema],
            DataSending = new AgentDataSendingPermissions { InferredSchema = true }
        };

        foreach (var provider in new[] { AgentProviderIds.GitHubCopilotSubscription, AgentProviderIds.ClaudeCodeSubscription, "other-provider" })
        {
            var plan = AgentModePolicy.Plan(AgentOperationMode.Agent,
                permissions with { ProviderId = provider }, Windows);
            Assert.That(plan.ProductTools, Does.Not.Contain(AgentProductToolNames.GetCollectionSchema), provider);
            Assert.That(plan.ProductTools, Does.Contain(AgentProductToolNames.GetCachedSchema), provider);
        }
    }

    [TestCase(AgentOperationMode.Agent)]
    [TestCase(AgentOperationMode.Planning)]
    [TestCase(AgentOperationMode.Automatic)]
    [TestCase(AgentOperationMode.AskConfirmations)]
    public void WithoutConsentNothingIsExposed(AgentOperationMode mode)
    {
        var plan = AgentModePolicy.Plan(mode, AgentProviderPermissions.Default("claude-code"), Windows);
        Assert.Multiple(() =>
        {
            Assert.That(plan.IsBlocked, Is.True);
            Assert.That(plan.BlockReason, Is.EqualTo(AgentTurnBlockReason.ConsentMissing));
            Assert.That(plan.NativeTools, Is.Empty);
            Assert.That(plan.ProductTools, Is.Empty);
            Assert.That(plan.ProposalHandling, Is.EqualTo(AgentProposalHandling.Disabled));
        });
    }

    [Test]
    public void LocalDestinationDoesNotRequireExternalDestinationConsent()
    {
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent,
            AgentProviderPermissions.Default("local-cli"), Windows, requireExternalDestinationConsent: false);

        Assert.Multiple(() =>
        {
            Assert.That(plan.IsBlocked, Is.False);
            Assert.That(plan.ProductTools, Is.Not.Empty);
            Assert.That(plan.ProposalHandling, Is.EqualTo(AgentProposalHandling.ReviewRequired));
        });
    }

    [Test]
    public void AgentModeReadsAndProposesWithReview()
    {
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, Consented(), Windows);
        Assert.Multiple(() =>
        {
            Assert.That(plan.NativeTools, Is.EqualTo(["Read", "Glob"]), "Grep nativo fica fora enquanto houver exclusões.");
            Assert.That(plan.Notices, Does.Contain(AgentModePolicy.NoticeGrepDisabledByExclusions));
            Assert.That(plan.ProductTools, Is.EquivalentTo(MetadataTools.Append("propose_file_edit")));
            Assert.That(plan.ProposalHandling, Is.EqualTo(AgentProposalHandling.ReviewRequired));
            Assert.That(plan.RequiresPermissionPromptTool, Is.False);
            Assert.That(plan.NativeAskRules, Is.Empty);
            Assert.That(plan.NativeDenyRules, Does.Contain("Read(**/.env)").And.Contain("Read(**/secrets/**)"));
            Assert.That(plan.AllowedConnectionIds, Is.Null);
        });
    }

    [Test]
    public void PlanningNeverExposesProposals()
    {
        var plan = AgentModePolicy.Plan(AgentOperationMode.Planning, Consented(), Windows);
        Assert.Multiple(() =>
        {
            Assert.That(plan.ProductTools, Is.EquivalentTo(MetadataTools));
            Assert.That(plan.ProposalHandling, Is.EqualTo(AgentProposalHandling.Disabled));
            Assert.That(plan.NativeTools, Is.Not.Empty);
        });
    }

    [Test]
    public void AutomaticAppliesToBufferAndOverridesPersistedConfirmations()
    {
        var permissions = Consented() with { ConfirmationCategories = AgentConfirmationCategories.All };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Automatic, permissions, Windows);
        Assert.Multiple(() =>
        {
            Assert.That(plan.ProposalHandling, Is.EqualTo(AgentProposalHandling.AutoApplyToBuffer));
            Assert.That(plan.ConfirmationCategories, Is.EqualTo(AgentConfirmationCategories.None));
            Assert.That(plan.RequiresPermissionPromptTool, Is.False);
            Assert.That(plan.NativeAskRules, Is.Empty);
        });
    }

    [Test]
    public void NativeCommandsAndNetworkRequireApprovalWhileFileWritesStayMediatedInAutomaticMode()
    {
        var off = AgentModePolicy.Plan(AgentOperationMode.Automatic, Consented(), Windows);
        var enabled = Consented() with { NativeCommandExecution = true, NativeFileWrite = true, NativeNetwork = true };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Automatic, enabled, Windows);
        Assert.Multiple(() =>
        {
            Assert.That(off.NativeTools, Does.Not.Contain("Bash").And.Not.Contain("Edit").And.Not.Contain("WebFetch"));
            Assert.That(plan.NativeTools, Does.Contain("Bash").And.Not.Contain("Edit").And.Not.Contain("Write").And.Contain("WebFetch"));
            Assert.That(plan.NativeAskRules, Does.Contain("Bash").And.Not.Contain("Edit").And.Not.Contain("Write").And.Contain("WebFetch"));
            Assert.That(plan.ConfirmationCategories, Is.EqualTo(AgentConfirmationCategories.NativeCommand |
                AgentConfirmationCategories.NativeNetwork));
            Assert.That(plan.ProductTools, Does.Contain(AgentProductToolNames.ProposeFileEdit));
            Assert.That(plan.RequiresPermissionPromptTool, Is.True);
        });

        var noApprovalChannel = AgentModePolicy.Plan(AgentOperationMode.Automatic, enabled, Linux);
        Assert.That(noApprovalChannel.NativeTools, Is.EquivalentTo(["Read", "Glob"]),
            "Read-only tools remain usable without a prompt; commands and network calls do not.");
    }

    [Test]
    public void AskConfirmationsConfirmsEveryExposedToolIncludingReads()
    {
        var plan = AgentModePolicy.Plan(AgentOperationMode.AskConfirmations, Consented(), Windows);
        Assert.Multiple(() =>
        {
            Assert.That(plan.RequiresPermissionPromptTool, Is.True);
            Assert.That(plan.ConfirmationCategories, Is.EqualTo(AgentConfirmationCategories.MongoMetadataRead |
                AgentConfirmationCategories.WorkspaceContextRead | AgentConfirmationCategories.NativeFileRead |
                AgentConfirmationCategories.EditProposal));
            Assert.That(plan.NativeAskRules, Is.EqualTo(["Read", "Glob"]));
            Assert.That(plan.ProposalHandling, Is.EqualTo(AgentProposalHandling.ReviewRequired));
        });
    }

    [Test]
    public void AgentModeConfirmsOnlyPersistedCategoriesThatAreExposed()
    {
        var permissions = Consented() with
        {
            ConfirmationCategories = AgentConfirmationCategories.MongoMetadataRead,
            NativeFileRead = false,
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, Windows);
        Assert.Multiple(() =>
        {
            Assert.That(plan.ConfirmationCategories, Is.EqualTo(AgentConfirmationCategories.MongoMetadataRead));
            Assert.That(plan.RequiresPermissionPromptTool, Is.True);
            Assert.That(plan.NativeTools, Is.Empty);
            Assert.That(plan.NativeAskRules, Is.Empty);
        });

        var nativeOnly = AgentModePolicy.Plan(AgentOperationMode.Agent,
            Consented() with { ConfirmationCategories = AgentConfirmationCategories.NativeFileRead, NativeFileRead = false },
            Windows);
        Assert.That(nativeOnly.RequiresPermissionPromptTool, Is.False, "Categoria sem ferramenta exposta não pede confirmação.");
    }

    [Test]
    public void WithoutProductChannelNoProductToolAndNoUnconfirmableRead()
    {
        var agent = AgentModePolicy.Plan(AgentOperationMode.Agent, Consented(), Linux);
        var ask = AgentModePolicy.Plan(AgentOperationMode.AskConfirmations, Consented(), Linux);
        Assert.Multiple(() =>
        {
            Assert.That(agent.ProductTools, Is.Empty);
            Assert.That(agent.ProposalHandling, Is.EqualTo(AgentProposalHandling.Disabled));
            Assert.That(agent.NativeTools, Is.Not.Empty, "Leitura nativa sem confirmação não depende do canal MCP.");
            Assert.That(agent.Notices, Does.Contain(AgentModePolicy.NoticeProductToolsUnavailable));
            Assert.That(ask.NativeTools, Is.Empty, "Leitura que exige confirmação não pode rodar sem a ferramenta de aprovação.");
            Assert.That(ask.RequiresPermissionPromptTool, Is.False);
            Assert.That(ask.Notices, Does.Contain(AgentModePolicy.NoticeConfirmationUnavailable));
        });
    }

    [Test]
    public void NativeReadsNeedWorkspaceFolderAndValidExclusions()
    {
        var noFolder = AgentModePolicy.Plan(AgentOperationMode.Agent, Consented(), new AgentPlatformFacts(false, true));
        var invalid = AgentModePolicy.Plan(AgentOperationMode.Agent,
            Consented() with { Workspace = new AgentWorkspacePermissions { Exclusions = [".env", "a(b)"] } }, Windows);
        var noWorkspaceFiles = AgentModePolicy.Plan(AgentOperationMode.Agent,
            Consented() with { DataSending = new AgentDataSendingPermissions { WorkspaceFiles = false } }, Windows);
        Assert.Multiple(() =>
        {
            Assert.That(noFolder.NativeTools, Is.Empty);
            Assert.That(noFolder.Notices, Does.Contain(AgentModePolicy.NoticeNoWorkspaceFolder));
            Assert.That(invalid.NativeTools, Is.Empty);
            Assert.That(invalid.NativeDenyRules, Is.Empty);
            Assert.That(invalid.Notices, Does.Contain(AgentModePolicy.NoticeInvalidExclusion));
            Assert.That(noWorkspaceFiles.NativeTools, Is.Empty);
        });
    }

    [Test]
    public void DataSendingGatesSchemaAndWorkspaceContext()
    {
        var schema = AgentModePolicy.Plan(AgentOperationMode.Agent,
            Consented() with { DataSending = new AgentDataSendingPermissions { InferredSchema = true, TabMetadata = false } },
            Windows);
        Assert.Multiple(() =>
        {
            Assert.That(schema.ProductTools, Does.Contain("get_cached_schema"));
            Assert.That(schema.ProductTools, Does.Not.Contain("get_workspace_context"));
        });
    }

    [Test]
    public void ProposalsRequireAtLeastOnePermittedTarget()
    {
        var permissions = Consented() with
        {
            EditProposals = new AgentEditProposalPermissions { ActiveFile = false, OtherWorkspaceFiles = false },
        };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, Windows);
        Assert.Multiple(() =>
        {
            Assert.That(plan.ProductTools, Does.Not.Contain("propose_file_edit"));
            Assert.That(plan.ProposalHandling, Is.EqualTo(AgentProposalHandling.Disabled));
        });
    }

    [Test]
    public void SelectedConnectionScopeIsCarried()
    {
        var id = Guid.NewGuid();
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent,
            Consented() with { ConnectionScope = AgentConnectionScope.Selected, SelectedConnectionIds = [id, id, Guid.Empty] },
            Windows);
        Assert.That(plan.AllowedConnectionIds, Is.EqualTo([id]));
    }

    [Test]
    public void NoWriteOrUnknownToolIsEverExposed()
    {
        string[] allowed = ["Read", "Glob", "Grep", .. AgentProductToolNames.ReadTools, "propose_file_edit"];
        var permissions = Consented() with
        {
            EnabledReadTools = [.. AgentProductToolNames.ReadTools, "mongo_find", "insert_one", "delete_one", "Bash", "Edit"],
            DataSending = new AgentDataSendingPermissions { InferredSchema = true, ExternalAttachments = true },
            EditProposals = new AgentEditProposalPermissions { OtherWorkspaceFiles = true },
        };
        foreach (var mode in Enum.GetValues<AgentOperationMode>())
        {
            foreach (var facts in new[] { Windows, Linux, new AgentPlatformFacts(false, true) })
            {
                var plan = AgentModePolicy.Plan(mode, permissions, facts);
                Assert.That(plan.NativeTools.Concat(plan.ProductTools), Is.SubsetOf(allowed), $"{mode}/{facts}");
            }
        }
    }

    [Test]
    public void GrepIsExposedOnlyWithoutExclusions()
    {
        var permissions = Consented() with { Workspace = new AgentWorkspacePermissions { Exclusions = [] } };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, Windows);
        Assert.Multiple(() =>
        {
            Assert.That(plan.NativeTools, Is.EqualTo(["Read", "Glob", "Grep"]));
            Assert.That(plan.NativeDenyRules, Is.Empty);
            Assert.That(plan.Notices, Does.Not.Contain(AgentModePolicy.NoticeGrepDisabledByExclusions));
        });
    }

    [Test]
    public void NullExclusionListMeansTheDefaults()
    {
        var permissions = Consented() with { Workspace = new AgentWorkspacePermissions { Exclusions = null! } };
        var plan = AgentModePolicy.Plan(AgentOperationMode.Agent, permissions, Windows);
        Assert.Multiple(() =>
        {
            Assert.That(plan.IsBlocked, Is.False);
            Assert.That(plan.NativeDenyRules, Does.Contain("Read(**/.env)").And.Contain("Read(**/*.pem)"));
            Assert.That(plan.NativeTools, Does.Not.Contain("Grep"));
        });
    }

    private static IEnumerable<TestCaseData> MalformedPermissions()
    {
        yield return new TestCaseData(Consented() with { DataSending = null! }).SetName("DataSendingNulo");
        yield return new TestCaseData(Consented() with { Workspace = null! }).SetName("WorkspaceNulo");
        yield return new TestCaseData(Consented() with { EditProposals = null! }).SetName("EditProposalsNulo");
        yield return new TestCaseData(Consented() with { AutomaticContext = null! }).SetName("AutomaticContextNulo");
        yield return new TestCaseData(Consented() with { FormatVersion = AgentProviderPermissions.CurrentFormatVersion + 1 })
            .SetName("FormatoFuturo");
        yield return new TestCaseData(Consented() with { FormatVersion = 0 }).SetName("FormatoZero");
        yield return new TestCaseData(Consented() with { ConnectionScope = (AgentConnectionScope)9 }).SetName("EscopoIndefinido");
        yield return new TestCaseData(Consented() with { ConnectionScope = AgentConnectionScope.Selected, SelectedConnectionIds = null! })
            .SetName("SelecionadasNulas");
        yield return new TestCaseData(Consented() with { DefaultMode = (AgentOperationMode)7 }).SetName("ModoPadraoIndefinido");
        yield return new TestCaseData(Consented() with { ConfirmationCategories = (AgentConfirmationCategories)256 })
            .SetName("CategoriaIndefinida");
    }

    [TestCaseSource(nameof(MalformedPermissions))]
    public void MalformedPermissionsBlockEveryMode(AgentProviderPermissions permissions)
    {
        Assert.That(permissions.IsWellFormed, Is.False);
        foreach (var mode in Enum.GetValues<AgentOperationMode>())
        {
            var plan = AgentModePolicy.Plan(mode, permissions, Windows);
            Assert.Multiple(() =>
            {
                Assert.That(plan.BlockReason, Is.EqualTo(AgentTurnBlockReason.InvalidPermissions), mode.ToString());
                Assert.That(plan.NativeTools.Concat(plan.ProductTools), Is.Empty, mode.ToString());
            });
        }
    }

    [Test]
    public void UndefinedModeIsRejected() =>
        Assert.That(() => AgentModePolicy.Plan((AgentOperationMode)42, Consented(), Windows),
            Throws.TypeOf<ArgumentOutOfRangeException>());
}
