using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using System.Diagnostics;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture]
[Category("Integration")]
public sealed class CopilotFakeStdioRuntimeTests
{
    private string _testRoot = null!;

    [SetUp]
    public void CreateIsolatedRoot()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "KapibaraStudioCopilotFakeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
    }

    [TearDown]
    public void RemoveIsolatedRoot()
    {
        var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "KapibaraStudioCopilotFakeTests"));
        var root = Path.GetFullPath(_testRoot);
        if (root.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private static readonly string[] ExpectedModels = ["fake-model"];
    private static readonly AgentProviderSessionChange[] ExpectedResumeChanges =
        [AgentProviderSessionChange.ResumeFallback, AgentProviderSessionChange.Established];

    [Test]
    public async Task SessionCreatesSendsAndCompletesOnIdleOverFakeStdio()
    {
        var contractLogPath = CreateContractLogPath();
        var client = CreateFakeClient(contractLogPath: contractLogPath);
        await using var session = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model"), client);
        var request = new AgentTurnRequest(AgentTurnId.New(), "mensagem sintética", "tab-test", 1)
        {
            Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled,
                false, AgentConfirmationCategories.None),
        };

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);

        TestContext.WriteLine(string.Join(" | ", events.Select(item => $"{item.Kind}:{item.Text}")));
        Assert.That(events.Select(item => item.Kind).ToArray(), Is.EqualTo(new[]
        {
            AgentEventKind.MessageStarted, AgentEventKind.MessageDelta, AgentEventKind.MessageCompleted,
        }));
        Assert.That(events[1].Text, Is.EqualTo("resposta fake"));
        AssertSessionConfigurationWasRestricted(contractLogPath, "session.create");
    }

    [Test]
    public async Task ExplicitAccountCheckEnablesOnlyVerifiedMessageCapabilities()
    {
        var factoryCalls = 0;
        using var provider = new CopilotSubscriptionAgentProvider(new NoToolsRegistry(), _ =>
        {
            factoryCalls++;
            return CreateFakeClient();
        }, static () => true);

        var before = await provider.GetStatusAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(factoryCalls, Is.Zero, "A passive catalog listing must not start the runtime.");
            Assert.That(before, Is.SameAs(AgentProviderStatus.NotReported));
        });

        var account = await provider.CheckAccountAndModelsAsync();
        var after = await provider.GetStatusAsync(CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(account.State, Is.EqualTo(CopilotAccountState.Subscription));
            Assert.That(after.IsAvailable, Is.True, "The explicitly checked account enables message-only chat.");
            Assert.That(after.Models, Is.EqualTo(ExpectedModels));
            Assert.That(after.UnavailableCode, Is.Null);
            Assert.That(after.Capabilities.Chat, Is.True);
            Assert.That(after.Capabilities.Streaming, Is.True);
            Assert.That(after.Capabilities.Sessions, Is.True);
            Assert.That(after.Capabilities.ModelSelection, Is.True);
            Assert.That(after.Capabilities.TurnPlan, Is.True);
            Assert.That(after.Capabilities.ToolCalling, Is.True);
            Assert.That(after.Capabilities.NativeTools, Is.False, "Provider-native shell/file/network tools stay disabled.");
        });
    }

    [Test, Category("OfficialCli"), Explicit("Aceite manual Windows/Linux: consulta pelo provider o estado da conta oficial Copilot e os modelos elegíveis, sem enviar prompt/contexto.")]
    public async Task OfficialRuntimeReportsSanitizedAccountAndModelCatalog()
    {
        Assert.That(OperatingSystem.IsWindows() || OperatingSystem.IsLinux(), Is.True,
            "Requer runtime oficial em Windows ou Linux; executar não implica homologação.");
        using var provider = new CopilotSubscriptionAgentProvider(new NoToolsRegistry());

        var account = await provider.CheckAccountAndModelsAsync();
        var status = await provider.GetStatusAsync(CancellationToken.None);
        TestContext.Progress.WriteLine($"Estado da conta: {account.State}; modelos elegíveis: {status.Models.Count}; IDs: {string.Join(",", status.Models)}; chat disponível: {status.IsAvailable}; motivo: {status.UnavailableCode ?? "nenhum"}.");

        Assert.That(account.State, Is.Not.EqualTo(CopilotAccountState.Unavailable),
            "O SDK/runtime oficial deve responder à consulta explícita de conta.");
        if (account.State == CopilotAccountState.Subscription)
        {
            Assert.That(status.Models, Is.Not.Empty, "Uma conta OAuth Copilot deve retornar os modelos elegíveis.");
            Assert.That(status.IsAvailable, Is.True,
                "The real account may use the verified message-only flow while tool gates remain closed.");
            Assert.That(status.Capabilities.TurnPlan, Is.True);
            Assert.That(status.Capabilities.ToolCalling, Is.True);
            Assert.That(status.Capabilities.NativeTools, Is.False);
        }
    }

    [Test]
    public async Task ProviderDeletesNativeSessionThroughOfficialSdkRuntime()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), $"KapibaraCopilotDelete-{Guid.NewGuid():N}");
        try
        {
            using var provider = new CopilotSubscriptionAgentProvider(new NoToolsRegistry(),
                _ => CreateFakeClient(authenticated: false), static () => false, sessionRoot);

            await provider.DeleteProviderSessionAsync("fake-copilot-session", CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(sessionRoot)) Directory.Delete(sessionRoot, recursive: true);
        }
    }

    [Test]
    public async Task NativeSessionDeletionIsIdempotentWhenSessionIsAlreadyAbsent()
    {
        var sessionRoot = Path.Combine(Path.GetTempPath(), $"KapibaraCopilotDelete-{Guid.NewGuid():N}");
        try
        {
            using var provider = new CopilotSubscriptionAgentProvider(new NoToolsRegistry(),
                _ => CreateFakeClient(sessionExists: false), static () => true, sessionRoot);

            await provider.DeleteProviderSessionAsync("already-deleted-session", CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(sessionRoot)) Directory.Delete(sessionRoot, recursive: true);
        }
    }

    [Test]
    public async Task FailedResumeFallsBackToFreshSessionAndReportsBothUpdates()
    {
        var contractLogPath = CreateContractLogPath();
        var updates = new List<AgentProviderSessionUpdate>();
        await using var session = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model")
            {
                ResumeProviderSessionId = "missing-session",
                ProviderSessionObserver = updates.Add,
            }, CreateFakeClient(resumeFails: true, contractLogPath: contractLogPath));
        var request = new AgentTurnRequest(AgentTurnId.New(), "mensagem sintética", "tab-test", 1)
        {
            Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled,
                false, AgentConfirmationCategories.None),
        };

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);

        Assert.Multiple(() =>
        {
            Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta && item.Text == "resposta fake"), Is.True);
            Assert.That(updates.Select(update => update.Change), Is.EqualTo(ExpectedResumeChanges));
            Assert.That(updates[0].NoticeCode, Is.EqualTo("CopilotSessionResumeFailed"));
            Assert.That(updates[1].ProviderSessionId, Is.Not.Null.And.Not.Empty);
        });
        AssertSessionConfigurationWasRestricted(contractLogPath, "session.resume", "session.create");
    }

    [Test]
    public async Task ReservedSessionMissingAfterRestartIsRecreatedUnderSameId()
    {
        const string reservedId = "e68f67bb-305f-42b5-b407-d4f6e78b6fa7";
        var updates = new List<AgentProviderSessionUpdate>();
        await using var session = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model")
            {
                ReservedProviderSessionId = reservedId,
                ResumeProviderSessionId = reservedId,
                ProviderSessionObserver = updates.Add,
            }, CreateFakeClient(sessionExists: false));
        var request = new AgentTurnRequest(AgentTurnId.New(), "mensagem sintética", "tab-test", 1)
        {
            Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled,
                false, AgentConfirmationCategories.None),
        };

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);

        Assert.Multiple(() =>
        {
            Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta), Is.True);
            Assert.That(updates.Select(update => update.Change), Is.EqualTo(ExpectedResumeChanges));
            Assert.That(updates.Last().ProviderSessionId, Is.EqualTo(reservedId));
        });
    }

    [Test]
    public async Task SuccessfulResumeReappliesRestrictedSessionConfiguration()
    {
        const string reservedId = "3a811a75-47e0-4b77-b00e-a3f8332165d9";
        var contractLogPath = CreateContractLogPath();
        await using var session = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model")
            {
                ReservedProviderSessionId = reservedId,
                ResumeProviderSessionId = reservedId,
            }, CreateFakeClient(contractLogPath: contractLogPath));

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(CreateRequest(), CancellationToken.None)) events.Add(item);

        Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta), Is.True);
        AssertSessionConfigurationWasRestricted(contractLogPath, "session.resume");
    }

    [Test]
    public async Task NativeToolRequestFromRuntimeFailsClosedBeforeItCanReachProductTools()
    {
        await using var session = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model"),
            CreateFakeClient(nativeToolRequest: true));

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(CreateRequest(), CancellationToken.None)) events.Add(item);

        Assert.Multiple(() =>
        {
            Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested), Is.False);
            Assert.That(events.Any(item => item.Kind == AgentEventKind.AgentError && item.Text == "CopilotToolNotPlanned"), Is.True);
            Assert.That(events.Any(item => item.ToolName == "bash"), Is.False);
        });
    }

    [Test]
    public async Task SessionDeclaresOnlyProductToolsIncludedInTheTurnPlan()
    {
        const string toolName = "propose_file_edit";
        var contractLogPath = CreateContractLogPath();
        var request = CreateRequest();
        request = request with
        {
            Plan = request.Plan! with { ProductTools = [toolName] },
        };
        await using var session = new CopilotSubscriptionAgentSession(new OneToolRegistry(toolName),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model"),
            CreateFakeClient(contractLogPath: contractLogPath));

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);

        Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta), Is.True);
        AssertOnlyAllowedToolWasDeclared(contractLogPath, toolName,
            "target=active_buffer", "sem path", "buffer redigido", "EditNotApplicable");
    }

    [Test]
    public async Task MongoFindDeclarationExplainsLiteralJsonAndReadBounds()
    {
        const string toolName = "mongo_find";
        var contractLogPath = CreateContractLogPath();
        var request = CreateRequest();
        request = request with { Plan = request.Plan! with { ProductTools = [toolName] } };
        await using var session = new CopilotSubscriptionAgentSession(new OneToolRegistry(toolName),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model"),
            CreateFakeClient(contractLogPath: contractLogPath));

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);

        Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta), Is.True);
        AssertOnlyAllowedToolWasDeclared(contractLogPath, toolName,
            "consulta MongoDB find somente leitura", "Extended JSON literal", "100 documentos", "consentimento MongoDB");
    }

    [TestCase("mongo_count", "Conta documentos", "Extended JSON literal", "consentimento MongoDB")]
    [TestCase("sample_documents", "amostra de até 20 documentos", "consentimento MongoDB")]
    [TestCase("mongo_find_one", "findOne somente leitura", "no máximo um documento")]
    [TestCase("get_document", "um único documento por _id", "consentimento MongoDB")]
    [TestCase("mongo_distinct", "valores distintos limitados", "Esses valores podem conter dados")]
    [TestCase("mongo_explain", "queryPlanner", "não executa a consulta", "grants de diagnóstico/leitura")]
    public async Task DerivedReadDeclarationsExplainTheirBoundsAndConsent(string toolName, params string[] terms)
    {
        var contractLogPath = CreateContractLogPath();
        var request = CreateRequest();
        request = request with { Plan = request.Plan! with { ProductTools = [toolName] } };
        await using var session = new CopilotSubscriptionAgentSession(new OneToolRegistry(toolName),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model"),
            CreateFakeClient(contractLogPath: contractLogPath));

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);

        Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta), Is.True);
        AssertOnlyAllowedToolWasDeclared(contractLogPath, toolName, terms);
    }

    [Test]
    public async Task PlannedProductToolResultIsReturnedThroughOfficialSdkRpc()
    {
        const string toolName = "get_workspace_context";
        var contractLogPath = CreateContractLogPath();
        var request = CreateRequest();
        request = request with { Plan = request.Plan! with { ProductTools = [toolName] } };
        var turnId = request.TurnId;
        var sessionId = AgentSessionId.New();
        await using var session = new CopilotSubscriptionAgentSession(new OneToolRegistry(toolName),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model"),
            CreateFakeClient(contractLogPath: contractLogPath, productToolRequest: true));

        var events = new List<AgentProviderEvent>();
        var replayRejected = false;
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None))
        {
            events.Add(item);
            if (item.Kind == AgentEventKind.ToolRequested)
            {
                var result = new AgentToolResult(sessionId, turnId, item.ToolCallId!.Value,
                    AgentToolResultStatus.Succeeded, "contexto sintético");
                await session.SubmitToolResultAsync(result, CancellationToken.None);
                replayRejected = Assert.ThrowsAsync<InvalidOperationException>(
                    () => session.SubmitToolResultAsync(result, CancellationToken.None)) is not null;
            }
        }

        JsonDocument[]? entries = null;
        try
        {
            entries = File.ReadAllLines(contractLogPath).Select(static line => JsonDocument.Parse(line)).ToArray();
            var toolResult = entries.SingleOrDefault(entry =>
                entry.RootElement.GetProperty("method").GetString() == "session.tools.handlePendingToolCall");
            Assert.Multiple(() =>
            {
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested && item.ToolName == toolName), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta && item.Text == "ferramenta autorizada"), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.AgentError), Is.False);
                Assert.That(replayRejected, Is.True, "A tool result must not be accepted more than once.");
                Assert.That(toolResult, Is.Not.Null);
                Assert.That(toolResult!.RootElement.GetProperty("params").GetProperty("requestId").GetString(), Is.EqualTo("rpc-product-1"));
                Assert.That(toolResult.RootElement.GetProperty("params").GetProperty("result").GetProperty("textResultForLlm").GetString(), Is.EqualTo("contexto sintético"));
                Assert.That(toolResult.RootElement.GetProperty("params").GetProperty("result").GetProperty("resultType").GetString(), Is.EqualTo("success"));
            });
        }
        finally
        {
            if (entries is not null)
                foreach (var entry in entries) entry.Dispose();
            if (File.Exists(contractLogPath)) File.Delete(contractLogPath);
        }
    }

    [Test]
    public async Task VolatileSessionResumesAcrossAgentSessionsOnlyWhileStoreRetainsItsId()
    {
        await using var store = new CopilotVolatileSessionFsStore();
        var firstUpdates = new List<AgentProviderSessionUpdate>();
        var firstOptions = new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model")
        {
            PersistProviderSession = false,
            ConversationId = Guid.NewGuid(),
            ProviderSessionObserver = firstUpdates.Add,
        };
        // This fake runtime models session.create/resume but not SessionFs RPCs; SessionFs client wiring is
        // asserted independently by CopilotVolatileSessionFsTests.RuntimeClientAndSessionConfigAreWiredToTheVolatileProvider.
        await using (var first = new CopilotSubscriptionAgentSession(new NoToolsRegistry(), firstOptions,
            CreateFakeClient(), store))
        {
            var firstEvents = new List<AgentProviderEvent>();
            await foreach (var item in first.RunTurnAsync(CreateRequest(), CancellationToken.None)) firstEvents.Add(item);
            TestContext.WriteLine($"first={string.Join(',', firstEvents.Select(item => $"{item.Kind}:{item.Text}"))}; updates={string.Join(',', firstUpdates.Select(item => $"{item.Change}:{item.ProviderSessionId}:{item.NoticeCode}"))}");
        }

        var volatileId = firstUpdates.Single(update => update.Change == AgentProviderSessionChange.Established).ProviderSessionId!;
        Assert.That(store.ContainsSession(volatileId), Is.True);
        var secondUpdates = new List<AgentProviderSessionUpdate>();
        await using (var second = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model")
            {
                PersistProviderSession = false,
                ResumeProviderSessionId = volatileId,
                ConversationId = firstOptions.ConversationId,
                ProviderSessionObserver = secondUpdates.Add,
            }, CreateFakeClient(), store))
        {
            var events = new List<AgentProviderEvent>();
            await foreach (var item in second.RunTurnAsync(CreateRequest(), CancellationToken.None)) events.Add(item);
            Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta), Is.True);
        }

        Assert.Multiple(() =>
        {
            Assert.That(secondUpdates.Any(update => update.Change == AgentProviderSessionChange.ResumeFallback), Is.False);
            Assert.That(secondUpdates.Single(update => update.Change == AgentProviderSessionChange.Established).ProviderSessionId,
                Is.EqualTo(volatileId));
        });
    }

    [Test]
    public async Task VolatileSessionWithMissingStoreIdFallsBackToFreshTrackedId()
    {
        await using var store = new CopilotVolatileSessionFsStore();
        const string staleId = "session-from-a-previous-process";
        var updates = new List<AgentProviderSessionUpdate>();
        await using var session = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model")
            {
                PersistProviderSession = false,
                ResumeProviderSessionId = staleId,
                ConversationId = Guid.NewGuid(),
                ProviderSessionObserver = updates.Add,
            }, CreateFakeClient(), store);

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(CreateRequest(), CancellationToken.None)) events.Add(item);
        var establishedId = updates.Single(update => update.Change == AgentProviderSessionChange.Established).ProviderSessionId!;

        Assert.Multiple(() =>
        {
            Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta), Is.True);
            Assert.That(updates.Any(update => update.Change == AgentProviderSessionChange.ResumeFallback), Is.True);
            Assert.That(establishedId, Is.Not.EqualTo(staleId));
            Assert.That(store.ContainsSession(establishedId), Is.True);
            Assert.That(store.ContainsSession(staleId), Is.False);
        });
    }

    [Test]
    public async Task CancellationAfterRuntimeEmitsOutputIsReportedAsPossiblyEffective()
    {
        var turnId = AgentTurnId.New();
        await using var session = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model"), CreateFakeClient());
        var request = new AgentTurnRequest(turnId, "mensagem sintética", "tab-test", 1)
        {
            Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled,
                false, AgentConfirmationCategories.None),
        };
        var sawDelta = false;
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None))
        {
            if (item.Kind != AgentEventKind.MessageDelta) continue;
            sawDelta = true;
            await session.CancelTurnAsync(turnId, CancellationToken.None);
            break;
        }

        Assert.Multiple(() =>
        {
            Assert.That(sawDelta, Is.True);
            Assert.That(session.GetCancellationReport(turnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
        });
    }

    [Test]
    public async Task CancellingOneConversationDoesNotAbortAnotherActiveConversation()
    {
        await using var cancelledConversation = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model"),
            CreateFakeClient(noIdle: true));
        await using var activeConversation = new CopilotSubscriptionAgentSession(new NoToolsRegistry(),
            new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, "fake-model"),
            CreateFakeClient(delayIdleMs: 2_000));

        using var cancelledToken = new CancellationTokenSource();
        var cancelledRequest = CreateRequest();
        var activeRequest = CreateRequest();
        var cancelledEvents = new List<AgentProviderEvent>();
        var activeEvents = new List<AgentProviderEvent>();
        var cancelledDelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var activeDelta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelledTask = CollectTurnAsync(cancelledConversation, cancelledRequest, cancelledEvents,
            cancelledToken.Token, () => cancelledDelta.TrySetResult());
        await cancelledDelta.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var activeTask = CollectTurnAsync(activeConversation, activeRequest, activeEvents, CancellationToken.None,
            () => activeDelta.TrySetResult());
        await activeDelta.Task.WaitAsync(TimeSpan.FromSeconds(10));

        cancelledToken.Cancel();
        await cancelledTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(activeTask.IsCompleted, Is.False,
            "The second conversation should still be active while its fake runtime delays the idle event.");
        await activeTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(cancelledEvents.Any(item => item.Kind == AgentEventKind.MessageDelta), Is.True);
            Assert.That(cancelledConversation.GetCancellationReport(cancelledRequest.TurnId),
                Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
            Assert.That(activeEvents.Any(item => item.Kind == AgentEventKind.MessageDelta && item.Text == "resposta fake"), Is.True);
            Assert.That(activeEvents.Any(item => item.Kind == AgentEventKind.AgentError), Is.False);
        });
    }

    private static async Task CollectTurnAsync(CopilotSubscriptionAgentSession session, AgentTurnRequest request,
        List<AgentProviderEvent> events, CancellationToken cancellationToken, Action? onDelta = null)
    {
        await foreach (var item in session.RunTurnAsync(request, cancellationToken))
        {
            events.Add(item);
            if (item.Kind == AgentEventKind.MessageDelta) onDelta?.Invoke();
        }
    }

    private CopilotClient CreateFakeClient(bool authenticated = true, bool sessionExists = true,
        bool resumeFails = false, SessionFsConfig? sessionFs = null, string? contractLogPath = null,
        bool nativeToolRequest = false, bool productToolRequest = false, bool noIdle = false,
        int delayIdleMs = 0)
    {
        var runtimeScript = FindRuntimeScript();
        var powershell = FindPowerShellExecutable();
        var commonArguments = OperatingSystem.IsWindows()
            ? new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", runtimeScript }
            : new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", runtimeScript };
        string[] arguments = authenticated
            ? sessionExists
                ? commonArguments
                : [.. commonArguments, "-MissingSession"]
            : sessionExists
                ? [.. commonArguments, "-NoLogin"]
                : [.. commonArguments, "-NoLogin", "-MissingSession"];
        if (resumeFails) arguments = [.. arguments, "-ResumeFail"];
        if (nativeToolRequest) arguments = [.. arguments, "-NativeToolRequest"];
        if (productToolRequest) arguments = [.. arguments, "-ProductToolRequest"];
        if (noIdle) arguments = [.. arguments, "-NoIdle"];
        if (delayIdleMs > 0) arguments = [.. arguments, "-DelayIdleMs", delayIdleMs.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        if (contractLogPath is not null) arguments = [.. arguments, "-ContractLogPath", contractLogPath];
        return new CopilotClient(new CopilotClientOptions
        {
            Connection = RuntimeConnection.ForStdio(powershell, arguments),
            UseLoggedInUser = false,
            // Match the production mode that shares the official user's CLI login.
            Mode = CopilotClientMode.CopilotCli,
            BaseDirectory = _testRoot,
            SessionFs = sessionFs,
        });
    }

    private string CreateContractLogPath() => Path.Combine(_testRoot,
        $"contract-{Guid.NewGuid():N}.jsonl");

    private static string FindPowerShellExecutable() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe")
        : "pwsh";

    private static void AssertOnlyAllowedToolWasDeclared(string logPath, string toolName,
        params string[] expectedDescriptionTerms)
    {
        try
        {
            using var entry = JsonDocument.Parse(File.ReadAllLines(logPath).Single(line =>
            {
                using var document = JsonDocument.Parse(line);
                return document.RootElement.GetProperty("method").GetString() == "session.create";
            }));
            var root = entry.RootElement;
            Assert.That(root.GetProperty("method").GetString(), Is.EqualTo("session.create"));
            var parameters = root.GetProperty("params");
            var tools = parameters.GetProperty("tools");
            var availableTools = parameters.GetProperty("availableTools");
            Assert.Multiple(() =>
            {
                Assert.That(tools.GetArrayLength(), Is.EqualTo(1));
                Assert.That(tools.GetRawText(), Does.Contain(toolName));
                var description = tools.EnumerateArray().Single().GetProperty("description").GetString();
                foreach (var term in expectedDescriptionTerms)
                    Assert.That(description, Does.Contain(term));
                Assert.That(availableTools.GetRawText(), Does.Contain(toolName));
                Assert.That(tools.GetRawText(), Does.Not.Contain("bash"));
                Assert.That(availableTools.GetRawText(), Does.Not.Contain("bash"));
            });
        }
        finally
        {
            if (File.Exists(logPath)) File.Delete(logPath);
        }
    }

    private static void AssertFalseIfPresent(JsonElement json, string propertyName)
    {
        if (json.TryGetProperty(propertyName, out var value))
            Assert.That(value.GetBoolean(), Is.False, $"The {propertyName} capability must not be enabled.");
    }

    private static void AssertSessionConfigurationWasRestricted(string logPath, params string[] expectedMethods)
    {
        try
        {
            var entries = File.ReadAllLines(logPath).Select(static line => JsonDocument.Parse(line)).ToArray();
            TestContext.WriteLine(string.Join(" | ", entries.Select(entry =>
                $"{entry.RootElement.GetProperty("method").GetString()}:{entry.RootElement.GetProperty("params")}")));
            var sessionConfigs = entries.Where(entry => entry.RootElement.GetProperty("method").GetString() is
                "session.create" or "session.resume").ToArray();
            Assert.That(sessionConfigs.Select(entry => entry.RootElement.GetProperty("method").GetString()).ToArray(),
                Is.EqualTo(expectedMethods));
            foreach (var entry in sessionConfigs)
            {
                var parameters = entry.RootElement.GetProperty("params");
                Assert.Multiple(() =>
                {
                    Assert.That(parameters.GetProperty("tools").GetArrayLength(), Is.Zero,
                        "No unplanned/native tool declaration may be sent by an empty product plan.");
                    Assert.That(parameters.GetProperty("availableTools").GetArrayLength(), Is.Zero,
                        "An empty plan must result in an empty SDK allowlist.");
                    Assert.That(parameters.GetProperty("enableConfigDiscovery").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("enableFileHooks").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("enableHostGitOperations").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("enableSessionStore").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("enableSkills").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("isExperimentalMode").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("enableSessionTelemetry").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("enableFileChangeTracking").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("enableOnDemandInstructionDiscovery").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("skipEmbeddingRetrieval").GetBoolean(), Is.True);
                    AssertFalseIfPresent(parameters, "enableMcpApps");
                    Assert.That(parameters.GetProperty("requestCanvasRenderer").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("requestExtensions").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("customAgentsLocalOnly").GetBoolean(), Is.True);
                    AssertFalseIfPresent(parameters, "coauthorEnabled");
                    AssertFalseIfPresent(parameters, "manageScheduleEnabled");
                    Assert.That(parameters.GetProperty("pluginDirectories").GetArrayLength(), Is.Zero);
                    Assert.That(parameters.GetProperty("instructionDirectories").GetArrayLength(), Is.Zero);
                    Assert.That(parameters.GetProperty("skillDirectories").GetArrayLength(), Is.Zero);
                    Assert.That(parameters.GetProperty("customAgents").GetArrayLength(), Is.Zero);
                    Assert.That(parameters.GetProperty("commands").GetArrayLength(), Is.Zero);
                    Assert.That(parameters.GetProperty("canvases").GetArrayLength(), Is.Zero);
                    Assert.That(parameters.GetProperty("memory").GetProperty("enabled").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("toolSearch").GetProperty("enabled").GetBoolean(), Is.False);
                    Assert.That(parameters.GetProperty("toolFilterPrecedence").GetString(), Is.EqualTo("excluded"));
                    Assert.That(parameters.GetProperty("hooks").GetBoolean(), Is.True);
                    Assert.That(parameters.GetProperty("requestPermission").GetBoolean(), Is.True);
                });
            }

            var builtInAgentUpdates = entries.Where(entry =>
                entry.RootElement.GetProperty("method").GetString() == "session.options.update" &&
                entry.RootElement.GetProperty("params").TryGetProperty("includedBuiltinAgents", out _)).ToArray();
            Assert.That(builtInAgentUpdates, Has.Length.EqualTo(1),
                "Every usable Copilot session must receive a built-in-agent allowlist before a turn.");
            Assert.That(builtInAgentUpdates[0].RootElement.GetProperty("params").GetProperty("includedBuiltinAgents").GetArrayLength(),
                Is.Zero, "An empty built-in agent allowlist must be applied by the runtime before sending.");
            var updateIndex = Array.FindIndex(entries, entry =>
                entry.RootElement.GetProperty("method").GetString() == "session.options.update" &&
                entry.RootElement.GetProperty("params").TryGetProperty("includedBuiltinAgents", out _));
            var sendIndex = Array.FindIndex(entries, entry => entry.RootElement.GetProperty("method").GetString() ==
                "session.send");
            Assert.That(updateIndex, Is.GreaterThanOrEqualTo(0).And.LessThan(sendIndex),
                "The acknowledged restriction must precede session.send.");
        }
        finally
        {
            if (File.Exists(logPath)) File.Delete(logPath);
        }
    }

    private static AgentTurnRequest CreateRequest() => new(AgentTurnId.New(), "mensagem sintética", "tab-test", 1)
    {
        Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled,
            false, AgentConfirmationCategories.None),
    };

    private static string FindRuntimeScript()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "Copilot", "FakeCopilotRuntime.ps1");
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Fake runtime script was not found above the test output directory.");
    }

    private sealed class NoToolsRegistry : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => null;
        public string? GetOutputSchemaJson(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No tools are expected in this contract test.");
    }

    private sealed class OneToolRegistry(string toolName) : IAgentToolRegistry
    {
        private readonly AgentToolDescriptor _descriptor = new(toolName, 1, AgentToolRisk.ReadOnly,
            [AgentPermission.ReadMetadata]);

        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [_descriptor];
        public AgentToolDescriptor? FindDescriptor(string? name) => string.Equals(name, toolName, StringComparison.Ordinal)
            ? _descriptor : null;
        public AgentToolDescriptor? FindInProcessDescriptor(string providerId, string? name) =>
            providerId == AgentProviderIds.GitHubCopilotSubscription ? FindDescriptor(name) : null;
        public string? GetInputSchemaJson(string? name) => FindDescriptor(name) is null
            ? null : "{\"type\":\"object\",\"properties\":{}}";
        public string? GetInProcessInputSchemaJson(string providerId, string? name) =>
            FindInProcessDescriptor(providerId, name) is null ? null : "{\"type\":\"object\",\"properties\":{}}";
        public string? GetOutputSchemaJson(string? name) => FindDescriptor(name) is null
            ? null : "{\"type\":\"object\",\"properties\":{}}";
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal,
            AgentInvocationContext? invocationContext, AgentOutputDestination? destination,
            AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(
                "The product tool registry is not invoked directly by the Copilot adapter.");
    }
}




