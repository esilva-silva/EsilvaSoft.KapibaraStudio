using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture, Category("Unit")]
internal sealed class CopilotSessionErrorTests
{
    private const string Canary = "private-prompt-token-path-canary";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(5);

    [Test]
    public async Task MissingStructuredErrorPayloadFailsClosedAfterSendWithoutReplay()
    {
        var client = new MemoryCopilotRuntime { CompleteOnSend = false };
        await using var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
        var request = Request();
        var pending = CollectAsync(session, request);
        var native = client.Sessions.Single();
        await native.Sent.Task.WaitAsync(Deadline);
        try
        {
            var callback = native.CaptureEventHandler();
            callback(new SessionErrorEvent { Data = null! });
            callback(new AssistantMessageDeltaEvent
            { Data = new() { MessageId = "late", DeltaContent = Canary } });
            callback(new SessionErrorEvent { Data = new() { ErrorType = "quota", Message = Canary } });
            var events = await pending.WaitAsync(Deadline);
            Assert.Multiple(() =>
            {
                Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).Text, Is.EqualTo("CopilotProviderFailure"));
                Assert.That(events.Any(item => item.Text?.Contains(Canary, StringComparison.Ordinal) == true), Is.False);
                Assert.That(session.ProviderErrorCodes, Does.Contain("CopilotProviderFailure"));
                Assert.That(native.SendInvocations, Is.EqualTo(1));
                Assert.That(client.Creates, Has.Count.EqualTo(1));
                Assert.That(client.Resumes, Is.Empty);
                Assert.That(native.Disposals, Is.EqualTo(1));
                Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
            });
        }
        finally
        {
            await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
            await pending.WaitAsync(Deadline);
        }
    }

    [Test]
    public async Task UnrequestedRuntimeCancellationAfterSendIsReportedAsFailureWithoutReplay()
    {
        var client = new MemoryCopilotRuntime { SendFailure = new OperationCanceledException(Canary) };
        await using var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
        var request = Request();
        // CollectAsync uses CancellationToken.None: the runtime exception is not a caller cancellation.
        var events = await CollectAsync(session, request).WaitAsync(Deadline);
        Assert.Multiple(() =>
        {
            Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).Text, Is.EqualTo("CopilotSessionSendFailed"));
            Assert.That(events.Any(item => item.Text?.Contains(Canary, StringComparison.Ordinal) == true), Is.False);
            Assert.That(session.ProviderErrorCodes, Does.Contain("CopilotSessionSendFailed"));
            Assert.That(client.Starts, Is.EqualTo(1));
            Assert.That(client.Creates, Has.Count.EqualTo(1));
            Assert.That(client.Resumes, Is.Empty);
            Assert.That(client.Sessions.Single().SendInvocations, Is.EqualTo(1));
            Assert.That(client.Sessions.Single().Disposals, Is.EqualTo(1));
            Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
        });
    }

    [TestCase("authentication", "CopilotAuthenticationFailed")]
    [TestCase("authorization", "CopilotAccessDenied")]
    [TestCase("quota", "CopilotQuotaExceeded")]
    [TestCase("rate_limit", "CopilotRateLimited")]
    [TestCase("context_limit", "CopilotContextLimitExceeded")]
    [TestCase("query", "CopilotQueryFailed")]
    [TestCase("Authentication", "CopilotProviderFailure")]
    [TestCase("unknown-" + Canary, "CopilotProviderFailure")]
    [TestCase(null, "CopilotProviderFailure")]
    public async Task StructuredSessionErrorIsSanitizedTerminalAndNeverReplaysPrompt(string? category, string expected)
    {
        var client = new MemoryCopilotRuntime { CompleteOnSend = false };
        await using var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
        var request = Request();
        var pending = CollectAsync(session, request);
        var native = client.Sessions.Single();
        await native.Sent.Task.WaitAsync(Deadline);
        try
        {
            native.Emit(new SessionErrorEvent
            {
                Data = new SessionErrorData
                {
                    ErrorType = category!, Message = Canary, Stack = Canary, Url = "https://example.invalid/" + Canary,
                    ErrorCode = Canary, ProviderCallId = Canary, ServiceRequestId = Canary,
                    EligibleForAutoSwitch = true,
                },
            });
            // Events after the error must not reopen the response or dispatch an unplanned tool.
            native.Emit(new AssistantMessageDeltaEvent
            { Data = new AssistantMessageDeltaData { MessageId = "late", DeltaContent = Canary } });
            native.Emit(new ExternalToolRequestedEvent
            { Data = new ExternalToolRequestedData { RequestId = "late-tool", ToolName = "bash", SessionId = native.SessionId, ToolCallId = "late-call" } });
            native.Emit(new SessionErrorEvent { Data = new SessionErrorData { ErrorType = "quota", Message = Canary } });
            var events = await pending.WaitAsync(Deadline);

            Assert.Multiple(() =>
            {
                Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).Text, Is.EqualTo(expected));
                Assert.That(session.ProviderErrorCodes, Does.Contain(expected), "The shared runtime only accepts declared fixed codes.");
                Assert.That(events.Any(item => item.Text?.Contains(Canary, StringComparison.Ordinal) == true), Is.False);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.ToolRequested), Is.False);
                Assert.That(native.SendInvocations, Is.EqualTo(1));
                Assert.That(client.Creates, Has.Count.EqualTo(1));
                Assert.That(client.Resumes, Is.Empty);
                Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
                Assert.That(native.Disposals, Is.EqualTo(1));
            });
        }
        finally
        {
            await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
            await pending.WaitAsync(Deadline);
        }
    }

    [TestCase("start")]
    [TestCase("auth")]
    [TestCase("create")]
    [TestCase("send")]
    public async Task TimeoutIsTypedWithoutRetryAndPreservesDeliveryUncertainty(string stage)
    {
        var timeout = new TimeoutException(Canary);
        var client = new MemoryCopilotRuntime();
        switch (stage)
        {
            case "start": client.StartFailure = timeout; break;
            case "auth": client.AuthenticationFailure = timeout; break;
            case "create": client.CreateFailure = timeout; break;
            case "send": client.SendFailure = timeout; break;
        }
        await using var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
        var request = Request();
        var events = await CollectAsync(session, request).WaitAsync(Deadline);
        Assert.Multiple(() =>
        {
            Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).Text, Is.EqualTo("CopilotRequestTimedOut"));
            Assert.That(session.ProviderErrorCodes, Does.Contain("CopilotRequestTimedOut"));
            Assert.That(events.Any(item => item.Text?.Contains(Canary, StringComparison.Ordinal) == true), Is.False);
            Assert.That(client.Starts, Is.EqualTo(1));
            Assert.That(client.Creates.Count, Is.LessThanOrEqualTo(1));
            Assert.That(client.Resumes, Is.Empty);
            Assert.That(client.Sessions.Sum(item => item.SendInvocations), Is.EqualTo(stage == "send" ? 1 : 0));
            Assert.That(session.GetCancellationReport(request.TurnId), Is.EqualTo(stage == "send"
                ? AgentTurnCancellationReport.MayHaveTakenEffect : AgentTurnCancellationReport.NothingSent));
        });
    }

    [Test]
    public async Task ExceptionTextCannotMasqueradeAsStructuredAuthenticationOrQuotaError()
    {
        var client = new MemoryCopilotRuntime { CreateFailure = new IOException("authentication quota " + Canary) };
        await using var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
        var events = await CollectAsync(session, Request());
        Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).Text, Is.EqualTo("CopilotSessionCreateFailed"));
        Assert.That(events.Any(item => item.Text?.Contains(Canary, StringComparison.Ordinal) == true), Is.False);
        Assert.That(client.Creates, Has.Count.EqualTo(1));
        Assert.That(client.Sessions, Is.Empty);
    }

    [Test]
    public async Task SendDisconnectUsesFixedCodeAndDoesNotReplayPossiblyDeliveredPrompt()
    {
        var client = new MemoryCopilotRuntime { SendFailure = new IOException("connection closed " + Canary) };
        await using var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "synthetic-model"), client);
        var request = Request();

        var events = await CollectAsync(session, request).WaitAsync(Deadline);

        Assert.Multiple(() =>
        {
            Assert.That(events.Single(item => item.Kind == AgentEventKind.AgentError).Text,
                Is.EqualTo("CopilotSessionSendFailed"));
            Assert.That(events.Any(item => item.Text?.Contains(Canary, StringComparison.Ordinal) == true), Is.False);
            Assert.That(client.Starts, Is.EqualTo(1));
            Assert.That(client.Creates, Has.Count.EqualTo(1));
            Assert.That(client.Resumes, Is.Empty);
            Assert.That(client.Sessions.Single().SendInvocations, Is.EqualTo(1));
            Assert.That(session.GetCancellationReport(request.TurnId),
                Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect));
        });
    }

    private static AgentTurnRequest Request() => new(AgentTurnId.New(), "synthetic prompt", "synthetic-tab", 1)
    {
        Plan = new(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None),
    };

    private static async Task<List<AgentProviderEvent>> CollectAsync(CopilotSubscriptionAgentSession session, AgentTurnRequest request)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);
        return events;
    }

    private sealed class NoTools : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => null;
        public string? GetOutputSchemaJson(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
