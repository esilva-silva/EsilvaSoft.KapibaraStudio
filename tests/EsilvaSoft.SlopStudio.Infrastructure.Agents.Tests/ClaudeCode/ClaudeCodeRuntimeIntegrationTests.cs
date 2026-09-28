using System.Text.Json;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests.ClaudeCode;

/// <summary>
/// P7-CL3-05: o adapter do modo Claude (assinatura) dentro do <see cref="AgentRuntime"/> real, contra o CLI falso.
/// Leituras nativas chegam ao stream normalizado só com nome e estado; o cancelamento distingue antes/depois do stdin;
/// códigos tipados do adapter chegam ao evento. Não homologa conta nem binário real (GCL-8).
/// </summary>
[TestFixture]
[CancelAfter(120_000)]
public sealed class ClaudeCodeRuntimeIntegrationTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(30);

    private static async Task<List<AgentEvent>> CollectAsync(IAsyncEnumerable<AgentEvent> stream)
    {
        var events = new List<AgentEvent>();
        await foreach (var item in stream)
        {
            events.Add(item);
        }

        return events;
    }

    private static AgentTurnRequest Request(string message = "Responda apenas: ok") => ClaudeCodeFixture.Request(message);

    [Test]
    public async Task NativeReadsReachTheRuntimeStreamAsObservationsWithNameAndStateOnly()
    {
        using var fixture = new ClaudeCodeFixture().Turn("read-tools.jsonl");
        await using var runtime = new AgentRuntime([fixture.Provider()]);
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);

        var events = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(), CancellationToken.None)).WaitAsync(Wait);
        var tools = events.Where(static e => e.ToolCallId is not null).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(tools.Where(static e => e.Kind is AgentEventKind.ToolCompleted or AgentEventKind.ToolFailed)
                .Select(static e => (e.ToolName, e.ToolStatus)), Is.EqualTo(new (string?, AgentToolResultStatus?)[]
            {
                ("Read", AgentToolResultStatus.Succeeded), ("Read", AgentToolResultStatus.Failed), ("Glob", AgentToolResultStatus.Succeeded),
            }));
            Assert.That(tools.Count(static e => e.Kind == AgentEventKind.ToolRequested), Is.EqualTo(3));
            Assert.That(tools.Select(static e => e.ToolOrigin), Is.All.EqualTo(AgentToolOrigin.ProviderObserved));
            Assert.That(tools.Select(static e => e.ToolDestination), Is.All.EqualTo(AgentDataDestinationKind.External));
            Assert.That(tools.Single(static e => e.Kind == AgentEventKind.ToolFailed).ErrorCode, Is.EqualTo(ClaudeCodeErrorCodes.NativeToolFailed));
            Assert.That(JsonSerializer.Serialize(events), Does.Not.Contain("CANARIO-A1").And.Not.Contain("a.txt"),
                "Caminho e conteúdo lidos nunca chegam ao stream.");
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        });
    }

    [Test]
    public async Task CancellingAfterThePromptWasWrittenEndsTheTurnAsOutcomeUnknownAndTheNextTurnResumes()
    {
        using var fixture = new ClaudeCodeFixture().Turn("cancel-with-child.jsonl", resumeFixture: "basic-turn2-resume.jsonl");
        await using var runtime = new AgentRuntime([fixture.Provider()]);
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        var request = Request("Rode algo longo");

        var run = CollectAsync(runtime.RunTurnAsync(sessionId, request, CancellationToken.None));
        await WaitUntilAsync(() => fixture.Log().Any(static e => e.GetProperty("event").GetString() == "child"));
        await runtime.CancelTurnAsync(sessionId, request.TurnId, CancellationToken.None);
        var events = await run.WaitAsync(Wait);

        Assert.Multiple(() =>
        {
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown), "O prompt saiu: nada é desfeito nem confirmado.");
            Assert.That(events.Single(static e => e.Kind == AgentEventKind.AgentError).ErrorCode, Is.EqualTo("CancelledAfterSend"));
        });

        var next = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(), CancellationToken.None)).WaitAsync(Wait);
        Assert.Multiple(() =>
        {
            Assert.That(next.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
            Assert.That(fixture.TurnInvocations()[^1], Does.Contain("--resume"), "Turno incerto continua retomável.");
        });
    }

    [Test]
    public async Task CancellingBeforeThePromptIsWrittenIsACleanCancellation()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        await using var runtime = new AgentRuntime([fixture.Provider()]);
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        fixture.AuthDelay(10_000);
        var request = Request();

        var run = CollectAsync(runtime.RunTurnAsync(sessionId, request, CancellationToken.None));
        await WaitUntilAsync(() => fixture.Invocations().Count(static argv => argv.Contains("status")) == 2);
        await runtime.CancelTurnAsync(sessionId, request.TurnId, CancellationToken.None);
        var events = await run.WaitAsync(Wait);

        Assert.Multiple(() =>
        {
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Cancelled));
            Assert.That(events.Any(static e => e.Kind == AgentEventKind.AgentError), Is.False);
            Assert.That(fixture.TurnInvocations(), Is.Empty);
        });
    }

    [Test]
    public async Task TypedAdapterErrorCodeReachesTheEventInsteadOfAGenericProviderError()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        await using var runtime = new AgentRuntime([fixture.Provider()]);
        var sessionId = await runtime.StartSessionAsync(new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        fixture.AuthStatus("""{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","apiKeySource":"ANTHROPIC_API_KEY","subscriptionType":null}""");

        var events = await CollectAsync(runtime.RunTurnAsync(sessionId, Request(), CancellationToken.None)).WaitAsync(Wait);

        Assert.Multiple(() =>
        {
            Assert.That(events.Last().Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
            Assert.That(events.Single(static e => e.Kind == AgentEventKind.AgentError).ErrorCode,
                Is.EqualTo(ClaudeCodeErrorCodes.NonSubscriptionAuthentication));
            Assert.That(fixture.TurnInvocations(), Is.Empty);
        });
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail("Condição não atingida.");
            }

            await Task.Delay(50);
        }
    }
}
