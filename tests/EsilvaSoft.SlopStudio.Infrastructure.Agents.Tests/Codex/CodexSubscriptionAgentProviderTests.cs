using System.Text.Json;
using System.Threading.Channels;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.Codex;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.CodexAppServer;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests.Codex;

[TestFixture]
internal sealed class CodexSubscriptionAgentProviderTests
{
    private static readonly string[] ExpectedModels = ["gpt-6-sol"];
    [Test]
    public async Task StatusUsesOnlyChatGptAccountAndListsAvailableModels()
    {
        var provider = CreateProvider();
        var status = await provider.GetStatusAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(status.IsAvailable, Is.True);
            Assert.That(status.Models, Is.EqualTo(ExpectedModels));
            Assert.That(status.DefaultModel, Is.EqualTo("gpt-6-sol"));
            Assert.That(status.Capabilities.UsesNetwork, Is.True);
        });
    }

    [Test]
    public async Task TurnStreamsAndCarriesResolvedAttachmentsWithReadOnlyPolicy()
    {
        FakeConnection? connection = null;
        var provider = CreateProvider(connect: _ => Task.FromResult<ICodexAppServerConnection>(connection = new FakeConnection("chatgpt")));
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(CodexSubscriptionAgentProvider.Id), CancellationToken.None);
        {
            var attachment = new AgentContextAttachment(AgentAttachmentKind.ActiveFile, "sample.cs", "sample.cs",
                "class Sample {}", 15, new string('a', 64));
            var request = new AgentTurnRequest(AgentTurnId.New(), "Explique", "tab-1", 1)
            {
                Plan = EmptyPlan(),
                SystemPrompt = "Regras do sistema da conversa",
                WorkspaceContext = new AgentWorkspaceContext(DateTimeOffset.UtcNow, Environment.CurrentDirectory),
                Attachments = [attachment],
            };

            var events = new List<AgentProviderEvent>();
            await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);

            Assert.Multiple(() =>
            {
                Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta && item.Text == "Olá Codex"), Is.True);
                Assert.That(events.Any(item => item.Kind == AgentEventKind.SessionCompleted), Is.True);
                Assert.That(connection!.Requests.Any(item => item.Method == "thread/start" &&
                    item.Parameters.GetProperty("sandbox").GetString() == "readOnly"), Is.True);
                var turnStart = connection.Requests.Single(item => item.Method == "turn/start").Parameters;
                var access = turnStart.GetProperty("sandboxPolicy").GetProperty("access");
                Assert.That(access.GetProperty("type").GetString(), Is.EqualTo("restricted"));
                Assert.That(access.GetProperty("includePlatformDefaults").GetBoolean(), Is.False);
                Assert.That(access.GetProperty("readableRoots")[0].GetString(), Is.EqualTo(Path.GetFullPath(Environment.CurrentDirectory)));
                Assert.That(turnStart.TryGetProperty("cwd", out _), Is.True);
                Assert.That(turnStart.GetProperty("sandboxPolicy").GetProperty("networkAccess").GetBoolean(), Is.False);
                var turnInput = connection.Requests.Single(item => item.Method == "turn/start").Parameters
                    .GetProperty("input")[0].GetProperty("text").GetString();
                Assert.That(turnInput, Does.Contain("class Sample {}"));
                Assert.That(turnInput, Does.Contain("Regras do sistema da conversa"));
                Assert.That(turnInput, Does.Contain("Explique"));
            });
        }
    }

    [Test]
    public async Task TurnInterruptsWhenCodexStartsNativeCommandExecution()
    {
        FakeConnection? connection = null;
        var provider = CreateProvider(connect: _ => Task.FromResult<ICodexAppServerConnection>(connection = new FakeConnection("chatgpt", nativeWork: true)));
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(CodexSubscriptionAgentProvider.Id), CancellationToken.None);
        var request = new AgentTurnRequest(AgentTurnId.New(), "Leia os arquivos", "tab-1", 1)
        {
            Plan = EmptyPlan(),
            WorkspaceContext = new AgentWorkspaceContext(DateTimeOffset.UtcNow, Environment.CurrentDirectory),
        };

        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None)) events.Add(item);

        Assert.Multiple(() =>
        {
            Assert.That(events.Any(item => item.Kind == AgentEventKind.AgentError && item.Text == "CodexNativeToolBlocked"), Is.True);
            var interrupt = connection!.Requests.Single(item => item.Method == "turn/interrupt").Parameters;
            Assert.That(interrupt.GetProperty("threadId").GetString(), Is.EqualTo("thread-1"));
            Assert.That(interrupt.GetProperty("turnId").GetString(), Is.EqualTo("turn-1"));
        });
    }

    [Test]
    public void ApiKeyAccountCannotCreateSubscriptionSession()
    {
        var provider = CreateProvider(connect: _ => Task.FromResult<ICodexAppServerConnection>(new FakeConnection("apiKey")));
        Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.CreateSessionAsync(
            new AgentSessionOptions(CodexSubscriptionAgentProvider.Id), CancellationToken.None));
    }

    [Test]
    public async Task RestartResumesOnlyWithThePersistedToolCatalogAndCapturedTurnWorkspace()
    {
        var updates = new List<AgentProviderSessionUpdate>();
        var firstConnection = new FakeConnection("chatgpt");
        var firstProvider = CreateProvider(connect: _ => Task.FromResult<ICodexAppServerConnection>(firstConnection));
        await using (var firstSession = await firstProvider.CreateSessionAsync(
                         new AgentSessionOptions(CodexSubscriptionAgentProvider.Id) { ProviderSessionObserver = updates.Add },
                         CancellationToken.None))
        {
            await DrainAsync(firstSession, Request(EmptyPlan(), "first"));
        }

        var handle = updates.Single(item => item.Change == AgentProviderSessionChange.Established).ProviderSessionId;
        Assert.That(handle, Does.StartWith("cs1."));

        var secondConnection = new FakeConnection("chatgpt");
        var secondProvider = CreateProvider(connect: _ => Task.FromResult<ICodexAppServerConnection>(secondConnection));
        await using (var resumed = await secondProvider.CreateSessionAsync(
                         new AgentSessionOptions(CodexSubscriptionAgentProvider.Id)
                         {
                             ResumeProviderSessionId = handle,
                             WorkingDirectory = Path.GetPathRoot(Environment.CurrentDirectory),
                             ProviderSessionObserver = updates.Add,
                         }, CancellationToken.None))
        {
            var capturedWorkspace = Path.GetFullPath(Environment.CurrentDirectory);
            await DrainAsync(resumed, Request(EmptyPlan(), "second", capturedWorkspace));
            var resume = secondConnection.Requests.Single(item => item.Method == "thread/resume").Parameters;
            var turn = secondConnection.Requests.Single(item => item.Method == "turn/start").Parameters;
            Assert.Multiple(() =>
            {
                Assert.That(resume.GetProperty("threadId").GetString(), Is.EqualTo("thread-1"));
                Assert.That(resume.GetProperty("cwd").GetString(), Is.EqualTo(capturedWorkspace));
                Assert.That(turn.GetProperty("cwd").GetString(), Is.EqualTo(capturedWorkspace));
                Assert.That(turn.GetProperty("sandboxPolicy").GetProperty("access").GetProperty("readableRoots")[0].GetString(), Is.EqualTo(capturedWorkspace));
                Assert.That(secondConnection.Requests.Any(item => item.Method == "thread/start"), Is.False);
            });
        }
    }

    [Test]
    public async Task ChangedToolCatalogStartsFreshThreadAndReportsLostResumeContext()
    {
        var updates = new List<AgentProviderSessionUpdate>();
        var original = new FakeConnection("chatgpt");
        var firstProvider = CreateProvider(connect: _ => Task.FromResult<ICodexAppServerConnection>(original));
        await using (var firstSession = await firstProvider.CreateSessionAsync(
                         new AgentSessionOptions(CodexSubscriptionAgentProvider.Id) { ProviderSessionObserver = updates.Add },
                         CancellationToken.None))
        {
            await DrainAsync(firstSession, Request(EmptyPlan(), "first"));
        }

        var handle = updates.Single(item => item.Change == AgentProviderSessionChange.Established).ProviderSessionId;
        var changedConnection = new FakeConnection("chatgpt");
        var changedProvider = new CodexSubscriptionAgentProvider(new CodexSubscriptionAgentProviderOptions("unused-codex-home"),
            new ReadOnlyToolRegistry(), _ => Task.FromResult<ICodexAppServerConnection>(changedConnection));
        await using (var changed = await changedProvider.CreateSessionAsync(
                         new AgentSessionOptions(CodexSubscriptionAgentProvider.Id)
                         {
                             ResumeProviderSessionId = handle,
                             ProviderSessionObserver = updates.Add,
                         }, CancellationToken.None))
        {
            await DrainAsync(changed, Request(ReadPlan("list_databases"), "second", permissions: new AgentProviderPermissions
            {
                ProviderId = CodexSubscriptionAgentProvider.Id,
            }));
        }

        var thread = changedConnection.Requests.Single(item => item.Method == "thread/start").Parameters;
        var tools = thread.GetProperty("dynamicTools");
        Assert.Multiple(() =>
        {
            Assert.That(changedConnection.Requests.Any(item => item.Method == "thread/resume"), Is.False);
            Assert.That(tools.GetArrayLength(), Is.EqualTo(1));
            Assert.That(tools[0].TryGetProperty("type", out _), Is.False);
            Assert.That(updates.Any(item => item.Change == AgentProviderSessionChange.ResumeFallback && item.NoticeCode == "CodexToolPolicyChanged"), Is.True);
        });
    }

    [Test]
    public async Task ActiveSessionToolCatalogChangeStartsNewThreadBeforeSendingTurn()
    {
        var connection = new FakeConnection("chatgpt");
        var provider = new CodexSubscriptionAgentProvider(new CodexSubscriptionAgentProviderOptions("unused-codex-home"),
            new ReadOnlyToolRegistry(), _ => Task.FromResult<ICodexAppServerConnection>(connection));
        var updates = new List<AgentProviderSessionUpdate>();
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(CodexSubscriptionAgentProvider.Id)
        {
            ProviderSessionObserver = updates.Add,
        }, CancellationToken.None);

        await DrainAsync(session, Request(EmptyPlan(), "first"));
        await DrainAsync(session, Request(ReadPlan("list_databases"), "second", permissions: new AgentProviderPermissions
        {
            ProviderId = CodexSubscriptionAgentProvider.Id,
        }));

        Assert.Multiple(() =>
        {
            Assert.That(connection.Requests.Count(item => item.Method == "thread/start"), Is.EqualTo(2));
            Assert.That(connection.Requests.Any(item => item.Method == "thread/resume"), Is.False);
            Assert.That(updates.Any(item => item.Change == AgentProviderSessionChange.ResumeFallback && item.NoticeCode == "CodexToolPolicyChanged"), Is.True);
        });
    }

    private static CodexSubscriptionAgentProvider CreateProvider(
        Func<CancellationToken, Task<ICodexAppServerConnection>>? connect = null) =>
        new(new CodexSubscriptionAgentProviderOptions("unused-codex-home"), tools: null,
            connect ?? (_ => Task.FromResult<ICodexAppServerConnection>(new FakeConnection("chatgpt"))));

    private static AgentTurnPlan EmptyPlan() => new(AgentOperationMode.Agent, [], [], [], [],
        AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None);

    private static AgentTurnRequest Request(AgentTurnPlan plan, string text, string? workspace = null,
        AgentProviderPermissions? permissions = null) => new(AgentTurnId.New(), text, "tab", 1)
    {
        Plan = plan,
        Permissions = permissions,
        SystemPrompt = "system " + text,
        WorkspaceContext = new AgentWorkspaceContext(DateTimeOffset.UtcNow, workspace ?? Environment.CurrentDirectory),
    };

    private static AgentTurnPlan ReadPlan(string name) => new(AgentOperationMode.Agent, [], [], [], [name],
        AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None);

    private static async Task DrainAsync(IAgentSession session, AgentTurnRequest request)
    {
        await foreach (var _ in session.RunTurnAsync(request, CancellationToken.None)) { }
    }

    private sealed class ReadOnlyToolRegistry : IAgentToolRegistry
    {
        private static readonly AgentToolDescriptor Descriptor = new("list_databases", 1, AgentToolRisk.ReadOnly,
            [AgentPermission.ReadMetadata]);

        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [Descriptor];
        public IReadOnlyList<AgentToolDescriptor> GetChannelDescriptors() => [Descriptor];
        public AgentToolDescriptor? FindDescriptor(string? name) => name == Descriptor.Name ? Descriptor : null;
        public string? GetInputSchemaJson(string? name) => name == Descriptor.Name
            ? "{\"type\":\"object\",\"properties\":{},\"required\":[],\"additionalProperties\":false}"
            : null;
        public string? GetOutputSchemaJson(string? name) => name == Descriptor.Name ? "{\"type\":\"object\"}" : null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeConnection(string authMode, bool nativeWork = false) : ICodexAppServerConnection
    {
        private readonly Channel<CodexAppServerInboundMessage> _messages = Channel.CreateUnbounded<CodexAppServerInboundMessage>();
        private string? _threadId;
        private int _threadSequence;
        private int _turnSequence;
        private readonly List<(string Method, JsonElement Parameters)> _requests = [];

        public IReadOnlyList<(string Method, JsonElement Parameters)> Requests => _requests;

        public IAsyncEnumerable<CodexAppServerInboundMessage> Messages => _messages.Reader.ReadAllAsync();

        public Task<JsonElement> RequestAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var parameterDoc = JsonDocument.Parse(JsonSerializer.Serialize(parameters ?? new { }));
            var captured = parameterDoc.RootElement.Clone();
            _requests.Add((method, captured));
            return Task.FromResult(method switch
            {
                "initialize" => Parse("{\"userAgent\":\"fake\"}"),
                "account/read" => Parse(authMode == "chatgpt"
                    ? "{\"requiresOpenaiAuth\":true,\"account\":{\"type\":\"chatgpt\",\"email\":\"private@example.invalid\",\"planType\":\"pro\"}}"
                    : "{\"requiresOpenaiAuth\":true,\"account\":{\"type\":\"apiKey\"}}"),
                "model/list" => Parse("{\"data\":[{\"model\":\"gpt-6-sol\",\"id\":\"gpt-6-sol\"}]}"),
                "thread/start" => StartThread(),
                "thread/resume" => ResumeThread(captured),
                "turn/start" => StartTurn(),
                "turn/interrupt" => Parse("{}"),
                _ => throw new InvalidOperationException("Unexpected fake App Server request."),
            });
        }

        public Task NotifyAsync(string method, object? parameters = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task RespondAsync(JsonElement id, object? result, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RespondErrorAsync(JsonElement id, int code, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            _messages.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }

        private JsonElement StartThread()
        {
            _threadId = $"thread-{++_threadSequence}";
            return Parse(JsonSerializer.Serialize(new { thread = new { id = _threadId } }));
        }

        private JsonElement ResumeThread(JsonElement parameters)
        {
            _threadId = parameters.GetProperty("threadId").GetString();
            return Parse(JsonSerializer.Serialize(new { thread = new { id = _threadId } }));
        }

        private JsonElement StartTurn()
        {
            var turnId = $"turn-{++_turnSequence}";
            if (nativeWork)
            {
                _messages.Writer.TryWrite(Message("item/started", new
                {
                    threadId = _threadId, turnId, item = new { id = "command-1", type = "commandExecution", status = "inProgress" },
                }));
            }
            _messages.Writer.TryWrite(Message("item/agentMessage/delta", new { threadId = _threadId, turnId, delta = "Olá Codex" }));
            _messages.Writer.TryWrite(Message("turn/completed", new { threadId = _threadId, turnId, turn = new { id = turnId, status = "completed" } }));
            return Parse(JsonSerializer.Serialize(new { turn = new { id = turnId, status = "inProgress" } }));
        }

        private static JsonElement Parse(string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }

        private static CodexAppServerInboundMessage Message(string method, object parameters) =>
            new(method, Parse(JsonSerializer.Serialize(parameters)), null);
    }
}
