using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Application.SchemaLearning;
using EsilvaSoft.SlopStudio.Autocomplete.Core;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// P7-CLP-3-01: registry of the integrated Claude Code agent. Metadata stage, per-session exposure by turn plan and
/// connections, get_cached_schema without touching MongoDB, get_workspace_context, propose_file_edit (never writes to
/// disk) and the permission-prompt tool.
/// </summary>
[TestFixture]
public sealed class AgentSessionToolsTests
{
    private static readonly string[] MetadataStageTools =
        ["list_connections", "list_databases", "list_collections", "get_indexes"];

    private static readonly string[] OriginalHunkLines = ["linha 2"];
    private static readonly string[] ProposedHunkLines = ["linha dois", "linha 2b"];
    private static readonly string[] ConfirmationDecisions = ["Rejected", "ApprovedOnce", "ApprovedThisSession"];

    private static readonly string[] SessionTools =
        ["get_cached_schema", "get_workspace_context", "propose_file_edit", "approve"];

    [Test]
    public void MetadataStageReleasesMetadataAndSessionToolsButNoDocumentOrWriteTool()
    {
        using var rig = new AgentSessionToolsTestRig();

        Assert.Multiple(() =>
        {
            Assert.That(AgentToolExposure.StageOf("get_indexes"), Is.EqualTo(AgentToolExposureStage.Metadata));
            Assert.That(SessionTools.Select(AgentToolExposure.StageOf), Is.All.EqualTo(AgentToolExposureStage.Metadata));
            Assert.That(AgentToolExposure.StageOf("get_collection_schema"), Is.EqualTo(AgentToolExposureStage.DerivedReads));
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
    public async Task ProposeFileEditRegistersAProposalWithHunksAndNeverWritesToDisk()
    {
        using var rig = new AgentSessionToolsTestRig();
        const string original = "linha 1\nlinha 2\nlinha 3\nlinha 4\n";
        var file = rig.WriteFile("dados/clientes.json", original);
        var before = File.GetLastWriteTimeUtc(file);

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
            Assert.That(File.ReadAllText(file), Is.EqualTo(original), "Nada gravado em disco.");
            Assert.That(File.GetLastWriteTimeUtc(file), Is.EqualTo(before));
            Assert.That(receipt.RootElement.GetProperty("status").GetString(), Is.EqualTo("registered"));
            Assert.That(receipt.RootElement.GetProperty("proposalId").GetGuid(), Is.EqualTo(proposal.Id));
            Assert.That(receipt.RootElement.GetProperty("added").GetInt32(), Is.EqualTo(2));
            Assert.That(receipt.RootElement.GetProperty("removed").GetInt32(), Is.EqualTo(1));
            Assert.That(proposal.ConversationId, Is.EqualTo(rig.ConversationId));
            Assert.That(proposal.TargetPath, Is.EqualTo(Path.GetFullPath(file)));
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
            Assert.That(File.ReadAllText(file), Is.EqualTo("disco\r\n"));
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
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(rig.WorkspaceFolder)!, "fora.txt"), "x\n");

        var result = await rig.CallAsync("propose_file_edit", new { path, new_content = "novo\n" });

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorCode, Is.EqualTo(expected));
            Assert.That(rig.Sink.Proposals, Is.Empty);
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
            "{\"tool_name\":\"mcp__slopstudio__list_databases\",\"input\":" + input + ",\"tool_use_id\":\"toolu_1\",\"extra\":1}");

        Assert.That(result.Succeeded, Is.True, result.ErrorCode);
        var request = rig.Confirmation!.Requests.Single();
        Assert.Multiple(() =>
        {
            // Contract of --permission-prompt-tool (Claude Code CLI): {"behavior":"allow","updatedInput":<input>}.
            Assert.That(result.StructuredContentJson, Is.EqualTo("{\"behavior\":\"allow\",\"updatedInput\":" + input + "}"));
            Assert.That(request.ToolName, Is.EqualTo("mcp__slopstudio__list_databases"));
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
        const string input = "{\"tool_name\":\"mcp__slopstudio__list_databases\",\"input\":{\"connectionId\":\"9d1d9d7a-2b35-4e47-8f71-08a1a7e2a101\"}}";
        var first = await rig.CallRawAsync("approve", input);
        var second = await rig.CallRawAsync("approve", input);
        var changedArguments = await rig.CallRawAsync("approve",
            "{\"tool_name\":\"mcp__slopstudio__list_databases\",\"input\":{\"connectionId\":\"a81d9f50-1f7f-4ee6-bb56-10c8aeb34292\"}}");
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
        const string request = "{\"tool_name\":\"mcp__slopstudio__list_databases\",\"input\":{\"connectionId\":\"9d1d9d7a-2b35-4e47-8f71-08a1a7e2a101\"}}";
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
}
