using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture, Category("Unit")]
internal sealed class CopilotToolResultCorrelationTests
{
    private const string ToolName = "get_workspace_context";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [TestCase("error")]
    [TestCase("idle")]
    [TestCase("unplanned")]
    [TestCase("cancel")]
    public async Task PendingResultAfterTerminalCannotReachNativeRequest(string terminal)
    {
        var releaseSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new MemoryCopilotRuntime { CompleteOnSend = false, SendWait = releaseSend.Task };
        await using var session = new CopilotSubscriptionAgentSession(new OneToolRegistry(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
        var request = Request();
        var toolSeen = new TaskCompletionSource<AgentProviderEvent[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = ObserveAsync(session, request, 1, toolSeen);
        var native = client.Sessions.Single();
        try
        {
            await native.Sent.Task.WaitAsync(Deadline);
            native.Emit(ToolRequest("rpc-pending", ToolName));
            var call = (await toolSeen.Task.WaitAsync(Deadline)).Single();
            if (terminal == "cancel") await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
            else native.Emit(terminal switch
            {
                "error" => new SessionErrorEvent { Data = new() { ErrorType = "quota", Message = "private-error-canary" } },
                "idle" => new SessionIdleEvent { Data = new() { Mode = SessionMode.Interactive } },
                _ => ToolRequest("rpc-unplanned", "bash"),
            });

            Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitToolResultAsync(
                Result(request.TurnId, call, "private-result-canary"), CancellationToken.None));
            Assert.That(native.ToolResults, Is.Empty);
            releaseSend.TrySetResult();
            var events = await pending.WaitAsync(Deadline);
            Assert.Multiple(() =>
            {
                Assert.That(events.Count(item => item.Kind == AgentEventKind.ToolRequested), Is.EqualTo(1));
                Assert.That(events.Any(item => item.Text?.Contains("canary", StringComparison.Ordinal) == true), Is.False);
                Assert.That(native.SendInvocations, Is.EqualTo(1));
                Assert.That(native.Disposals, Is.EqualTo(1));
                Assert.That(client.Creates, Has.Count.EqualTo(1));
                Assert.That(client.Resumes, Is.Empty);
                Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
            });
        }
        finally
        {
            releaseSend.TrySetResult();
            await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
            await pending.WaitAsync(Deadline);
        }
    }

    [Test]
    public async Task ConcurrentReversedResultsKeepRequestIdsAndRejectDuplicatesAndWrongTurn()
    {
        var client = new MemoryCopilotRuntime { CompleteOnSend = false };
        await using var session = new CopilotSubscriptionAgentSession(new OneToolRegistry(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
        var request = Request();
        var toolsSeen = new TaskCompletionSource<AgentProviderEvent[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = ObserveAsync(session, request, 2, toolsSeen);
        var native = client.Sessions.Single();
        try
        {
            await native.Sent.Task.WaitAsync(Deadline);
            native.Emit(ToolRequest("rpc-first", ToolName));
            native.Emit(ToolRequest("rpc-second", ToolName));
            var calls = await toolsSeen.Task.WaitAsync(Deadline);
            Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitToolResultAsync(
                Result(AgentTurnId.New(), calls[0], "wrong-turn"), CancellationToken.None));
            var first = Result(request.TurnId, calls[0], "first-result");
            var second = Result(request.TurnId, calls[1], "second-result");
            await Task.WhenAll(Task.Run(() => session.SubmitToolResultAsync(second, CancellationToken.None)),
                Task.Run(() => session.SubmitToolResultAsync(first, CancellationToken.None)));
            Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitToolResultAsync(first, CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitToolResultAsync(second, CancellationToken.None));
            var results = native.ToolResults.ToDictionary(item => item.RequestId, item => item.Result);
            Assert.Multiple(() =>
            {
                Assert.That(results, Has.Count.EqualTo(2));
                Assert.That(results["rpc-first"].TextResultForLlm, Is.EqualTo("first-result"));
                Assert.That(results["rpc-second"].TextResultForLlm, Is.EqualTo("second-result"));
                Assert.That(results.Values.All(item => item.ResultType == "success" && item.Error is null), Is.True);
            });
            native.Emit(new SessionIdleEvent { Data = new() { Mode = SessionMode.Interactive } });
            await pending.WaitAsync(Deadline);
            Assert.That(native.Disposals, Is.EqualTo(1));
        }
        finally
        {
            await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
            await pending.WaitAsync(Deadline);
        }
    }

    [TestCase("idle")]
    [TestCase("error")]
    [TestCase("cancel")]
    public async Task OldToolCallbackAndResultCannotReachFreshTurnReusingNativeRequestId(string terminal)
    {
        var client = new MemoryCopilotRuntime { CompleteOnSend = false };
        await using var session = new CopilotSubscriptionAgentSession(new OneToolRegistry(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
        var firstRequest = Request();
        var firstToolSeen = new TaskCompletionSource<AgentProviderEvent[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRun = ObserveAsync(session, firstRequest, 1, firstToolSeen);
        var firstNative = client.Sessions.Single();
        await firstNative.Sent.Task.WaitAsync(Deadline);
        var oldCallback = firstNative.CaptureEventHandler();
        var reusedEvent = ToolRequest("rpc-reused", ToolName);
        reusedEvent.Id = Guid.NewGuid();
        firstNative.Emit(reusedEvent);
        var firstCall = (await firstToolSeen.Task.WaitAsync(Deadline)).Single();
        if (terminal == "cancel") await session.CancelTurnAsync(firstRequest.TurnId, CancellationToken.None);
        else firstNative.Emit(terminal == "idle"
            ? new SessionIdleEvent { Data = new() { Mode = SessionMode.Interactive } }
            : new SessionErrorEvent { Data = new() { ErrorType = "quota", Message = "private-error-canary" } });
        await firstRun.WaitAsync(Deadline);

        var secondRequest = Request();
        var secondToolSeen = new TaskCompletionSource<AgentProviderEvent[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRun = ObserveAsync(session, secondRequest, 1, secondToolSeen);
        var secondNative = client.Sessions.Last();
        try
        {
            await secondNative.Sent.Task.WaitAsync(Deadline);
            oldCallback(reusedEvent);
            oldCallback(ToolRequest("rpc-late", ToolName));
            oldCallback(new SessionErrorEvent { Data = new() { ErrorType = "quota", Message = "private-late-canary" } });
            secondNative.Emit(reusedEvent);
            secondNative.Emit(reusedEvent);
            var secondCall = (await secondToolSeen.Task.WaitAsync(Deadline)).Single();
            Assert.That(secondCall.ToolCallId, Is.Not.EqualTo(firstCall.ToolCallId));
            Assert.That(secondRun.IsCompleted, Is.False);
            Assert.ThrowsAsync<InvalidOperationException>(() => session.SubmitToolResultAsync(
                Result(firstRequest.TurnId, firstCall, "private-old-result-canary"), CancellationToken.None));
            await session.SubmitToolResultAsync(Result(secondRequest.TurnId, secondCall, "fresh-result"), CancellationToken.None);
            secondNative.Emit(new SessionIdleEvent { Data = new() { Mode = SessionMode.Interactive } });
            var events = await secondRun.WaitAsync(Deadline);
            Assert.Multiple(() =>
            {
                Assert.That(events.Count(item => item.Kind == AgentEventKind.ToolRequested), Is.EqualTo(1));
                Assert.That(events.Any(item => item.Kind == AgentEventKind.AgentError || item.Text?.Contains("canary", StringComparison.Ordinal) == true), Is.False);
                Assert.That(firstNative.ToolResults, Is.Empty);
                Assert.That(secondNative.ToolResults.Single().RequestId, Is.EqualTo("rpc-reused"));
                Assert.That(secondNative.ToolResults.Single().Result.TextResultForLlm, Is.EqualTo("fresh-result"));
                Assert.That(firstNative.SendInvocations, Is.EqualTo(1));
                Assert.That(secondNative.SendInvocations, Is.EqualTo(1));
                Assert.That(firstNative.Disposals, Is.EqualTo(1));
                Assert.That(secondNative.Disposals, Is.EqualTo(1));
                Assert.That(client.Creates.Count + client.Resumes.Count, Is.EqualTo(2));
            });
        }
        finally
        {
            await session.CancelTurnAsync(secondRequest.TurnId, CancellationToken.None);
            await secondRun.WaitAsync(Deadline);
        }
    }

    [Test]
    public async Task TwoSessionsCorrelateIdenticalNativeIdsAndKeepCancellationIndependent()
    {
        var firstClient = new MemoryCopilotRuntime { CompleteOnSend = false };
        var secondClient = new MemoryCopilotRuntime { CompleteOnSend = false };
        await using var firstSession = new CopilotSubscriptionAgentSession(new OneToolRegistry(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), firstClient);
        await using var secondSession = new CopilotSubscriptionAgentSession(new OneToolRegistry(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), secondClient);
        using var firstToken = new CancellationTokenSource();
        using var secondToken = new CancellationTokenSource();
        var firstRequest = Request();
        var secondRequest = Request();
        var firstTools = new TaskCompletionSource<AgentProviderEvent[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondTools = new TaskCompletionSource<AgentProviderEvent[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstRun = ObserveAsync(firstSession, firstRequest, 2, firstTools, firstToken.Token);
        var secondRun = ObserveAsync(secondSession, secondRequest, 2, secondTools, secondToken.Token);
        var firstNative = firstClient.Sessions.Single();
        var secondNative = secondClient.Sessions.Single();
        try
        {
            await Task.WhenAll(firstNative.Sent.Task, secondNative.Sent.Task).WaitAsync(Deadline);
            var sharedEvent = ToolRequest("rpc-shared", ToolName);
            sharedEvent.Id = Guid.NewGuid();
            var nextEvent = ToolRequest("rpc-next", ToolName);
            nextEvent.Id = Guid.NewGuid();
            foreach (var native in new[] { firstNative, secondNative })
            {
                native.Emit(sharedEvent);
                native.Emit(sharedEvent);
                native.Emit(nextEvent);
            }
            var firstCalls = await firstTools.Task.WaitAsync(Deadline);
            var secondCalls = await secondTools.Task.WaitAsync(Deadline);
            Assert.ThrowsAsync<InvalidOperationException>(() => secondSession.SubmitToolResultAsync(
                Result(secondRequest.TurnId, firstCalls[0], "private-foreign-canary"), CancellationToken.None));
            await Task.WhenAll(Task.Run(() => firstSession.SubmitToolResultAsync(
                    Result(firstRequest.TurnId, firstCalls[0], "first-result"), firstToken.Token)),
                Task.Run(() => secondSession.SubmitToolResultAsync(
                    Result(secondRequest.TurnId, secondCalls[0], "second-result"), secondToken.Token)));
            var oldCallback = firstNative.CaptureEventHandler();
            firstToken.Cancel();
            var firstEvents = await firstRun.WaitAsync(Deadline);
            Assert.That(secondToken.IsCancellationRequested, Is.False);
            Assert.That(secondRun.IsCompleted, Is.False, "The second session still awaits its second tool result.");
            oldCallback(nextEvent);
            oldCallback(new SessionErrorEvent { Data = new() { ErrorType = "quota", Message = "private-late-canary" } });
            Assert.ThrowsAsync<InvalidOperationException>(() => firstSession.SubmitToolResultAsync(
                Result(firstRequest.TurnId, firstCalls[1], "private-cancelled-canary"), CancellationToken.None));
            await secondSession.SubmitToolResultAsync(Result(secondRequest.TurnId, secondCalls[1], "second-next"), secondToken.Token);
            secondNative.Emit(new SessionIdleEvent { Data = new() { Mode = SessionMode.Interactive } });
            var secondEvents = await secondRun.WaitAsync(Deadline);
            Assert.Multiple(() =>
            {
                Assert.That(firstNative.ToolResults.Single().RequestId, Is.EqualTo("rpc-shared"));
                Assert.That(firstNative.ToolResults.Single().Result.TextResultForLlm, Is.EqualTo("first-result"));
                Assert.That(secondNative.ToolResults.ToDictionary(item => item.RequestId, item => item.Result.TextResultForLlm),
                    Is.EqualTo(new Dictionary<string, string?> { ["rpc-shared"] = "second-result", ["rpc-next"] = "second-next" }));
                Assert.That(firstEvents.Count(item => item.Kind == AgentEventKind.ToolRequested), Is.EqualTo(2));
                Assert.That(secondEvents.Count(item => item.Kind == AgentEventKind.ToolRequested), Is.EqualTo(2));
                Assert.That(secondEvents.Any(item => item.Kind == AgentEventKind.AgentError || item.Text?.Contains("canary", StringComparison.Ordinal) == true), Is.False);
                Assert.That(firstNative.Aborts, Is.GreaterThanOrEqualTo(1));
                Assert.That(secondNative.Aborts, Is.Zero);
                Assert.That(firstNative.Disposals, Is.EqualTo(1));
                Assert.That(secondNative.Disposals, Is.EqualTo(1));
                Assert.That(firstNative.SendInvocations, Is.EqualTo(1));
                Assert.That(secondNative.SendInvocations, Is.EqualTo(1));
                Assert.That(firstSession.GetCancellationReport(firstRequest.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
                Assert.That(secondSession.GetCancellationReport(secondRequest.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
            });
        }
        finally
        {
            firstToken.Cancel();
            secondToken.Cancel();
            await Task.WhenAll(firstRun, secondRun).WaitAsync(Deadline);
        }
    }

    private static AgentToolResult Result(AgentTurnId turnId, AgentProviderEvent call, string data) =>
        new(AgentSessionId.New(), turnId, call.ToolCallId!.Value, AgentToolResultStatus.Succeeded, data);

    private static ExternalToolRequestedEvent ToolRequest(string requestId, string name) => new()
    {
        Data = new() { RequestId = requestId, SessionId = "synthetic-session", ToolCallId = requestId + "-tool", ToolName = name,
            Arguments = JsonSerializer.SerializeToElement(new Dictionary<string, string>()) },
    };

    private static AgentTurnRequest Request() => new(AgentTurnId.New(), "synthetic prompt", "synthetic-tab", 1)
    {
        Plan = new(AgentOperationMode.Agent, [], [], [], [ToolName], AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None),
    };

    private static async Task<List<AgentProviderEvent>> ObserveAsync(CopilotSubscriptionAgentSession session,
        AgentTurnRequest request, int count, TaskCompletionSource<AgentProviderEvent[]> toolsSeen, CancellationToken cancellationToken = default)
    {
        var events = new List<AgentProviderEvent>();
        var tools = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, cancellationToken))
        {
            events.Add(item);
            if (item.Kind != AgentEventKind.ToolRequested) continue;
            tools.Add(item);
            if (tools.Count == count) toolsSeen.TrySetResult(tools.ToArray());
        }
        return events;
    }

    private sealed class OneToolRegistry : IAgentToolRegistry
    {
        private readonly AgentToolDescriptor _descriptor = new(ToolName, 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata]);
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [_descriptor];
        public AgentToolDescriptor? FindDescriptor(string? name) => name == ToolName ? _descriptor : null;
        public AgentToolDescriptor? FindInProcessDescriptor(string providerId, string? name) =>
            providerId == AgentProviderIds.GitHubCopilotSubscription ? FindDescriptor(name) : null;
        public string? GetInputSchemaJson(string? name) => FindDescriptor(name) is null ? null
            : "{\"type\":\"object\",\"additionalProperties\":false,\"properties\":{}}";
        public string? GetInProcessInputSchemaJson(string providerId, string? name) =>
            FindInProcessDescriptor(providerId, name) is null ? null : GetInputSchemaJson(name);
        public string? GetOutputSchemaJson(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("The adapter must not invoke handlers directly.");
    }
}
