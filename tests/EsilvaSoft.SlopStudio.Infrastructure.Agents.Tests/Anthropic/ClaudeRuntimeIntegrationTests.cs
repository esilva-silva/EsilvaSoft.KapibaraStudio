using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests.Anthropic;

/// <summary>
/// O adapter dentro do <see cref="AgentRuntime"/> real: o runtime publica o pedido de tool, um broker confiável
/// (autoridade sintética) responde pelo runtime, e o adapter continua o turno. Offline, sem MongoDB nem conta.
/// </summary>
[TestFixture]
[CancelAfter(30_000)]
public sealed class ClaudeRuntimeIntegrationTests
{
    private sealed class TrustingBroker : IAgentInteractionAuthority
    {
        public int ToolRequests;
        public Task<bool> ValidateToolRequestAsync(AgentSessionId sessionId, AgentTurnId turnId, AgentToolCallId toolCallId,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref ToolRequests);
            return Task.FromResult(true);
        }

        public Task<bool> ValidateToolResultAsync(AgentToolResult result, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> ValidateApprovalRequestAsync(AgentSessionId sessionId, AgentTurnId turnId, AgentApprovalId approvalId,
            CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> ValidateApprovalDecisionAsync(AgentApprovalDecision decision, CancellationToken cancellationToken) =>
            Task.FromResult(false);
    }

    [Test]
    public async Task RuntimeLoopDeliversBrokerResultAndCompletesTheTurn()
    {
        var first = new SseBuilder().Start().Text("Consultando.").ToolUse("toolu_rt", "list_connections", "{}").Stop("tool_use").Build();
        var second = new SseBuilder().Start().Text("Pronto.").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(first, chunkSize: 11).EnqueueSse(second);
        using var provider = ClaudeFixture.Provider(handler);
        var broker = new TrustingBroker();
        await using var runtime = new AgentRuntime([provider], broker);
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);
        var request = new AgentTurnRequest(AgentTurnId.New(), "Quais conexões existem?", "tab-1", 3);
        var events = new List<AgentEvent>();

        await foreach (var item in runtime.RunTurnAsync(sessionId, request, CancellationToken.None))
        {
            events.Add(item);
            if (item.Kind == AgentEventKind.ToolRequested)
            {
                var result = new AgentToolResult(sessionId, request.TurnId, item.ToolCallId!.Value,
                    AgentToolResultStatus.Succeeded, """{"connections":[]}""");
                _ = Task.Run(() => runtime.SubmitToolResultAsync(result, CancellationToken.None));
            }
        }

        Assert.Multiple(() =>
        {
            Assert.That(broker.ToolRequests, Is.EqualTo(1));
            Assert.That(events.Last().Kind, Is.EqualTo(AgentEventKind.TaskCompleted));
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
            Assert.That(events.Any(static e => e.Kind == AgentEventKind.ToolCompleted), Is.True);
            Assert.That(string.Concat(events.Where(static e => e.Kind == AgentEventKind.MessageDelta).Select(static e => e.Text)),
                Is.EqualTo("Consultando.Pronto."));
            Assert.That(events.Select(static e => e.Sequence), Is.Ordered.Ascending);
        });
        var toolResult = handler.Requests.ElementAt(1).Json.GetProperty("messages")[2].GetProperty("content")[0];
        Assert.That(toolResult.GetProperty("content").GetString(), Is.EqualTo("""{"connections":[]}"""));
    }

    [Test]
    public async Task RuntimeCancellationInterruptsTheAdapterAndReportsCancelled()
    {
        using var handler = new FakeClaudeHandler().EnqueueSse(new SseBuilder().Start().Text("longo...").Build(), hangAtEnd: true);
        using var provider = ClaudeFixture.Provider(handler);
        await using var runtime = new AgentRuntime([provider], new TrustingBroker());
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);
        var request = new AgentTurnRequest(AgentTurnId.New(), "Explique.", "tab-1", 1);
        var events = new List<AgentEvent>();

        await foreach (var item in runtime.RunTurnAsync(sessionId, request, CancellationToken.None))
        {
            events.Add(item);
            if (item.Kind == AgentEventKind.MessageDelta)
            {
                await runtime.CancelTurnAsync(sessionId, request.TurnId, CancellationToken.None);
            }
        }

        Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Cancelled));
        await handler.RequestCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task UnavailableProviderDoesNotBreakTheRuntime()
    {
        using var handler = new FakeClaudeHandler();
        using var provider = ClaudeFixture.Provider(handler, ClaudeFixture.Options() with { ApiKeyReference = null });
        await using var runtime = new AgentRuntime([provider], new TrustingBroker());

        var failure = Assert.ThrowsAsync<AgentRuntimeException>(() =>
            runtime.StartSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None));

        Assert.That(failure!.Code, Is.EqualTo("ProviderUnavailable"));
        Assert.That(handler.Requests, Is.Empty);
    }
}

/// <summary>
/// Homologação real opcional. Só roda explicitamente e com <c>SLOP_ANTHROPIC_API_KEY</c> definida por quem autorizou
/// a conta; sem a variável o teste é ignorado. A chave vai direto ao provider por um credential provider em memória,
/// nunca ao cofre, disco ou saída do teste. Gera custo na conta do usuário.
/// </summary>
[TestFixture]
[Explicit("Chamada real à Claude API; exige SLOP_ANTHROPIC_API_KEY autorizada.")]
[Category("ClaudeReal")]
public sealed class ClaudeRealApiTests
{
    private sealed class EnvironmentCredential(string key) : IAgentCredentialProvider
    {
        public Task<SecretStoreResult<string>> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Success(key));
    }

    [Test]
    [CancelAfter(120_000)]
    public async Task RealStreamingTurnCompletes()
    {
        var key = Environment.GetEnvironmentVariable("SLOP_ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Assert.Ignore("SLOP_ANTHROPIC_API_KEY ausente: homologação real pendente.");
        }

        var model = Environment.GetEnvironmentVariable("SLOP_ANTHROPIC_MODEL") ?? ClaudeAgentProviderOptions.DefaultModelId;
        var options = new ClaudeAgentProviderOptions
        {
            ApiKeyReference = new SecretReference(Guid.NewGuid()),
            AllowedModelIds = [model],
            DefaultModel = model,
            Budget = ClaudeAgentBudget.Default with { MaxOutputTokensPerRequest = 256, MaxRequestsPerTurn = 1 },
        };
        using var provider = new ClaudeAgentProvider(new EnvironmentCredential(key!), options);
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);
        var events = new List<AgentProviderEvent>();

        await foreach (var item in session.RunTurnAsync(new AgentTurnRequest(AgentTurnId.New(),
                           "Responda apenas com a palavra: pronto", "tab-real", 1), CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.That(events.Where(static e => e.Kind == AgentEventKind.AgentError).Select(static e => e.Text), Is.Empty);
        Assert.That(events.Count(static e => e.Kind == AgentEventKind.MessageDelta), Is.GreaterThan(0));
    }
}
