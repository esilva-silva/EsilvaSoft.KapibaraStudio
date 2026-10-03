using System.Reflection;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

[TestFixture]
[Category("Unit")]
public sealed class CopilotSessionEventContractTests
{
    private static readonly Type SessionType = typeof(CopilotSubscriptionAgentProvider).Assembly
        .GetType("EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot.CopilotSubscriptionAgentSession", throwOnError: true)!;
    private static readonly Type ActiveTurnType = SessionType.GetNestedType("ActiveTurn", BindingFlags.NonPublic)!;
    private static readonly MethodInfo OnEvent = SessionType.GetMethod("OnEvent", BindingFlags.NonPublic | BindingFlags.Static)!;
    private static readonly AgentEventKind[] TwoMessageEvents =
    [
        AgentEventKind.MessageStarted, AgentEventKind.MessageDelta, AgentEventKind.MessageCompleted,
        AgentEventKind.MessageStarted, AgentEventKind.MessageDelta, AgentEventKind.MessageCompleted,
    ];
    private static readonly string[] TwoMessageContents = ["first answer", "second answer"];

    [Test]
    public void SdkContractExposesStdioTransportAndConstructibleSessionEvents()
    {
        Assert.That(typeof(RuntimeConnection).GetMethod(nameof(RuntimeConnection.ForStdio)), Is.Not.Null);
        Assert.That(typeof(SessionIdleEvent).GetProperty(nameof(SessionIdleEvent.Data))?.SetMethod, Is.Not.Null);
        Assert.That(typeof(ExternalToolRequestedEvent).GetProperty(nameof(ExternalToolRequestedEvent.Data))?.SetMethod, Is.Not.Null);
        Assert.That(typeof(AssistantMessageDeltaEvent).GetProperty(nameof(AssistantMessageDeltaEvent.Data))?.SetMethod, Is.Not.Null);
    }

    [Test]
    public void OfficialUsageIsMappedAsCallTotalWithoutInventingMonetaryCostOrClosingTurn()
    {
        var turn = CreateTurn();
        Dispatch(turn, new AssistantUsageEvent { Data = new AssistantUsageData
        {
            ApiCallId = "call-one", Model = "fixture-model", InputTokens = 100, OutputTokens = 0,
            CacheReadTokens = 40, CacheWriteTokens = 5,
        } });
        var metric = ReadEvents(turn).Single();
        Assert.Multiple(() =>
        {
            Assert.That(metric.Kind, Is.EqualTo(AgentEventKind.UsageUpdated));
            Assert.That(metric.Usage!.Scope, Is.EqualTo(AgentUsageScope.CallTotal));
            Assert.That(metric.Usage.InputTokens, Is.EqualTo(100));
            Assert.That(metric.Usage.OutputTokens, Is.Zero);
            Assert.That(metric.Usage.CacheReadTokens, Is.EqualTo(40));
            Assert.That(metric.Usage.Cost, Is.Null, "SDK Cost is a model multiplier, not currency.");
            Assert.That(GetDone(turn).IsCompleted, Is.False);
        });
    }

    [Test]
    public void AutopilotIdleDoesNotCompleteTurnWhileOrdinaryIdleDoes()
    {
        var autopilot = CreateTurn();
        var autopilotEvent = new SessionIdleEvent { Data = new SessionIdleData { Mode = SessionMode.Autopilot } };
        Dispatch(autopilot, autopilotEvent);
        Assert.That(GetDone(autopilot).IsCompleted, Is.False);

        var ordinary = CreateTurn();
        var ordinaryEvent = new SessionIdleEvent { Data = new SessionIdleData { Mode = SessionMode.Interactive } };
        Dispatch(ordinary, ordinaryEvent);
        Assert.That(GetDone(ordinary).IsCompleted, Is.True);
    }

    [Test]
    public void LateDeltaAfterCompletionDoesNotReopenTheMessageOrFailTheNextMessage()
    {
        var turn = CreateTurn();
        Dispatch(turn, new AssistantMessageStartEvent { Data = new AssistantMessageStartData { MessageId = "first" } });
        Dispatch(turn, new AssistantMessageDeltaEvent
        { Data = new AssistantMessageDeltaData { MessageId = "first", DeltaContent = "first answer" } });
        Dispatch(turn, new AssistantMessageEvent
        { Data = new AssistantMessageData { MessageId = "first", Content = "first answer" } });
        Assert.DoesNotThrow(() => Dispatch(turn, new AssistantMessageDeltaEvent
        { Data = new AssistantMessageDeltaData { MessageId = "first", DeltaContent = "late content" } }));
        Dispatch(turn, new AssistantMessageDeltaEvent
        { Data = new AssistantMessageDeltaData { MessageId = "second", DeltaContent = "second answer" } });
        Dispatch(turn, new AssistantMessageEvent
        { Data = new AssistantMessageData { MessageId = "second", Content = "second answer" } });

        var events = ReadEvents(turn);
        Assert.That(events.Select(item => item.Kind), Is.EqualTo(TwoMessageEvents));
        Assert.That(events.Where(item => item.Kind == AgentEventKind.MessageDelta).Select(item => item.Text),
            Is.EqualTo(TwoMessageContents));
        Assert.That(events.Take(3).Select(item => item.MessageId).Distinct().Count(), Is.EqualTo(1));
        Assert.That(events.Skip(3).Select(item => item.MessageId).Distinct().Count(), Is.EqualTo(1));
        Assert.That(events[0].MessageId, Is.Not.EqualTo(events[3].MessageId));
        Assert.That(GetDone(turn).IsCompleted, Is.False);
    }

    [Test]
    public void ToolRequestIsMappedOnlyWhenShapeAndPlanAreValid()
    {
        var turn = CreateTurn();
        using var args = JsonDocument.Parse("{\"database\":\"sample\"}");
        var request = new ExternalToolRequestedEvent
        {
            Data = new ExternalToolRequestedData
            {
                RequestId = "rpc-1", SessionId = "session-1", ToolCallId = "tool-1",
                ToolName = "get_workspace_context",
                Arguments = args.RootElement.Clone(),
            },
        };

        Dispatch(turn, request, ["get_workspace_context"]);
        var mapped = ReadEvents(turn);
        Assert.That(mapped, Has.Length.EqualTo(1));
        Assert.That(mapped[0].Kind, Is.EqualTo(AgentEventKind.ToolRequested));
        Assert.That(mapped[0].ToolName, Is.EqualTo("get_workspace_context"));
        Assert.That(mapped[0].ArgumentsJson, Is.EqualTo("{\"database\":\"sample\"}"));
    }

    [TestCase(null, "get_workspace_context", "rpc-1")]
    [TestCase("{}", null, "rpc-1")]
    [TestCase("null", "get_workspace_context", "rpc-1")]
    [TestCase("{}", "bash", "rpc-1")]
    public void MalformedOrUnplannedToolRequestFailsClosed(string? json, string? toolName, string requestId)
    {
        var turn = CreateTurn();
        JsonElement? args = null;
        JsonDocument? document = null;
        if (json is not null)
        {
            document = JsonDocument.Parse(json);
            args = document.RootElement.Clone();
        }

        try
        {
            Dispatch(turn, new ExternalToolRequestedEvent
            {
                Data = new ExternalToolRequestedData { RequestId = requestId, SessionId = "session-1", ToolCallId = "tool-1", ToolName = toolName!, Arguments = args },
            }, ["get_workspace_context"]);
            var mapped = ReadEvents(turn);
            Assert.That(mapped, Has.Length.EqualTo(1));
            Assert.That(mapped[0].Kind, Is.EqualTo(AgentEventKind.AgentError));
            Assert.That(GetDone(turn).IsCompleted, Is.True);
        }
        finally { document?.Dispose(); }
    }

    [Test]
    public void DuplicateSdkToolRequestIdFailsClosed()
    {
        var turn = CreateTurn();
        using var args = JsonDocument.Parse("{}");
        ExternalToolRequestedEvent Request() => new()
        {
            Data = new ExternalToolRequestedData
            {
                RequestId = "rpc-duplicate", SessionId = "session-1", ToolCallId = "tool-1", ToolName = "get_workspace_context", Arguments = args.RootElement.Clone(),
            },
        };

        Dispatch(turn, Request(), ["get_workspace_context"]);
        Dispatch(turn, Request(), ["get_workspace_context"]);
        var mapped = ReadEvents(turn);
        Assert.That(mapped.Select(x => x.Kind), Is.EqualTo(new[] { AgentEventKind.ToolRequested, AgentEventKind.AgentError }));
        Assert.That(GetDone(turn).IsCompleted, Is.True);
    }


    [Test]
    public async Task PermissionRequestForPlannedCustomToolIsApprovedOnce()
    {
        var permission = new PermissionRequestCustomTool
        {
            ToolName = "get_workspace_context",
            ToolDescription = "Read authorized workspace context",
        };
        var allowed = new HashSet<string>(["get_workspace_context"], StringComparer.Ordinal);

        var decision = await CopilotSubscriptionAgentSession.DecidePermissionAsync(permission, allowed);

        Assert.That(decision.GetType().Name, Is.EqualTo("PermissionDecisionApproveOnce"));
    }

    [Test]
    public async Task PermissionRequestForNativeShellToolIsRejectedEvenWhenItsNameAppearsInPlan()
    {
        var permission = new PermissionRequestShell
        {
            CanOfferSessionApproval = false,
            Commands = [],
            FullCommandText = "echo should-not-run",
            HasWriteFileRedirection = false,
            Intention = "run a native shell command",
            PossiblePaths = [],
            PossibleUrls = [],
        };
        var allowed = new HashSet<string>(["bash", "get_workspace_context"], StringComparer.Ordinal);

        var decision = await CopilotSubscriptionAgentSession.DecidePermissionAsync(permission, allowed);

        Assert.That(decision.GetType().Name, Is.EqualTo("PermissionDecisionReject"));
    }
    private static object CreateTurn() => Activator.CreateInstance(ActiveTurnType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null,
        args: [AgentTurnId.New(), CancellationToken.None], culture: null)!;

    private static void Dispatch(object turn, SessionEvent evt, HashSet<string>? allowed = null) =>
        OnEvent.Invoke(null, [turn, evt, allowed ?? new HashSet<string>(StringComparer.Ordinal)]);

    private static Task GetDone(object turn) => (Task)ActiveTurnType.GetProperty("Done")!.PropertyType.GetProperty("Task")!.GetValue(ActiveTurnType.GetProperty("Done")!.GetValue(turn))!;

    private static AgentProviderEvent[] ReadEvents(object turn)
    {
        var channel = ActiveTurnType.GetProperty("Events")!.GetValue(turn)!;
        var reader = channel.GetType().GetProperty("Reader")!.GetValue(channel)!;
        var tryRead = reader.GetType().GetMethod("TryRead")!;
        var values = new List<AgentProviderEvent>();
        var args = new object?[] { null };
        while ((bool)tryRead.Invoke(reader, args)!) values.Add((AgentProviderEvent)args[0]!);
        return values.ToArray();
    }
}



