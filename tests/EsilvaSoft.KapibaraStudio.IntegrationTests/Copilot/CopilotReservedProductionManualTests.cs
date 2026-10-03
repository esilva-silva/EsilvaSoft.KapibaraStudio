using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Copilot;

[TestFixture, Category("Integration"), Category("OfficialCli")]
internal sealed class CopilotReservedProductionManualTests
{
    [Test, Explicit("Reproduz o adapter de produção persistente/reservado com prompt e contexto sintéticos sem tools, trust ou dados do usuário.")]
    public async Task OfficialProductionAdapterStreamsReservedPersistentSessionWithSyntheticContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "KapibaraStudio-CopilotReserved-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sessionId = Guid.NewGuid().ToString("D");
        await using var store = new CopilotPersistentSessionFsStore(Path.Combine(root, "sessions"));
        var sdk = new CopilotClient(CopilotRuntimeSettings.SessionClientOptions(root,
            CopilotPersistentSessionFsStore.CreateConfiguration(root)));
        var diagnostic = new DiagnosticClient(new SdkCopilotRuntimeClient(sdk));
        await using var session = new CopilotSubscriptionAgentSession(new NoTools(),
            new(CopilotSubscriptionAgentProvider.Id, "auto", root)
            {
                PersistProviderSession = true,
                ReservedProviderSessionId = sessionId,
                ConversationId = Guid.NewGuid(),
            }, diagnostic, store);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var events = new List<AgentProviderEvent>();
            var request = new AgentTurnRequest(AgentTurnId.New(),
                "Responda apenas OK. Contexto sintético: {\"editor\":\"db.synthetic.find({})\"}.", "synthetic-tab", 1)
            {
                SystemPrompt = "Você é um assistente em um teste sintético. Nenhum arquivo, banco ou ferramenta deve ser acessado.",
                Plan = new(AgentOperationMode.Agent, [], [], [], [], AgentProposalHandling.Disabled,
                    false, AgentConfirmationCategories.None),
            };
            await foreach (var item in session.RunTurnAsync(request, timeout.Token)) events.Add(item);
            TestContext.Progress.WriteLine($"Última fase RPC: {diagnostic.Stage}; exceção: {diagnostic.Failure?.GetType().Name ?? "none"}; HResult: {diagnostic.Failure?.HResult ?? 0}; causa: {FailureCategory(diagnostic.Failure)}.");
            Assert.That(events.Where(item => item.Kind == AgentEventKind.AgentError).Select(item => item.Text), Is.Empty,
                $"Fase {diagnostic.Stage}; exceção {diagnostic.Failure?.GetType().Name}; causa {FailureCategory(diagnostic.Failure)}.");
            Assert.That(events.Any(item => item.Kind == AgentEventKind.MessageDelta && !string.IsNullOrWhiteSpace(item.Text)), Is.True);
        }
        finally
        {
            await session.DisposeAsync();
            await store.DeleteSessionAsync(sessionId, CancellationToken.None);
            await store.DisposeAsync();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static string FailureCategory(Exception? failure)
    {
        if (failure is null) return "none";
        var message = failure.Message;
        foreach (var category in new[] { "permission", "trust", "hook", "session", "model", "agent", "sqlite", "access", "protocol", "method", "authentication" })
            if (message.Contains(category, StringComparison.OrdinalIgnoreCase)) return category;
        return "unclassified";
    }

    private sealed class NoTools : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => null;
        public string? GetOutputSchemaJson(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No tool is planned.");
    }

    private sealed class DiagnosticClient(ICopilotRuntimeClient inner) : ICopilotRuntimeClient
    {
        public string Stage { get; private set; } = "pending";
        public Exception? Failure { get; private set; }
        public async Task<T> Capture<T>(string stage, Func<Task<T>> operation)
        {
            Stage = stage;
            try { return await operation(); }
            catch (Exception exception) { Failure = exception; throw; }
        }
        public async Task Capture(string stage, Func<Task> operation) =>
            await Capture(stage, async () => { await operation(); return true; });
        public Task StartAsync(CancellationToken token) => Capture("start", () => inner.StartAsync(token));
        public Task<CopilotRuntimeAuthentication> GetAuthStatusAsync(CancellationToken token) => Capture("auth", () => inner.GetAuthStatusAsync(token));
        public Task<IReadOnlyList<string>> ListModelIdsAsync(CancellationToken token) => Capture("models", () => inner.ListModelIdsAsync(token));
        public Task<bool> HasSessionAsync(string id, CancellationToken token) => Capture("metadata", () => inner.HasSessionAsync(id, token));
        public async Task<ICopilotRuntimeSession> CreateSessionAsync(SessionConfig config, CancellationToken token) =>
            new DiagnosticSession(this, await Capture("create", () => inner.CreateSessionAsync(config, token)));
        public async Task<ICopilotRuntimeSession> ResumeSessionAsync(string id, ResumeSessionConfig config, CancellationToken token) =>
            new DiagnosticSession(this, await Capture("resume", () => inner.ResumeSessionAsync(id, config, token)));
        public Task DeleteSessionAsync(string id, CancellationToken token) => inner.DeleteSessionAsync(id, token);
        public ValueTask DisposeAsync() => inner.DisposeAsync();

        private sealed class DiagnosticSession(DiagnosticClient owner, ICopilotRuntimeSession innerSession) : ICopilotRuntimeSession
        {
            public string SessionId => innerSession.SessionId;
            public Task<bool> RestrictBuiltInAgentsAsync(CancellationToken token) => owner.Capture("restrict", () => innerSession.RestrictBuiltInAgentsAsync(token));
            public IDisposable Subscribe(Action<SessionEvent> handler) => innerSession.Subscribe(handler);
            public Task SendAsync(MessageOptions message, CancellationToken token) => owner.Capture("send", () => innerSession.SendAsync(message, token));
            public Task AbortAsync(CancellationToken token) => innerSession.AbortAsync(token);
            public Task SubmitToolResultAsync(string requestId, ToolResultObject result, CancellationToken token) => innerSession.SubmitToolResultAsync(requestId, result, token);
            public ValueTask DisposeAsync() => innerSession.DisposeAsync();
        }
    }
}
