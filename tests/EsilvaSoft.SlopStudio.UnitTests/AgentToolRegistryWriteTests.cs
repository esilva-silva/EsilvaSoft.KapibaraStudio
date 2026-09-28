using System.Text.Json;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using static EsilvaSoft.SlopStudio.UnitTests.AgentWriteTestDoubles;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// Lote 10 registry side (AC-12, part of AC-19): unitary writes and index tools behind a closed exposure, a durable
/// intent, an operation-bound human approval and a single dispatch. The write source is a fake; integrity against a
/// real MongoDB (atomic precondition, RBAC, concurrency) belongs to the Mongo suite and is not claimed here.
/// </summary>
[TestFixture]
public sealed class AgentToolRegistryWriteTests
{
    private static readonly string[] WriteTools =
        ["insert_one", "update_one", "delete_one", "create_index", "drop_index"];

    private static readonly AgentAuditOutcome[] IntentThenSucceeded = [AgentAuditOutcome.Intent, AgentAuditOutcome.Succeeded];
    private static readonly AgentAuditOutcome[] IntentThenDenied = [AgentAuditOutcome.Intent, AgentAuditOutcome.Denied];
    private static readonly AgentAuditOutcome[] IntentThenUncertain = [AgentAuditOutcome.Intent, AgentAuditOutcome.Uncertain];
    private static readonly AgentAuditOutcome[] IntentOnly = [AgentAuditOutcome.Intent];
    private static readonly string[] OnlyInsert = ["insert_one"];

    [Test]
    public async Task WritesAreClosedByDefaultEvenWithEverythingComposed()
    {
        var fixture = new Fixture(AgentToolExposure.Through(AgentToolExposureStage.DerivedReads));

        var result = await fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"));

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Registry.GetDescriptors().Select(item => item.Name), Has.None.AnyOf(WriteTools));
            Assert.That(WriteTools.Select(fixture.Registry.GetInputSchemaJson), Is.All.Null);
            Assert.That(result.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(fixture.Audit.Events, Is.Empty);
            Assert.That(fixture.Prompt.Prompts, Is.Empty);
            Assert.That(fixture.Source.Reads + fixture.Source.Writes, Is.Zero);
            Assert.That(AgentToolExposure.Through(AgentToolExposureStage.UnitaryWrites).Exposes("insert_one"), Is.False);
        });
        Assert.Throws<InvalidOperationException>(() =>
            AgentToolExposure.Through(AgentToolExposureStage.DerivedReads).WithWriteTools(AgentWriteToolRelease.InsertOne));
    }

    [Test]
    public async Task EachWriteToolIsReleasedSeparately()
    {
        var fixture = new Fixture(AgentToolExposure.Through(AgentToolExposureStage.UnitaryWrites)
            .WithWriteTools(AgentWriteToolRelease.InsertOne));

        var update = await fixture.InvokeAsync("update_one", Update("1", "{\"$set\":{\"v\":2}}"));

        Assert.Multiple(() =>
        {
            Assert.That(fixture.Registry.GetDescriptors().Select(item => item.Name).Intersect(WriteTools),
                Is.EqualTo(OnlyInsert));
            Assert.That(update.ErrorCode, Is.EqualTo("UnknownTool"));
            Assert.That(fixture.Source.Reads + fixture.Source.Writes, Is.Zero);
        });
    }

    [Test]
    public void WriteWithoutApprovalAuthorityOrSourceIsNotDiscoverable()
    {
        var withoutApprovals = new Fixture(AllWrites(), composeApprovals: false);
        var withoutSource = new Fixture(AllWrites(), composeSource: false);

        Assert.That(withoutApprovals.Registry.GetDescriptors().Select(item => item.Name), Has.None.AnyOf(WriteTools));
        Assert.That(withoutSource.Registry.GetDescriptors().Select(item => item.Name), Has.None.AnyOf(WriteTools));
    }

    [Test]
    public void WriteDescriptorsDeclareRiskPermissionAndClosedSchemas()
    {
        var registry = new Fixture(AllWrites()).Registry;
        var expected = new Dictionary<string, (AgentToolRisk, AgentPermission)>
        {
            ["insert_one"] = (AgentToolRisk.Write, AgentPermission.InsertDocuments),
            ["update_one"] = (AgentToolRisk.Write, AgentPermission.UpdateDocuments),
            ["delete_one"] = (AgentToolRisk.Destructive, AgentPermission.DeleteDocuments),
            ["create_index"] = (AgentToolRisk.Write, AgentPermission.CreateIndexes),
            ["drop_index"] = (AgentToolRisk.Destructive, AgentPermission.DropIndexes)
        };
        foreach (var (name, (risk, permission)) in expected)
        {
            var descriptor = registry.FindDescriptor(name)!;
            Assert.That(descriptor.Risk, Is.EqualTo(risk), name);
            Assert.That(descriptor.RequiredPermissions, Is.EqualTo(new[] { permission }), name);
            using var input = JsonDocument.Parse(registry.GetInputSchemaJson(name)!);
            Assert.That(input.RootElement.GetProperty("additionalProperties").GetBoolean(), Is.False, name);
            Assert.That(input.RootElement.GetProperty("properties").TryGetProperty("approved", out _), Is.False, name);
            Assert.That(registry.GetOutputSchemaJson(name), Is.Not.Null, name);
        }
    }

    [Test]
    public async Task ApprovedInsertIsDispatchedOnceWithFixedIdAndDurableIntentFirst()
    {
        var fixture = new Fixture(AllWrites());
        AgentAuditEvent[]? ledgerAtPrompt = null;
        fixture.Prompt.Handler = (_, _) =>
        {
            ledgerAtPrompt = [.. fixture.Audit.Events];
            return Task.FromResult<AgentApprovalOutcome?>(AgentApprovalOutcome.Granted);
        };

        var result = await fixture.InvokeAsync("insert_one", Insert("{\"name\":\"a\",\"big\":{\"$numberLong\":\"9007199254740993\"}}"));

        var request = (AgentMongoInsertRequest)fixture.Source.Requests.Single();
        var prompt = fixture.Prompt.Prompts.Single();
        using var output = JsonDocument.Parse(result.StructuredContentJson!);
        using var sent = JsonDocument.Parse(request.DocumentEjson);
        var fixedId = sent.RootElement.GetProperty("_id").GetRawText();
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.ErrorCode);
            Assert.That(fixture.Source.Writes, Is.EqualTo(1));
            Assert.That(ledgerAtPrompt!.Select(item => item.Outcome), Is.EqualTo(IntentOnly));
            Assert.That(ledgerAtPrompt![0].ApprovalState, Is.EqualTo(AgentAuditApprovalState.Pending));
            Assert.That(fixedId, Does.StartWith("{\"$oid\":\""));
            Assert.That(sent.RootElement.GetProperty("big").GetRawText(), Is.EqualTo("{\"$numberLong\":\"9007199254740993\"}"));
            Assert.That(prompt.Details.Change, Is.EqualTo(request.DocumentEjson));
            Assert.That(prompt.Details.Target, Is.EqualTo("_id " + fixedId));
            Assert.That(prompt.Details.ConnectionLabel, Is.EqualTo("Escrita"));
            Assert.That(request.Approval, Is.Not.Null);
            Assert.That(request.Approval!.ApprovalId.ToString("N"), Is.EqualTo(prompt.ApprovalId.Value));
            Assert.That(request.SourceGenerationId, Is.EqualTo(fixture.Profiles.Profile.SourceGenerationId));
            Assert.That(output.RootElement.GetProperty("insertedIdEjson").GetString(), Is.EqualTo(fixedId));
            Assert.That(fixture.Audit.Events.Select(item => item.Outcome), Is.EqualTo(IntentThenSucceeded));
            Assert.That(fixture.Audit.Events[1].ApprovalState, Is.EqualTo(AgentAuditApprovalState.ApprovedOnce));
            Assert.That(fixture.Audit.Events[1].ApprovalId, Is.EqualTo(fixture.Audit.Events[0].ApprovalId));
            Assert.That(fixture.Audit.Events.Select(item => item.NamespaceKind),
                Is.All.EqualTo(AgentAuditNamespaceKind.Pseudonym));
        });
    }

    [Test]
    public async Task UpdatePreviewReadsBeforeStateWithoutWritingAndBindsItToTheDispatch()
    {
        var fixture = new Fixture(AllWrites());
        var writesAtPrompt = -1;
        fixture.Prompt.Handler = (_, _) =>
        {
            writesAtPrompt = fixture.Source.Writes;
            return Task.FromResult<AgentApprovalOutcome?>(AgentApprovalOutcome.Granted);
        };

        var result = await fixture.InvokeAsync("update_one", Update("1", "{\"$set\":{\"v\":2}}"));

        var request = fixture.Source.Requests.OfType<AgentMongoUpdateRequest>().Single();
        var target = fixture.Source.Requests.OfType<AgentMongoWriteTarget>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.Succeeded, Is.True, result.ErrorCode);
            Assert.That(writesAtPrompt, Is.Zero);
            Assert.That(fixture.Prompt.Prompts.Single().BeforeEjson, Is.EqualTo("{\"_id\":1,\"v\":1}"));
            Assert.That(target.SourceGenerationId, Is.EqualTo(fixture.Profiles.Profile.SourceGenerationId));
            Assert.That(request.ExpectedStateHash, Is.EqualTo(StateHash));
            Assert.That(request.ExpectedStateEjson, Is.EqualTo("{\"_id\":1,\"v\":1}"));
            Assert.That(request.UpdateEjson, Is.EqualTo("{\"$set\":{\"v\":2}}"));
        });
    }

    [Test]
    public async Task MissingTargetIsNotFoundBeforeAnyApproval()
    {
        var fixture = new Fixture(AllWrites());
        fixture.Source.Snapshot = new AgentMongoWriteSnapshot(false, null, null, true, false);

        var result = await fixture.InvokeAsync("delete_one", Delete("1"));

        Assert.That(result.ErrorCode, Is.EqualTo("NotFound"));
        Assert.That(fixture.Prompt.Prompts, Is.Empty);
        Assert.That(fixture.Source.Writes, Is.Zero);
        Assert.That(fixture.Audit.Events.Select(item => item.Outcome), Is.EqualTo(IntentThenDenied));
    }

    [Test]
    public async Task ProductionFailClosedPromptWritesNothing()
    {
        var fixture = new Fixture(AllWrites(), prompt: new FailClosedAgentWriteApprovalPrompt());

        var result = await fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"));

        Assert.That(result.ErrorCode, Is.EqualTo("ApprovalUnavailable"));
        Assert.That(fixture.Source.Writes, Is.Zero);
        Assert.That(fixture.Audit.Events.Select(item => item.Outcome), Is.EqualTo(IntentThenDenied));
        Assert.That(fixture.Audit.Events[1].ApprovalState, Is.EqualTo(AgentAuditApprovalState.Pending));
    }

    [Test]
    public async Task RejectedAndExpiredApprovalsWriteNothing()
    {
        var rejecting = new Fixture(AllWrites());
        rejecting.Prompt.Handler = static (_, _) => Task.FromResult<AgentApprovalOutcome?>(AgentApprovalOutcome.Denied);
        var expiring = new Fixture(AllWrites(), approvalTimeout: TimeSpan.FromMilliseconds(100));
        expiring.Prompt.Handler = static async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return AgentApprovalOutcome.Granted;
        };

        var rejected = await rejecting.InvokeAsync("insert_one", Insert("{\"v\":1}"));
        var expired = await expiring.InvokeAsync("insert_one", Insert("{\"v\":1}"));

        Assert.Multiple(() =>
        {
            Assert.That(rejected.ErrorCode, Is.EqualTo("ApprovalRejected"));
            Assert.That(rejecting.Audit.Events[1].ApprovalState, Is.EqualTo(AgentAuditApprovalState.Rejected));
            Assert.That(expired.ErrorCode, Is.EqualTo("ApprovalExpired"));
            Assert.That(expiring.Audit.Events[1].ApprovalState, Is.EqualTo(AgentAuditApprovalState.Expired));
            Assert.That(rejecting.Source.Writes + expiring.Source.Writes, Is.Zero);
        });
    }

    [TestCase("{\"connectionId\":\"CONN\",\"database\":\"app\",\"collection\":\"items\",\"documentEjson\":\"{\\\"v\\\":1}\",\"approved\":true}")]
    [TestCase("{\"connectionId\":\"CONN\",\"database\":\"app\",\"collection\":\"items\",\"documentEjson\":\"{\\\"v\\\":1}\",\"approval\":{\"granted\":true}}")]
    public async Task ApprovalClaimsInArgumentsAreRejectedBeforeAnyPrompt(string template)
    {
        var fixture = new Fixture(AllWrites());

        var result = await fixture.InvokeAsync("insert_one",
            template.Replace("CONN", fixture.Profiles.Profile.Id.ToString("D"), StringComparison.Ordinal));

        Assert.That(result.ErrorCode, Is.EqualTo("InvalidArguments"));
        Assert.That(fixture.Prompt.Prompts, Is.Empty);
        Assert.That(fixture.Source.Reads + fixture.Source.Writes, Is.Zero);
    }

    [TestCase("insert_one", "documentEjson", "{\"v\":{\"$where\":\"sleep(1)\"}}")]
    [TestCase("insert_one", "documentEjson", "{\"v\":\"ok\",\"$set\":{\"x\":1}}")]
    [TestCase("insert_one", "documentEjson", "{\"v\":ENV.get(\"X\")}")]
    [TestCase("insert_one", "documentEjson", "{\"v\":{\"$function\":{\"body\":\"x\"}}}")]
    [TestCase("update_one", "updateEjson", "{\"$setOnInsert\":{\"v\":1}}")]
    [TestCase("update_one", "updateEjson", "[{\"$set\":{\"v\":1}}]")]
    [TestCase("update_one", "updateEjson", "{\"$set\":{\"_id\":2}}")]
    [TestCase("update_one", "updateEjson", "{\"$set\":{\"a.$[x]\":2}}")]
    [TestCase("update_one", "updateEjson", "{\"v\":2}")]
    [TestCase("update_one", "updateEjson", "{\"$set\":{\"v\":{\"$function\":{}}}}")]
    [TestCase("update_one", "idEjson", "{\"$gt\":0}")]
    [TestCase("create_index", "keysEjson", "{\"$**\":1}")]
    [TestCase("create_index", "keysEjson", "{\"a\":\"text\"}")]
    [TestCase("create_index", "name", "_id_")]
    [TestCase("drop_index", "indexName", "_id_")]
    [TestCase("drop_index", "indexName", "*")]
    public async Task NonLiteralOrForbiddenInputsAreRejectedBeforeAnySource(string tool, string field, string value)
    {
        var fixture = new Fixture(AllWrites());
        var arguments = new Dictionary<string, object>
        {
            ["connectionId"] = fixture.Profiles.Profile.Id, ["database"] = "app", ["collection"] = "items"
        };
        switch (tool)
        {
            case "insert_one": arguments["documentEjson"] = "{\"v\":1}"; break;
            case "update_one": arguments["idEjson"] = "1"; arguments["updateEjson"] = "{\"$set\":{\"v\":1}}"; break;
            case "create_index": arguments["keysEjson"] = "{\"v\":1}"; break;
            case "drop_index": arguments["indexName"] = "v_1"; break;
        }
        arguments[field] = value;

        var result = await fixture.InvokeAsync(tool, JsonSerializer.Serialize(arguments));

        Assert.That(result.ErrorCode, Is.EqualTo("InvalidArguments"));
        Assert.That(fixture.Prompt.Prompts, Is.Empty);
        Assert.That(fixture.Source.Reads + fixture.Source.Writes, Is.Zero);
    }

    [TestCase("admin")]
    [TestCase("config")]
    [TestCase("local")]
    public async Task SystemDatabasesAreNotWritable(string database)
    {
        var fixture = new Fixture(AllWrites());

        var result = await fixture.InvokeAsync("insert_one", JsonSerializer.Serialize(new
        {
            connectionId = fixture.Profiles.Profile.Id, database, collection = "items", documentEjson = "{\"v\":1}"
        }));

        Assert.That(result.ErrorCode, Is.EqualTo("InvalidArguments"));
        Assert.That(fixture.Source.Writes, Is.Zero);
    }

    [Test]
    public async Task ExternalMcpPrincipalNeverWritesEvenWithGrants()
    {
        var fixture = new Fixture(AllWrites());
        var destination = AgentOutputDestination.McpExternal("claude-code");
        fixture.Policies.Set(OtherPrincipalId, 3,
            Grants(OtherPrincipalId, fixture.Profiles.Profile, destination, AgentPermission.InsertDocuments));

        var result = await fixture.Registry.InvokeAsync(
            new AgentPrincipal(OtherPrincipalId, AgentPrincipalOrigin.External, 3),
            new AgentInvocationContext("claude-code", Guid.NewGuid(), SessionId, TurnId), destination,
            AgentOutputDataScope.DocumentValues, "insert_one", Insert("{\"v\":1}"));

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(fixture.Audit.Events, Is.Empty);
        Assert.That(fixture.Prompt.Prompts, Is.Empty);
        Assert.That(fixture.Source.Reads + fixture.Source.Writes, Is.Zero);
    }

    [Test]
    public async Task UnavailableLedgerDeniesBeforeProfilesPromptOrSource()
    {
        var fixture = new Fixture(AllWrites());
        fixture.Audit.Fail = static _ => true;

        var result = await fixture.InvokeAsync("update_one", Update("1", "{\"$set\":{\"v\":2}}"));

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(fixture.Prompt.Prompts, Is.Empty);
        Assert.That(fixture.Source.Reads + fixture.Source.Writes, Is.Zero);
    }

    [Test]
    public async Task ReadOnlyConnectionDeniesEveryWriteBeforeThePrompt()
    {
        var fixture = new Fixture(AllWrites());
        fixture.Profiles.Profile = fixture.Profiles.Profile with { IsReadOnly = true };

        foreach (var (tool, arguments) in new[]
                 {
                     ("insert_one", Insert("{\"v\":1}")), ("create_index", Index("{\"v\":1}"))
                 })
        {
            var result = await fixture.InvokeAsync(tool, arguments);
            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"), tool);
        }
        Assert.That(fixture.Prompt.Prompts, Is.Empty);
        Assert.That(fixture.Source.Reads + fixture.Source.Writes, Is.Zero);
    }

    [Test]
    public async Task MissingGrantForTheWritePermissionDenies()
    {
        var fixture = new Fixture(AllWrites());

        var result = await fixture.InvokeAsync("drop_index", JsonSerializer.Serialize(new
        {
            connectionId = fixture.Profiles.Profile.Id, database = "app", collection = "items", indexName = "v_1"
        }));

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(fixture.Prompt.Prompts, Is.Empty);
        Assert.That(fixture.Source.Writes, Is.Zero);
    }

    [Test]
    public async Task PolicyRevocationDuringApprovalDeniesTheApprovedWrite()
    {
        var fixture = new Fixture(AllWrites());
        fixture.Prompt.Handler = (_, _) =>
        {
            fixture.Policies.Set(PrincipalId, 4, []);
            return Task.FromResult<AgentApprovalOutcome?>(AgentApprovalOutcome.Granted);
        };

        var result = await fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"));

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(fixture.Source.Writes, Is.Zero);
        Assert.That(fixture.Audit.Events.Select(item => item.Outcome), Is.EqualTo(IntentThenDenied));
        Assert.That(fixture.Audit.Events[1].ApprovalState, Is.EqualTo(AgentAuditApprovalState.ApprovedOnce));
    }

    [Test]
    public async Task ProfileGenerationOrReadOnlyChangeDuringApprovalDeniesTheApprovedWrite()
    {
        foreach (var change in new Func<ConnectionProfile, ConnectionProfile>[]
                 {
                     profile => profile with { SourceGenerationId = Guid.NewGuid() },
                     profile => profile with { IsReadOnly = true }
                 })
        {
            var fixture = new Fixture(AllWrites());
            fixture.Prompt.Handler = (_, _) =>
            {
                fixture.Profiles.Profile = change(fixture.Profiles.Profile);
                return Task.FromResult<AgentApprovalOutcome?>(AgentApprovalOutcome.Granted);
            };

            var result = await fixture.InvokeAsync("update_one", Update("1", "{\"$set\":{\"v\":2}}"));

            Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
            Assert.That(fixture.Source.Writes, Is.Zero);
        }
    }

    [Test]
    public async Task RevokedChannelDuringApprovalDeniesTheApprovedWrite()
    {
        var fixture = new Fixture(AllWrites());
        fixture.Prompt.Handler = (_, _) =>
        {
            fixture.Authority.IsCurrent = static _ => false;
            return Task.FromResult<AgentApprovalOutcome?>(AgentApprovalOutcome.Granted);
        };

        var result = await fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"));

        Assert.That(result.ErrorCode, Is.EqualTo("PermissionDenied"));
        Assert.That(fixture.Source.Writes, Is.Zero);
    }

    [Test]
    public async Task CrashAfterSendIsOutcomeUnknownWithoutRetry()
    {
        var fixture = new Fixture(AllWrites());
        fixture.Source.OnWrite = static _ => throw new IOException("connection reset after send");

        var result = await fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"));

        Assert.That(result.ErrorCode, Is.EqualTo("OutcomeUnknown"));
        Assert.That(fixture.Source.Writes, Is.EqualTo(1));
        Assert.That(fixture.Audit.Events.Select(item => item.Outcome), Is.EqualTo(IntentThenUncertain));
        Assert.That(fixture.Audit.Events[1].DecisionReason, Is.EqualTo(AgentAuditDecisionReason.OutcomeUncertain));
    }

    [Test]
    public async Task SourceReportedUncertaintyAndInvalidAppliedResultAreOutcomeUnknown()
    {
        foreach (var reported in new[]
                 {
                     new AgentMongoWriteResult(AgentMongoWriteStatus.OutcomeUnknown, 0, null, true),
                     new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 2, null, true),
                     new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1, null, false)
                 })
        {
            var fixture = new Fixture(AllWrites());
            fixture.Source.OnWrite = _ => Task.FromResult(reported);

            var result = await fixture.InvokeAsync("delete_one", Delete("1"));

            Assert.That(result.ErrorCode, Is.EqualTo("OutcomeUnknown"), reported.ToString());
            Assert.That(fixture.Source.Writes, Is.EqualTo(1));
        }
    }

    [Test]
    public async Task CancellationAfterSendReportsOutcomeUnknownInsteadOfRollback()
    {
        var fixture = new Fixture(AllWrites());
        var sent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Source.OnWrite = async token =>
        {
            sent.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1, null, true);
        };
        using var cancellation = new CancellationTokenSource();

        var invocation = fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"), cancellation.Token);
        await sent.Task;
        await cancellation.CancelAsync();
        var result = await invocation;

        Assert.That(result.ErrorCode, Is.EqualTo("OutcomeUnknown"));
        Assert.That(fixture.Source.Writes, Is.EqualTo(1));
        Assert.That(fixture.Audit.Events.Select(item => item.Outcome), Is.EqualTo(IntentThenUncertain));
    }

    [Test]
    public async Task CancellationBeforeApprovalWritesNothingAndIsAudited()
    {
        var fixture = new Fixture(AllWrites());
        using var cancellation = new CancellationTokenSource();
        fixture.Prompt.Handler = async (_, token) =>
        {
            await cancellation.CancelAsync();
            await Task.Delay(Timeout.Infinite, token);
            return AgentApprovalOutcome.Granted;
        };

        Assert.CatchAsync<OperationCanceledException>(() =>
            fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"), cancellation.Token));
        await Task.Yield();

        Assert.That(fixture.Source.Writes, Is.Zero);
        Assert.That(fixture.Audit.Events.Select(item => item.Outcome),
            Is.EqualTo(new[] { AgentAuditOutcome.Intent, AgentAuditOutcome.Cancelled }));
    }

    [Test]
    public async Task LostTerminalAuditAfterAppliedWriteIsReportedAndLeavesIntentPending()
    {
        var fixture = new Fixture(AllWrites());
        fixture.Audit.Fail = static entry => entry.Outcome != AgentAuditOutcome.Intent;

        var result = await fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"));

        Assert.That(result.ErrorCode, Is.EqualTo("AppliedAuditPending"));
        Assert.That(fixture.Source.Writes, Is.EqualTo(1));
        Assert.That(fixture.Audit.Events.Select(item => item.Outcome), Is.EqualTo(IntentOnly));
    }

    [TestCase(AgentMongoWriteStatus.Conflict, "WriteConflict")]
    [TestCase(AgentMongoWriteStatus.NotFound, "NotFound")]
    [TestCase(AgentMongoWriteStatus.PreconditionUnavailable, "PreconditionUnavailable")]
    [TestCase(AgentMongoWriteStatus.Forbidden, "WriteForbidden")]
    [TestCase(AgentMongoWriteStatus.NotSent, "WriteNotSent")]
    [TestCase(AgentMongoWriteStatus.InvalidRequest, "WriteRejected")]
    public async Task DefiniteNonAppliedStatusesAreFailuresWithoutRetry(AgentMongoWriteStatus status, string code)
    {
        var fixture = new Fixture(AllWrites());
        fixture.Source.OnWrite = _ => Task.FromResult(new AgentMongoWriteResult(status, 0, null, true));

        var result = await fixture.InvokeAsync("update_one", Update("1", "{\"$set\":{\"v\":2}}"));

        Assert.That(result.ErrorCode, Is.EqualTo(code));
        Assert.That(fixture.Source.Writes, Is.EqualTo(1));
        Assert.That(fixture.Audit.Events[1].Outcome, Is.EqualTo(AgentAuditOutcome.Failed));
    }

    [Test]
    public async Task RepeatedCallNeedsItsOwnApprovalAndNeverReusesTheTicket()
    {
        var fixture = new Fixture(AllWrites());

        var first = await fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"));
        var second = await fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"));

        var approvals = fixture.Prompt.Prompts.Select(prompt => prompt.ApprovalId).ToArray();
        var requests = fixture.Source.Requests.OfType<AgentMongoInsertRequest>().ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(first.Succeeded && second.Succeeded, Is.True);
            Assert.That(approvals, Has.Length.EqualTo(2));
            Assert.That(approvals.Distinct().Count(), Is.EqualTo(2));
            Assert.That(requests[0].Approval!.ApprovalId, Is.Not.EqualTo(requests[1].Approval!.ApprovalId));
            Assert.That(requests[0].DocumentEjson, Is.Not.EqualTo(requests[1].DocumentEjson), "cada inserção fixa seu _id");
        });
    }

    [Test]
    public async Task SecondWriteOfTheSameSessionIsBusyWhileTheFirstAwaitsApproval()
    {
        var fixture = new Fixture(AllWrites());
        var release = new TaskCompletionSource<AgentApprovalOutcome?>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Prompt.Handler = (_, _) => release.Task;

        var first = fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"));
        await WaitUntilAsync(() => fixture.Prompt.Prompts.Count == 1);
        var second = await fixture.InvokeAsync("insert_one", Insert("{\"v\":2}"));
        release.SetResult(AgentApprovalOutcome.Granted);
        var firstResult = await first;

        Assert.That(second.ErrorCode, Is.EqualTo("Busy"));
        Assert.That(firstResult.Succeeded, Is.True);
        Assert.That(fixture.Source.Writes, Is.EqualTo(1));
    }

    [Test]
    public async Task CreateAndDropIndexUseMetadataScopeAndBindTheDefinition()
    {
        var fixture = new Fixture(AllWrites());
        fixture.Source.Snapshot = new AgentMongoWriteSnapshot(true, "{\"v\":1,\"key\":{\"v\":1},\"name\":\"v_1\"}",
            StateHash, true, false);
        fixture.Source.OnWrite = static _ => Task.FromResult(
            new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1, "\"v_1\"", true));
        fixture.Policies.Set(PrincipalId, 3,
            [.. Grants(PrincipalId, fixture.Profiles.Profile, AgentOutputDestination.Local(), AgentPermission.CreateIndexes,
                AgentOutputDataScope.Metadata),
             .. Grants(PrincipalId, fixture.Profiles.Profile, AgentOutputDestination.Local(), AgentPermission.DropIndexes,
                AgentOutputDataScope.Metadata)]);

        var created = await fixture.Registry.InvokeAsync(fixture.Principal, Context(), AgentOutputDestination.Local(),
            AgentOutputDataScope.Metadata, "create_index", Index("{\"v\":1}"));
        var dropped = await fixture.Registry.InvokeAsync(fixture.Principal, Context(), AgentOutputDestination.Local(),
            AgentOutputDataScope.Metadata, "drop_index", JsonSerializer.Serialize(new
            {
                connectionId = fixture.Profiles.Profile.Id, database = "app", collection = "items", indexName = "v_1"
            }));

        var drop = fixture.Source.Requests.OfType<AgentMongoDropIndexRequest>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(created.Succeeded, Is.True, created.ErrorCode);
            Assert.That(JsonDocument.Parse(created.StructuredContentJson!).RootElement.GetProperty("indexName").GetString(),
                Is.EqualTo("v_1"));
            Assert.That(dropped.Succeeded, Is.True, dropped.ErrorCode);
            Assert.That(drop.ExpectedStateHash, Is.EqualTo(StateHash));
            Assert.That(fixture.Prompt.Prompts[1].BeforeEjson, Does.Contain("\"name\":\"v_1\""));
        });
    }

    [Test]
    public async Task RealLiteDbLedgerAcceptsEveryWriteSequenceAndKeepsOnlyTheUnrecordedOnePending()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slop-agent-write-ledger-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var repository = new Infrastructure.LiteDbConnectionProfileRepository(
                Path.Combine(directory, "workspace.db"));
            IAgentAuditRepository ledger = repository;
            var failTerminals = false;
            var audit = new ForwardingAudit(ledger, () => failTerminals);
            var fixture = new Fixture(AllWrites(), audit: audit);
            var verdicts = new Queue<AgentApprovalOutcome?>(
                [AgentApprovalOutcome.Granted, AgentApprovalOutcome.Denied, AgentApprovalOutcome.Granted, null,
                 AgentApprovalOutcome.Granted]);
            fixture.Prompt.Handler = (_, _) => Task.FromResult(verdicts.Dequeue());
            var outcomes = new Queue<Func<CancellationToken, Task<AgentMongoWriteResult>>>(
            [
                static _ => Task.FromResult(new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1, null, true)),
                static _ => throw new IOException("reset after send"),
                static _ => Task.FromResult(new AgentMongoWriteResult(AgentMongoWriteStatus.Applied, 1, null, true))
            ]);
            fixture.Source.OnWrite = token => outcomes.Dequeue()(token);

            var applied = await fixture.InvokeAsync("insert_one", Insert("{\"v\":1}"));
            var rejected = await fixture.InvokeAsync("insert_one", Insert("{\"v\":2}"));
            var uncertain = await fixture.InvokeAsync("update_one", Update("1", "{\"$set\":{\"v\":2}}"));
            var unavailable = await fixture.InvokeAsync("delete_one", Delete("1"));
            var invalid = await fixture.InvokeAsync("insert_one", "{\"approved\":true}");
            var pendingBefore = await ledger.GetPendingAsync();
            failTerminals = true;
            var auditLost = await fixture.InvokeAsync("insert_one", Insert("{\"v\":3}"));
            var recent = await ledger.GetRecentAsync(100);
            var pending = await ledger.GetPendingAsync();

            Assert.Multiple(() =>
            {
                Assert.That(applied.Succeeded, Is.True, applied.ErrorCode);
                Assert.That(rejected.ErrorCode, Is.EqualTo("ApprovalRejected"));
                Assert.That(uncertain.ErrorCode, Is.EqualTo("OutcomeUnknown"));
                Assert.That(unavailable.ErrorCode, Is.EqualTo("ApprovalUnavailable"));
                Assert.That(invalid.ErrorCode, Is.EqualTo("InvalidArguments"));
                Assert.That(pendingBefore, Is.Empty, "cada intenção de escrita recebeu seu desfecho no ledger real");
                Assert.That(auditLost.ErrorCode, Is.EqualTo("AppliedAuditPending"));
                Assert.That(pending, Has.Count.EqualTo(1));
                Assert.That(pending[0].ToolName, Is.EqualTo("insert_one"));
                Assert.That(recent.Where(item => item.Outcome != AgentAuditOutcome.Intent).Select(item => item.Outcome),
                    Is.EquivalentTo(new[]
                    {
                        AgentAuditOutcome.Succeeded, AgentAuditOutcome.Denied, AgentAuditOutcome.Uncertain,
                        AgentAuditOutcome.Denied, AgentAuditOutcome.Denied
                    }));
                Assert.That(fixture.Source.Writes, Is.EqualTo(3));
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class ForwardingAudit(IAgentAuditRepository inner, Func<bool> failTerminals) : IAgentAuditRepository
    {
        public Task AppendAsync(AgentAuditEvent entry, CancellationToken cancellationToken = default) =>
            failTerminals() && entry.Outcome != AgentAuditOutcome.Intent
                ? throw new IOException("terminal append lost")
                : inner.AppendAsync(entry, cancellationToken);

        public Task<IReadOnlyList<AgentAuditEvent>> GetRecentAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => inner.GetRecentAsync(maximum, cancellationToken);

        public Task<IReadOnlyList<AgentAuditEvent>> GetPendingAsync(int maximum = 100,
            CancellationToken cancellationToken = default) => inner.GetPendingAsync(maximum, cancellationToken);
    }

    private static AgentToolExposure AllWrites() =>
        AgentToolExposure.Through(AgentToolExposureStage.UnitaryWrites).WithWriteTools(
            AgentWriteToolRelease.InsertOne | AgentWriteToolRelease.UpdateOne | AgentWriteToolRelease.DeleteOne |
            AgentWriteToolRelease.CreateIndex | AgentWriteToolRelease.DropIndex);

    private static AgentInvocationContext Context() => new(null, null, SessionId, TurnId);

    private static AgentPermissionGrant[] Grants(Guid principalId, ConnectionProfile profile,
        AgentOutputDestination destination, AgentPermission permission,
        AgentOutputDataScope scope = AgentOutputDataScope.DocumentValues) =>
        [new AgentPermissionGrant(principalId, AgentInvocationScope.ForSession(SessionId),
            profile.SourceGenerationId!.Value, permission, AgentNamespaceScope.ForCollection(profile.Id, "app", "items"),
            destination, scope)];

    private static readonly Guid ConnectionId = Guid.Parse("7a1f0c3e-9999-4e47-8f71-08a1a7e2a1c1");

    private static string Insert(string document) => JsonSerializer.Serialize(new
    {
        connectionId = ConnectionId, database = "app", collection = "items", documentEjson = document
    });

    private static string Update(string id, string update) => JsonSerializer.Serialize(new
    {
        connectionId = ConnectionId, database = "app", collection = "items", idEjson = id, updateEjson = update
    });

    private static string Delete(string id) => JsonSerializer.Serialize(new
    {
        connectionId = ConnectionId, database = "app", collection = "items", idEjson = id
    });

    private static string Index(string keys) => JsonSerializer.Serialize(new
    {
        connectionId = ConnectionId, database = "app", collection = "items", keysEjson = keys
    });

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) Assert.Fail("Condição não atingida.");
            await Task.Delay(5);
        }
    }

    private sealed class Fixture
    {
        public Fixture(AgentToolExposure exposure, bool composeApprovals = true, bool composeSource = true,
            IAgentWriteApprovalPrompt? prompt = null, TimeSpan? approvalTimeout = null,
            IAgentAuditRepository? audit = null)
        {
            Profiles = new MutableProfiles(ConnectionProfile.Create("Escrita", "mongodb://localhost:27017") with
            {
                Id = ConnectionId, SourceGenerationId = Guid.NewGuid()
            });
            Policies = new MutablePolicyProvider();
            Policies.Set(PrincipalId, 3,
            [
                .. Grants(PrincipalId, Profiles.Profile, AgentOutputDestination.Local(), AgentPermission.InsertDocuments),
                .. Grants(PrincipalId, Profiles.Profile, AgentOutputDestination.Local(), AgentPermission.UpdateDocuments),
                .. Grants(PrincipalId, Profiles.Profile, AgentOutputDestination.Local(), AgentPermission.DeleteDocuments),
                .. Grants(PrincipalId, Profiles.Profile, AgentOutputDestination.Local(), AgentPermission.CreateIndexes,
                    AgentOutputDataScope.Metadata)
            ]);
            Audit = new ConcurrentMemoryAudit();
            Source = new FakeAgentWriteSource();
            Prompt = new ScriptedWritePrompt();
            Authority = new TestAgentPrincipalAuthority();
            Registry = new AgentToolRegistry(Profiles, Policies, new AgentPermissionEvaluator(Policies), audit ?? Audit,
                exposure: exposure, principalAuthority: Authority, write: composeSource ? Source : null,
                writeApprovals: composeApprovals
                    ? new AgentWriteApprovalCoordinator(prompt ?? Prompt, approvalTimeout: approvalTimeout)
                    : null);
        }

        public MutableProfiles Profiles { get; }
        public MutablePolicyProvider Policies { get; }
        public ConcurrentMemoryAudit Audit { get; }
        public FakeAgentWriteSource Source { get; }
        public ScriptedWritePrompt Prompt { get; }
        public TestAgentPrincipalAuthority Authority { get; }
        public AgentToolRegistry Registry { get; }
        public AgentPrincipal Principal { get; } = new(PrincipalId, AgentPrincipalOrigin.Internal, 3);

        public Task<AgentToolInvocationResult> InvokeAsync(string tool, string arguments,
            CancellationToken cancellationToken = default) =>
            Registry.InvokeAsync(Principal, Context(), AgentOutputDestination.Local(),
                AgentToolOutputScopes.For(tool), tool, arguments, cancellationToken);
    }
}
