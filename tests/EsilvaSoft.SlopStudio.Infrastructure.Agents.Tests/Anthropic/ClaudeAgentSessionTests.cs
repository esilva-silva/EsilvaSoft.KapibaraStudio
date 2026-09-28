using System.Net;
using System.Text.Json;
using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests.Anthropic;

/// <summary>
/// Contrato do adapter Claude contra streams SSE sintéticos servidos por um handler HTTP falso (sem rede, sem conta).
/// Não homologa a Claude API real, modelo, conta ou cobrança.
/// </summary>
[TestFixture]
[CancelAfter(30_000)]
public sealed class ClaudeAgentSessionTests
{
    private static readonly string[] ParallelToolIds = ["toolu_a", "toolu_b"];
    private static readonly string[] CommittedHistory = ["pergunta 1", "resposta 1", "pergunta 3"];

    private static AgentTurnRequest Turn(string message = "Liste as coleções.", string? context = null) =>
        new(AgentTurnId.New(), message, "tab-1", 1, context);

    private static async Task<IAgentSession> SessionAsync(ClaudeAgentProvider provider, string? model = null) =>
        await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id, model), CancellationToken.None);

    /// <summary>Consome o turno; em cada ToolRequested entrega um resultado como o runtime faria, fora do stream.</summary>
    private static async Task<List<AgentProviderEvent>> RunAsync(
        IAgentSession session, AgentTurnRequest request, Func<AgentProviderEvent, AgentToolResult?>? onTool = null,
        CancellationToken cancellationToken = default)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(request, cancellationToken))
        {
            events.Add(item);
            if (item.Kind == AgentEventKind.ToolRequested && onTool?.Invoke(item) is { } result)
            {
                _ = Task.Run(() => session.SubmitToolResultAsync(result, CancellationToken.None), CancellationToken.None);
            }
        }

        return events;
    }

    private static string Text(IEnumerable<AgentProviderEvent> events) =>
        string.Concat(events.Where(static e => e.Kind == AgentEventKind.MessageDelta).Select(static e => e.Text));

    private static AgentToolResult Result(AgentTurnRequest request, AgentProviderEvent tool, AgentToolResultStatus status,
        string? data = null, string? code = null) =>
        new(AgentSessionId.New(), request.TurnId, tool.ToolCallId!.Value, status, data, code);

    [Test]
    public async Task FragmentedStreamIsTranslatedInOrderWithOneMessage()
    {
        // Pedaços de 5 bytes cortam linhas SSE, JSON e o caractere multibyte "á".
        var sse = new SseBuilder().Start().Ping().Text("Olá", ", mun", "do!").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(sse, chunkSize: 5);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);

        var events = await RunAsync(session, Turn());

        Assert.That(events.Select(static e => e.Kind), Is.EqualTo(new[]
        {
            AgentEventKind.MessageStarted, AgentEventKind.MessageDelta, AgentEventKind.MessageDelta, AgentEventKind.MessageDelta,
            AgentEventKind.MessageCompleted,
        }));
        Assert.That(Text(events), Is.EqualTo("Olá, mundo!"));
        Assert.That(events.Select(static e => e.MessageId).Distinct().Count(), Is.EqualTo(1));
        Assert.That(events.All(static e => e.MessageId!.Value.IsValid), Is.True);
    }

    [Test]
    public async Task RequestUsesApiKeyHeaderConfiguredModelAndNoAmbientCredentials()
    {
        var sse = new SseBuilder().Start().Text("ok").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(sse);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider, "claude-sonnet-5");

        await RunAsync(session, Turn("Pergunta", context: "db.users"));

        var request = handler.Requests.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Uri!.Host, Is.EqualTo("claude.test.invalid"));
            Assert.That(request.Uri.AbsolutePath, Is.EqualTo("/v1/messages"));
            Assert.That(request.Headers["x-api-key"], Is.EqualTo(ClaudeFixture.SyntheticKey));
            Assert.That(request.Headers.ContainsKey("authorization"), Is.False, "Sem token OAuth/Claude Code.");
            Assert.That(request.Body, Does.Not.Contain(ClaudeFixture.SyntheticKey));
            Assert.That(request.Json.GetProperty("model").GetString(), Is.EqualTo("claude-sonnet-5"));
            Assert.That(request.Json.GetProperty("stream").GetBoolean(), Is.True);
            Assert.That(request.Json.GetProperty("max_tokens").GetInt32(), Is.EqualTo(ClaudeAgentBudget.Default.MaxOutputTokensPerRequest));
            Assert.That(request.Json.TryGetProperty("tools", out _), Is.False, "Sem registry não há tools declaradas.");
            var content = request.Json.GetProperty("messages")[0].GetProperty("content");
            Assert.That(content[0].GetProperty("text").GetString(), Is.EqualTo("Pergunta"));
            Assert.That(content[1].GetProperty("text").GetString(), Does.Contain("<contexto_autorizado>").And.Contain("db.users"));
        });
    }

    [Test]
    public async Task ToolUseIsRequestedOnlyAfterCompleteArgumentsAndResultGoesBackToModel()
    {
        var first = new SseBuilder().Start().Thinking("sig-opaque").Text("Vou listar.")
            .ToolUse("toolu_01", "list_databases", "{\"conn", "ection_id\":", " \"c-1\"}").Stop("tool_use").Build();
        var second = new SseBuilder().Start().Text("Há 2 bancos.").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(first, chunkSize: 7).EnqueueSse(second);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);
        var request = Turn();

        var events = await RunAsync(session, request,
            tool => Result(request, tool, AgentToolResultStatus.Succeeded, """{"databases":["a","b"]}"""));

        var tool = events.Single(static e => e.Kind == AgentEventKind.ToolRequested);
        Assert.Multiple(() =>
        {
            Assert.That(tool.ToolName, Is.EqualTo("list_databases"));
            Assert.That(tool.ArgumentsJson, Is.EqualTo("{\"connection_id\": \"c-1\"}"));
            Assert.That(tool.ToolCallId!.Value.IsValid, Is.True, "ID nativo não vira protocolo público.");
            // A mensagem visível fecha antes do pedido de tool; o texto posterior abre outra mensagem.
            var toolIndex = events.IndexOf(tool);
            Assert.That(events.Take(toolIndex).Count(static e => e.Kind == AgentEventKind.MessageCompleted), Is.EqualTo(1));
            Assert.That(Text(events), Is.EqualTo("Vou listar.Há 2 bancos."));
            Assert.That(events.Any(static e => e.Kind == AgentEventKind.AgentError), Is.False);
        });

        var followUp = handler.Requests.ElementAt(1).Json.GetProperty("messages");
        Assert.That(followUp.GetArrayLength(), Is.EqualTo(3));
        var assistant = followUp[1].GetProperty("content");
        Assert.Multiple(() =>
        {
            Assert.That(assistant[0].GetProperty("type").GetString(), Is.EqualTo("thinking"));
            Assert.That(assistant[0].GetProperty("signature").GetString(), Is.EqualTo("sig-opaque"));
            Assert.That(assistant[2].GetProperty("type").GetString(), Is.EqualTo("tool_use"));
            Assert.That(assistant[2].GetProperty("id").GetString(), Is.EqualTo("toolu_01"));
            Assert.That(assistant[2].GetProperty("input").GetProperty("connection_id").GetString(), Is.EqualTo("c-1"));
            var result = followUp[2].GetProperty("content")[0];
            Assert.That(result.GetProperty("type").GetString(), Is.EqualTo("tool_result"));
            Assert.That(result.GetProperty("tool_use_id").GetString(), Is.EqualTo("toolu_01"));
            Assert.That(result.GetProperty("content").GetString(), Is.EqualTo("""{"databases":["a","b"]}"""));
            Assert.That(result.TryGetProperty("is_error", out var isError) && isError.GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task DeniedToolResultReturnsOnlyStatusAndSafeCode()
    {
        var first = new SseBuilder().Start().ToolUse("toolu_02", "mongo_find", "{}").Stop("tool_use").Build();
        var second = new SseBuilder().Start().Text("Sem permissão.").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(first).EnqueueSse(second);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);
        var request = Turn();

        await RunAsync(session, request, tool => Result(request, tool, AgentToolResultStatus.Denied,
            data: "segredo que não deveria sair", code: "PermissionDenied"));

        var result = handler.Requests.ElementAt(1).Json.GetProperty("messages")[2].GetProperty("content")[0];
        Assert.That(result.GetProperty("is_error").GetBoolean(), Is.True);
        Assert.That(result.GetProperty("content").GetString(), Is.EqualTo("""{"status":"Denied","error":"PermissionDenied"}"""));
    }

    [Test]
    public async Task MultipleToolUsesInOneMessageReturnAllResultsInOneUserMessage()
    {
        var first = new SseBuilder().Start().ToolUse("toolu_a", "list_connections", "{}")
            .ToolUse("toolu_b", "list_databases", "{\"connection_id\":\"x\"}").Stop("tool_use").Build();
        var second = new SseBuilder().Start().Text("ok").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(first).EnqueueSse(second);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);
        var request = Turn();

        var events = await RunAsync(session, request, tool => Result(request, tool, AgentToolResultStatus.Succeeded, "{}"));

        Assert.That(events.Count(static e => e.Kind == AgentEventKind.ToolRequested), Is.EqualTo(2));
        var results = handler.Requests.ElementAt(1).Json.GetProperty("messages")[2].GetProperty("content");
        Assert.That(results.EnumerateArray().Select(static r => r.GetProperty("tool_use_id").GetString()),
            Is.EqualTo(ParallelToolIds));
    }

    [Test]
    public async Task InvalidToolJsonIsNeverRequestedAndReturnsErrorResultToModel()
    {
        var first = new SseBuilder().Start().ToolUse("toolu_bad", "mongo_find", "{\"filter\": {", "\"a\": ").Stop("tool_use").Build();
        var second = new SseBuilder().Start().Text("Corrigindo.").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(first).EnqueueSse(second);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);

        var events = await RunAsync(session, Turn());

        Assert.That(events.Any(static e => e.Kind == AgentEventKind.ToolRequested), Is.False);
        var result = handler.Requests.ElementAt(1).Json.GetProperty("messages")[2].GetProperty("content")[0];
        Assert.That(result.GetProperty("is_error").GetBoolean(), Is.True);
        Assert.That(result.GetProperty("content").GetString(), Does.Contain(ClaudeErrorCodes.InvalidToolArguments));
    }

    [Test]
    public async Task StreamCutBeforeMessageStopNeverRequestsTheTool()
    {
        // Bloco de tool sem content_block_stop nem message_stop: conexão caiu no meio dos argumentos.
        var cut = new SseBuilder().Start().ToolUse("toolu_cut", "mongo_find", false, "{\"filter\":").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(cut);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);

        var events = await RunAsync(session, Turn());

        Assert.That(events.Any(static e => e.Kind == AgentEventKind.ToolRequested), Is.False);
        Assert.That(events.Last().Kind, Is.EqualTo(AgentEventKind.AgentError));
        Assert.That(events.Last().Text, Is.EqualTo(ClaudeErrorCodes.ProviderStreamIncomplete));
    }

    [Test]
    public async Task MaxTokensStopWithToolUseDoesNotRequestTheTool()
    {
        var sse = new SseBuilder().Start().ToolUse("toolu_mt", "mongo_find", "{}").Stop("max_tokens").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(sse);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);

        var events = await RunAsync(session, Turn());

        Assert.That(events.Any(static e => e.Kind == AgentEventKind.ToolRequested), Is.False);
        Assert.That(events.Last().Text, Is.EqualTo(ClaudeErrorCodes.OutputTokenLimit));
    }

    [Test]
    public async Task InvalidKeyReturns401OnceThenProviderIsUnavailableWithoutRetry()
    {
        using var handler = new FakeClaudeHandler().EnqueueError(HttpStatusCode.Unauthorized, "authentication_error");
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);

        var events = await RunAsync(session, Turn());
        var second = await RunAsync(session, Turn());

        Assert.Multiple(async () =>
        {
            Assert.That(events.Single().Kind, Is.EqualTo(AgentEventKind.AgentError));
            Assert.That(events.Single().Text, Is.EqualTo(ClaudeErrorCodes.AuthenticationFailed));
            Assert.That(second.Single().Text, Is.EqualTo(ClaudeErrorCodes.AuthenticationFailed));
            Assert.That(handler.Requests, Has.Count.EqualTo(1), "Chave recusada não entra em loop de retries.");
            var availability = await provider.GetAvailabilityAsync();
            Assert.That(availability.IsAvailable, Is.False);
            Assert.That(availability.Reason, Is.EqualTo(ClaudeAgentUnavailableReason.CredentialRejected));
            Assert.That(availability.Capabilities, Is.EqualTo(AgentProviderCapabilities.None with { UsesNetwork = true }));
        });
        var failure = Assert.ThrowsAsync<ClaudeProviderUnavailableException>(() => SessionAsync(provider));
        Assert.That(failure!.Reason, Is.EqualTo(ClaudeAgentUnavailableReason.CredentialRejected));
        Assert.That(failure.Message, Does.Not.Contain(ClaudeFixture.SyntheticKey));
    }

    [TestCase(HttpStatusCode.TooManyRequests, "rate_limit_error", ClaudeErrorCodes.RateLimited)]
    [TestCase((HttpStatusCode)529, "overloaded_error", ClaudeErrorCodes.ProviderUnavailable)]
    [TestCase(HttpStatusCode.InternalServerError, "api_error", ClaudeErrorCodes.ProviderUnavailable)]
    [TestCase(HttpStatusCode.BadRequest, "invalid_request_error", ClaudeErrorCodes.ProviderRequestRejected)]
    [TestCase(HttpStatusCode.Forbidden, "permission_error", ClaudeErrorCodes.ProviderPermissionDenied)]
    public async Task HttpFailuresMapToSafeCodesWithoutAutomaticRetry(HttpStatusCode status, string type, string expected)
    {
        using var handler = new FakeClaudeHandler().EnqueueError(status, type);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);

        var events = await RunAsync(session, Turn());

        Assert.That(events.Single().Text, Is.EqualTo(expected));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
        Assert.That((await provider.GetAvailabilityAsync()).IsAvailable, Is.True, "Falha transitória não invalida a chave.");
    }

    [Test]
    public async Task MissingModelMarksSessionModelUnavailable()
    {
        using var handler = new FakeClaudeHandler().EnqueueError(HttpStatusCode.NotFound, "not_found_error");
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);

        var first = await RunAsync(session, Turn());
        var second = await RunAsync(session, Turn());

        Assert.That(first.Single().Text, Is.EqualTo(ClaudeErrorCodes.ModelUnavailable));
        Assert.That(second.Single().Text, Is.EqualTo(ClaudeErrorCodes.ModelUnavailable));
        Assert.That(handler.Requests, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ErrorEventInsideTheStreamEndsTheTurnWithSafeCode()
    {
        var sse = new SseBuilder().Start().Text("parcial").Error("overloaded_error").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(sse);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);

        var events = await RunAsync(session, Turn());

        Assert.That(events.Last().Kind, Is.EqualTo(AgentEventKind.AgentError));
        Assert.That(events.Last().Text, Is.AnyOf(ClaudeErrorCodes.ProviderStreamError, ClaudeErrorCodes.ProviderUnavailable));
        Assert.That(events.Last().Text, Does.Not.Contain("synthetic"));
    }

    [Test]
    public async Task ServerWithoutFirstEventTimesOut()
    {
        using var handler = new FakeClaudeHandler().EnqueueHang();
        var budget = ClaudeFixture.Options().Budget with { FirstEventTimeout = TimeSpan.FromMilliseconds(300) };
        using var provider = ClaudeFixture.Provider(handler, ClaudeFixture.Options(budget));
        await using var session = await SessionAsync(provider);

        var events = await RunAsync(session, Turn());

        Assert.That(events.Single().Text, Is.EqualTo(ClaudeErrorCodes.ProviderTimeout));
        await handler.RequestCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Test]
    public async Task StreamIdleAfterFirstEventTimesOut()
    {
        var partial = new SseBuilder().Start().Text("começo").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(partial, hangAtEnd: true);
        var budget = ClaudeFixture.Options().Budget with { StreamIdleTimeout = TimeSpan.FromMilliseconds(300) };
        using var provider = ClaudeFixture.Provider(handler, ClaudeFixture.Options(budget));
        await using var session = await SessionAsync(provider);

        var events = await RunAsync(session, Turn());

        Assert.That(Text(events), Is.EqualTo("começo"));
        Assert.That(events.Last().Text, Is.EqualTo(ClaudeErrorCodes.ProviderTimeout));
        Assert.That(events.Count(static e => e.Kind == AgentEventKind.MessageCompleted), Is.EqualTo(1));
    }

    [Test]
    public async Task StreamIdleWatchdogDoesNotRunWhileTheTurnWaitsForAToolResult()
    {
        // A human approval can keep a tool result pending far longer than the idle window of a streamed round.
        var first = new SseBuilder().Start().ToolUse("toolu_idle", "update_one", "{}").Stop("tool_use").Build();
        var second = new SseBuilder().Start().Text("Atualizado.").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(first).EnqueueSse(second);
        var budget = ClaudeFixture.Options().Budget with { StreamIdleTimeout = TimeSpan.FromMilliseconds(200) };
        using var provider = ClaudeFixture.Provider(handler, ClaudeFixture.Options(budget));
        await using var session = await SessionAsync(provider);
        var request = Turn();

        var events = await RunAsync(session, request, tool =>
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(800));
                await session.SubmitToolResultAsync(Result(request, tool, AgentToolResultStatus.Succeeded, "{\"modified\":1}"),
                    CancellationToken.None);
            });
            return null;
        });

        Assert.That(events.Any(static e => e.Kind == AgentEventKind.AgentError), Is.False);
        Assert.That(Text(events), Is.EqualTo("Atualizado."));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public void SystemPromptAllowsDataChangesOnlyThroughDeclaredToolsWithHumanApproval()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ClaudeAgentSession.SystemPrompt, Does.Not.Contain("não altera dados"),
                "O prompt não pode negar escritas que o aplicativo pode oferecer com aprovação.");
            Assert.That(ClaudeAgentSession.SystemPrompt, Does.Contain("ferramenta de escrita que o aplicativo tenha declarado"));
            Assert.That(ClaudeAgentSession.SystemPrompt, Does.Contain("aprovação humana explícita"));
            Assert.That(ClaudeAgentSession.SystemPrompt, Does.Contain("não executa comandos nem edita arquivos"));
            Assert.That(ClaudeAgentSession.SystemPrompt, Does.Contain("Nunca afirme que uma alteração foi aplicada"));
        });
    }

    [Test]
    public async Task CancelTurnStopsOnlyThatSessionAndAbortsTheHttpStream()
    {
        using var handlerA = new FakeClaudeHandler().EnqueueSse(new SseBuilder().Start().Text("A...").Build(), hangAtEnd: true);
        using var handlerB = new FakeClaudeHandler().EnqueueSse(new SseBuilder().Start().Text("B concluído").Stop("end_turn").Build(),
            chunkSize: 3);
        using var providerA = ClaudeFixture.Provider(handlerA);
        using var providerB = ClaudeFixture.Provider(handlerB);
        await using var sessionA = await SessionAsync(providerA);
        await using var sessionB = await SessionAsync(providerB);
        var turnA = Turn();
        var eventsA = new List<AgentProviderEvent>();

        var runA = Task.Run(async () =>
        {
            await foreach (var item in sessionA.RunTurnAsync(turnA, CancellationToken.None))
            {
                eventsA.Add(item);
                if (item.Kind == AgentEventKind.MessageDelta)
                {
                    await sessionA.CancelTurnAsync(turnA.TurnId, CancellationToken.None);
                }
            }
        });
        var eventsB = await RunAsync(sessionB, Turn());
        await runA.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Multiple(async () =>
        {
            Assert.That(eventsA.Any(static e => e.Kind == AgentEventKind.AgentError), Is.False, "Cancelamento não vira erro.");
            Assert.That(Text(eventsB), Is.EqualTo("B concluído"));
            Assert.That(eventsB.Any(static e => e.Kind == AgentEventKind.AgentError), Is.False);
            await handlerA.RequestCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        });
    }

    [Test]
    public async Task ConsumerTokenCancellationWhileWaitingForToolResultEndsTheTurn()
    {
        var first = new SseBuilder().Start().ToolUse("toolu_w", "list_connections", "{}").Stop("tool_use").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(first);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);
        using var cts = new CancellationTokenSource();
        var request = Turn();
        AgentProviderEvent? tool = null;

        var run = RunAsync(session, request, item =>
        {
            tool = item;
            cts.Cancel();
            return null;
        }, cts.Token);

        // O adapter encerra em silêncio (sem AgentError): o terminal Cancelled pertence ao runtime.
        var events = await run.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.That(events.Any(static e => e.Kind == AgentEventKind.AgentError), Is.False);
        Assert.That(handler.Requests, Has.Count.EqualTo(1), "Nenhuma requisição nova após cancelar.");
        // Resultado tardio do turno cancelado é recusado.
        Assert.That(() => session.SubmitToolResultAsync(Result(request, tool!, AgentToolResultStatus.Succeeded, "{}"), CancellationToken.None),
            Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task DuplicateOrUnknownToolResultIsRejected()
    {
        var first = new SseBuilder().Start().ToolUse("toolu_d", "list_connections", "{}").Stop("tool_use").Build();
        var second = new SseBuilder().Start().Text("ok").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(first).EnqueueSse(second);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);
        var request = Turn();
        var rejections = 0;

        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None))
        {
            if (item.Kind != AgentEventKind.ToolRequested)
            {
                continue;
            }

            var result = Result(request, item, AgentToolResultStatus.Succeeded, "{}");
            Assert.That(() => session.SubmitToolResultAsync(result with { ToolCallId = AgentToolCallId.New() }, CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());
            Assert.That(() => session.SubmitToolResultAsync(result with { TurnId = AgentTurnId.New() }, CancellationToken.None),
                Throws.InstanceOf<InvalidOperationException>());
            await session.SubmitToolResultAsync(result, CancellationToken.None);
            try
            {
                await session.SubmitToolResultAsync(result, CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                rejections++;
            }
        }

        Assert.That(rejections, Is.EqualTo(1));
        Assert.That(handler.Requests, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task ApprovalsAreNotSupportedByTheAdapter()
    {
        using var handler = new FakeClaudeHandler();
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);

        Assert.That(() => session.SubmitApprovalAsync(new AgentApprovalDecision(AgentSessionId.New(), AgentTurnId.New(),
            AgentApprovalId.New(), AgentApprovalOutcome.Granted), CancellationToken.None), Throws.InstanceOf<NotSupportedException>());
    }

    [Test]
    public async Task SecondConcurrentTurnOnSameSessionIsRejected()
    {
        using var handler = new FakeClaudeHandler().EnqueueSse(new SseBuilder().Start().Text("...").Build(), hangAtEnd: true);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);
        var first = Turn();
        await using var enumerator = session.RunTurnAsync(first, CancellationToken.None).GetAsyncEnumerator();
        Assert.That(await enumerator.MoveNextAsync(), Is.True);

        Assert.That(async () => await RunAsync(session, Turn()), Throws.InstanceOf<InvalidOperationException>());
        await session.CancelTurnAsync(first.TurnId, CancellationToken.None);
    }

    [Test]
    public async Task HistoryKeepsCompletedTurnsAndDiscardsFailedOnes()
    {
        var ok = new SseBuilder().Start().Text("resposta 1").Stop("end_turn").Build();
        var ok2 = new SseBuilder().Start().Text("resposta 3").Stop("end_turn").Build();
        using var handler = new FakeClaudeHandler().EnqueueSse(ok).EnqueueError(HttpStatusCode.TooManyRequests, "rate_limit_error")
            .EnqueueSse(ok2);
        using var provider = ClaudeFixture.Provider(handler);
        await using var session = await SessionAsync(provider);

        await RunAsync(session, Turn("pergunta 1"));
        await RunAsync(session, Turn("pergunta 2"));
        await RunAsync(session, Turn("pergunta 3"));

        var messages = handler.Requests.Last().Json.GetProperty("messages");
        var texts = messages.EnumerateArray().Select(static m => m.GetProperty("content")[0].GetProperty("text").GetString()).ToArray();
        Assert.That(texts, Is.EqualTo(CommittedHistory));
    }

    [Test]
    public async Task DisposedSessionRejectsNewTurns()
    {
        using var handler = new FakeClaudeHandler();
        using var provider = ClaudeFixture.Provider(handler);
        var session = await SessionAsync(provider);
        await session.DisposeAsync();

        Assert.That(async () => await RunAsync(session, Turn()), Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(handler.Requests, Is.Empty);
    }

    [Test]
    public async Task RunTurnRacingDisposeNeverLeavesALiveTurnOnADisposedSession()
    {
        // The stream never ends by itself: only cancellation (by DisposeAsync) finishes the turn. A turn that becomes
        // active after DisposeAsync already looked for one would keep running on a disposed session.
        var sse = new SseBuilder().Start().Text("parcial").Build();
        for (var iteration = 0; iteration < 400; iteration++)
        {
            using var handler = new FakeClaudeHandler().EnqueueSse(sse, hangAtEnd: true);
            using var provider = ClaudeFixture.Provider(handler);
            var session = await SessionAsync(provider);
            using var start = new Barrier(2);
            var run = Task.Run(async () =>
            {
                start.SignalAndWait();
                try
                {
                    await RunAsync(session, Turn());
                }
                catch (ObjectDisposedException)
                {
                    // Rejected because the session was already disposed: the expected safe outcome.
                }
            });
            var dispose = Task.Run(async () =>
            {
                start.SignalAndWait();
                await session.DisposeAsync();
            });

            await dispose;
            var finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(5))) == run;
            Assert.That(finished, Is.True, $"Iteração {iteration}: turno continuou ativo após o descarte da sessão.");
            await run;
        }
    }
}
