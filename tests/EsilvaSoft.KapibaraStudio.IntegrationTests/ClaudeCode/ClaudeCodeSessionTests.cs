using System.Diagnostics;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode;
using EsilvaSoft.KapibaraStudio.SystemAdapters.ClaudeCode;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.ClaudeCode;

/// <summary>
/// Sessão/turno do modo Claude (assinatura) contra o CLI falso com fixtures derivadas dos transcripts do spike
/// P7-CL0-01 (Windows, Claude Code 2.1.268). Prova argv, stream, continuidade, cancelamento da árvore e limites; não
/// homologa conta, assinatura, modelo nem o binário real (GCL-8, homologação manual).
/// </summary>
[TestFixture]
[CancelAfter(120_000)]
public sealed class ClaudeCodeSessionTests
{
    private static readonly string[] TurnPrefix = ["-p", "--input-format", "stream-json"];
    private static readonly string[] AuthStatusPair = ["auth", "status"];
    private static readonly string[] TurnFailureFields =
        ["timestampUtc", "eventType", "errorCode", "stage", "resultReceived", "discardedLines", "exitCode"];

    private static async Task<ClaudeCodeAgentSession> SessionAsync(ClaudeCodeAgentProvider provider, string? model = null) =>
        (ClaudeCodeAgentSession)await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeCodeAgentProvider.Id, model), CancellationToken.None);

    private static string ValueAfter(string[] argv, string flag) => argv[Array.IndexOf(argv, flag) + 1];

    [Test]
    public async Task BasicTurnUsesTheExactArgvStreamsTextAndClosesStdinAfterTheResult()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        await using var session = await SessionAsync(fixture.Provider());

        var events = await ClaudeCodeFixture.RunAsync(session);

        var turn = fixture.TurnInvocations().Single();
        var stdin = fixture.Log().Where(static e => e.GetProperty("event").GetString() == "stdin").ToArray();
        using var message = JsonDocument.Parse(stdin[0].GetProperty("line").GetString()!);
        Assert.Multiple(() =>
        {
            Assert.That(events.Select(static e => e.Kind), Is.EqualTo(new[]
            {
                AgentEventKind.MessageStarted, AgentEventKind.MessageDelta, AgentEventKind.MessageCompleted,
            }));
            Assert.That(ClaudeCodeFixture.Text(events), Is.EqualTo("ok"));
            Assert.That(turn[..3], Is.EqualTo(TurnPrefix));
            Assert.That(ValueAfter(turn, "--output-format"), Is.EqualTo("stream-json"));
            Assert.That(ValueAfter(turn, "--tools"), Is.EqualTo("Read,Glob,Grep"), "Allowlist exata: nada de Bash/Edit/Write/WebFetch/Agent.");
            Assert.That(ValueAfter(turn, "--permission-mode"), Is.EqualTo("default"));
            Assert.That(ValueAfter(turn, "--setting-sources"), Is.EqualTo("user"));
            Assert.That(ValueAfter(turn, "--model"), Is.EqualTo("haiku"));
            Assert.That(ValueAfter(turn, "--max-turns"), Is.EqualTo("8"));
            Assert.That(turn, Does.Contain("--strict-mcp-config").And.Contain("--include-partial-messages").And.Contain("--verbose"));
            Assert.That(turn, Does.Not.Contain("--bare").And.Not.Contain("--resume").And.Not.Contain("--mcp-config")
                .And.Not.Contain("--permission-prompt-tool").And.Not.Contain("--dangerously-skip-permissions"));
            Assert.That(Guid.TryParseExact(ValueAfter(turn, "--session-id"), "D", out _), Is.True);
            Assert.That(ValueAfter(turn, "--settings"), Does.Contain("\"disableAllHooks\":true"));
            Assert.That(message.RootElement.GetProperty("type").GetString(), Is.EqualTo("user"));
            Assert.That(message.RootElement.GetProperty("message").GetProperty("content")[0].GetProperty("text").GetString(),
                Is.EqualTo("Responda apenas: ok"));
            Assert.That(fixture.Log().Any(static e => e.GetProperty("event").GetString() == "stdin-closed"), Is.True);
            Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
            Assert.That(session.LastTurn.TotalCostUsd, Is.GreaterThan(0));
            Assert.That(session.LastTurn.ObservedModel, Is.EqualTo("claude-haiku-4-5-20251001"));
        });
    }

    [Test]
    public async Task AuthStatusRunsBeforeTheTurnWithTheSameGlobalFlagsAndWorkingDirectory()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        await using var session = await SessionAsync(fixture.Provider());
        await ClaudeCodeFixture.RunAsync(session);

        var invocations = fixture.Invocations();
        var turnIndex = invocations.ToList().FindIndex(static argv => argv.Contains("-p"));
        var preflight = invocations[turnIndex - 1];
        var turn = invocations[turnIndex];
        Assert.Multiple(() =>
        {
            Assert.That(preflight[^2..], Is.EqualTo(AuthStatusPair), "auth status imediatamente antes do turno.");
            Assert.That(ValueAfter(preflight, "--settings"), Is.EqualTo(ValueAfter(turn, "--settings")));
            Assert.That(ValueAfter(preflight, "--setting-sources"), Is.EqualTo(ValueAfter(turn, "--setting-sources")));
            Assert.That(preflight, Does.Not.Contain("login").And.Not.Contain("--console").And.Not.Contain("--bare"));
        });
    }

    [Test]
    public async Task SecondTurnResumesTheSameCliSessionAndReassemblesFragmentedDeltas()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl", resumeFixture: "basic-turn2-fragmented.jsonl");
        await using var session = await SessionAsync(fixture.Provider());

        await ClaudeCodeFixture.RunAsync(session, "Memorize a palavra ABACAXI-7. Responda apenas: ok");
        var second = await ClaudeCodeFixture.RunAsync(session, "Qual palavra eu pedi para memorizar?");

        var turns = fixture.TurnInvocations();
        Assert.Multiple(() =>
        {
            Assert.That(ValueAfter(turns[1], "--resume"), Is.EqualTo(ValueAfter(turns[0], "--session-id")));
            Assert.That(turns[1], Does.Not.Contain("--session-id"));
            Assert.That(ClaudeCodeFixture.Text(second), Is.EqualTo("ABACAXI-7"), "Deltas AB/ACAXI-/7 escritos em pedaços de 7 bytes.");
            Assert.That(second.Count(static e => e.Kind == AgentEventKind.MessageStarted), Is.EqualTo(1));
            Assert.That(session.LastTurn!.Resumed, Is.True);
        });
    }

    [Test]
    public async Task MissingCliSessionOnResumeIsReportedAndTheNextTurnStartsAFreshSession()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        await using var session = await SessionAsync(fixture.Provider());
        await ClaudeCodeFixture.RunAsync(session);
        var firstId = session.CliSession.SessionId;

        fixture.ResumeMissing();
        var failed = await ClaudeCodeFixture.RunAsync(session);
        fixture.ResumeMissing(false);
        await ClaudeCodeFixture.RunAsync(session);

        var turns = fixture.TurnInvocations();
        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(failed), Is.EqualTo(ClaudeCodeErrorCodes.SessionNotFound));
            Assert.That(ValueAfter(turns[1], "--resume"), Is.EqualTo(firstId));
            Assert.That(turns[2], Does.Contain("--session-id"));
            Assert.That(ValueAfter(turns[2], "--session-id"), Is.Not.EqualTo(firstId), "Sem transportar contexto para outra sessão.");
        });
    }

    [TestCase("init-mismatch-tools.jsonl")]
    [TestCase("init-mismatch-apikey.jsonl")]
    [TestCase("init-mismatch-permission.jsonl")]
    [TestCase("init-old-version.jsonl")]
    public async Task DivergentInitAbortsTheTurnBeforeAnyTextIsShown(string fixtureName)
    {
        using var fixture = new ClaudeCodeFixture().Turn(fixtureName);
        await using var session = await SessionAsync(fixture.Provider());

        var events = await ClaudeCodeFixture.RunAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.EqualTo(ClaudeCodeErrorCodes.InitMismatch));
            Assert.That(events.Any(static e => e.Kind == AgentEventKind.MessageDelta), Is.False);
            Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Failed));
            Assert.That(session.CliSession.Established, Is.False);
        });
    }

#if DEBUG
    [Test]
    public async Task InitMismatchWritesBoundedDiagnosticWithoutPromptOrRawFrame()
    {
        using var fixture = new ClaudeCodeFixture().Turn("init-mismatch-tools.jsonl");
        var logDirectory = Path.Combine(fixture.Root, "logs");
        await using var session = await SessionAsync(fixture.Provider(fixture.Options(debugLogDirectory: logDirectory)));

        var events = await ClaudeCodeFixture.RunAsync(session, "PROMPT-CANARIO-PRIVATE-6391");
        var logPath = Path.Combine(logDirectory, "claude-code-debug.jsonl");
        var lines = File.ReadAllLines(logPath);
        using var init = JsonDocument.Parse(lines[0]);
        var diagnostic = init.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.EqualTo(ClaudeCodeErrorCodes.InitMismatch));
            Assert.That(diagnostic.GetProperty("eventType").GetString(), Is.EqualTo("ClaudeCodeInitMismatch"));
            Assert.That(diagnostic.GetProperty("reasons").EnumerateArray().Select(static value => value.GetString()),
                Does.Contain("Tools"));
            Assert.That(diagnostic.GetProperty("unexpectedToolCount").GetInt32(), Is.GreaterThan(0));
            Assert.That(lines.Any(static line => line.Contains("ClaudeCodeTurnFailure", StringComparison.Ordinal)), Is.True);
            Assert.That(string.Join('\n', lines), Does.Not.Contain("PROMPT-CANARIO-PRIVATE-6391")
                .And.Not.Contain(fixture.Root).And.Not.Contain("session_id").And.Not.Contain("orgId")
                .And.Not.Contain("<PIPE>"));
        });
    }

    [Test]
    public void DebugLogRedactsCredentialShapedToolNames()
    {
        using var fixture = new ClaudeCodeFixture();
        var logDirectory = Path.Combine(fixture.Root, "logs");
        ClaudeCodeDebugLog.InitMismatch(new LocalClaudeCodeSystem(), logDirectory, ["Tools"], [], ["sk-ant-CANARY-PRIVATE-6391"], 1,
            expectsMcp: false, observedMcpServerCount: 0, observedMcpStatus: null, permissionMode: "default",
            apiKeySource: "none", model: "haiku", version: "2.1.268");

        var text = File.ReadAllText(Path.Combine(logDirectory, "claude-code-debug.jsonl"));
        using var entry = JsonDocument.Parse(text);
        Assert.Multiple(() =>
        {
            Assert.That(entry.RootElement.GetProperty("unexpectedTools")[0].GetString(), Is.EqualTo("<redacted>"));
            Assert.That(text, Does.Not.Contain("CANARY-PRIVATE-6391"));
        });
    }

    [Test]
    public void TurnFailureLogsOnlyKnownCategoriesAndTypedResultMetadata()
    {
        using var fixture = new ClaudeCodeFixture();
        var logDirectory = Path.Combine(fixture.Root, "logs");
        ClaudeCodeDebugLog.TurnFailure(new LocalClaudeCodeSystem(), logDirectory, ClaudeCodeErrorCodes.ExecutionError, "Process", true, 0, 23);

        var text = File.ReadAllText(Path.Combine(logDirectory, "claude-code-debug.jsonl"));
        using var entry = JsonDocument.Parse(text);
        var diagnostic = entry.RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(diagnostic.GetProperty("errorCode").GetString(), Is.EqualTo(ClaudeCodeErrorCodes.ExecutionError));
            Assert.That(diagnostic.GetProperty("stage").GetString(), Is.EqualTo("Process"));
            Assert.That(diagnostic.GetProperty("resultReceived").GetBoolean(), Is.True);
            Assert.That(diagnostic.GetProperty("discardedLines").GetInt32(), Is.Zero);
            Assert.That(diagnostic.GetProperty("exitCode").GetInt32(), Is.EqualTo(23));
            Assert.That(diagnostic.EnumerateObject().Select(static property => property.Name),
                Is.EquivalentTo(TurnFailureFields));
        });
    }
#endif

    [Test]
    public async Task ToolOutsideTheAllowlistAbortsTheTurn()
    {
        using var fixture = new ClaudeCodeFixture().Turn("unexpected-tool.jsonl");
        await using var session = await SessionAsync(fixture.Provider());

        var events = await ClaudeCodeFixture.RunAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.EqualTo(ClaudeCodeErrorCodes.ToolOutsideAllowlist));
            Assert.That(events.Where(static e => e.ToolName is not null).Select(static e => e.ToolName), Has.None.EqualTo("Bash"));
        });
    }

    [Test]
    public async Task NativeReadToolsBecomeVisibleEventsWithoutArgumentsOrContent()
    {
        using var fixture = new ClaudeCodeFixture().Turn("read-tools.jsonl");
        await using var session = await SessionAsync(fixture.Provider());

        var events = await ClaudeCodeFixture.RunAsync(session);
        var tools = events.Where(static e => e.Kind is AgentEventKind.ToolStarted or AgentEventKind.ToolCompleted or AgentEventKind.ToolFailed).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(tools.Select(static e => (e.Kind, e.ToolName)), Is.EqualTo(new[]
            {
                (AgentEventKind.ToolStarted, "Read"), (AgentEventKind.ToolCompleted, "Read"),
                (AgentEventKind.ToolStarted, "Read"), (AgentEventKind.ToolFailed, "Read"),
                (AgentEventKind.ToolStarted, "Glob"), (AgentEventKind.ToolCompleted, "Glob"),
            }));
            Assert.That(tools.Where(static e => e.Kind == AgentEventKind.ToolFailed).Select(static e => e.Text),
                Is.All.EqualTo(ClaudeCodeErrorCodes.NativeToolFailed));
            Assert.That(events.All(static e => e.ArgumentsJson is null), Is.True, "Caminhos/argumentos nativos não viram eventos.");
            Assert.That(string.Concat(events.Select(static e => e.Text)), Does.Not.Contain("CANARIO-A1").And.Not.Contain("a.txt"));
            Assert.That(events.Any(static e => e.Kind == AgentEventKind.ToolRequested), Is.False, "Leitura nativa não passa pelo registry.");
            Assert.That(ClaudeCodeFixture.Text(events), Does.EndWith("FIM"));
            Assert.That(ClaudeCodeFixture.Error(events), Is.Null);
            Assert.That(session.LastTurn!.NativeToolCalls, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task PreventiveAuthBlockNeverStartsATurnProcessOrWritesThePrompt()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        await using var session = await SessionAsync(fixture.Provider());
        // A chave aparece depois da criação da sessão: o bloqueio acontece no próximo turno, antes do stdin.
        fixture.AuthStatus("""{"loggedIn":true,"authMethod":"claude.ai","apiProvider":"firstParty","apiKeySource":"ANTHROPIC_API_KEY","subscriptionType":null}""");

        var events = await ClaudeCodeFixture.RunAsync(session, "prompt-que-nao-pode-sair");

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.EqualTo(ClaudeCodeErrorCodes.NonSubscriptionAuthentication));
            Assert.That(fixture.TurnInvocations(), Is.Empty);
            Assert.That(fixture.Log().Any(static e => e.GetProperty("event").GetString() == "stdin"), Is.False);
        });
    }

    [Test]
    public async Task EnvironmentVariableThatChangesBillingBlocksTheTurnWithoutStartingTheCli()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        var blocked = false;
        var provider = fixture.Provider(fixture.Options(environment: name => Volatile.Read(ref blocked) && name == "ANTHROPIC_API_KEY"));
        await using var session = await SessionAsync(provider);
        var before = fixture.Invocations().Count;
        Volatile.Write(ref blocked, true);

        var events = await ClaudeCodeFixture.RunAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.EqualTo(ClaudeCodeErrorCodes.BlockedEnvironment));
            Assert.That(fixture.Invocations(), Has.Count.EqualTo(before), "Nenhum processo depois do bloqueio por nome de variável.");
        });
    }

    [Test]
    public async Task CrashMidStreamClosesTheOpenMessageAndReportsOnlyASafeCode()
    {
        using var fixture = new ClaudeCodeFixture().Turn("crash-mid-stream.jsonl");
        await using var session = await SessionAsync(fixture.Provider());

        var events = await ClaudeCodeFixture.RunAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.EqualTo(ClaudeCodeErrorCodes.ProcessFailed));
            Assert.That(events.Count(static e => e.Kind == AgentEventKind.MessageStarted),
                Is.EqualTo(events.Count(static e => e.Kind == AgentEventKind.MessageCompleted)));
            Assert.That(string.Concat(events.Select(static e => e.Text)), Does.Not.Contain(ClaudeCodeFixture.StderrCanary));
            Assert.That(session.LastTurn!.ToString(), Does.Not.Contain(ClaudeCodeFixture.StderrCanary));
        });
    }

    [Test]
    public async Task InvalidAndGiantLinesAreDiscardedWithoutBufferingThemAndTheTurnStillCompletes()
    {
        using var fixture = new ClaudeCodeFixture().Turn("noise-then-result.jsonl");
        var options = fixture.Options();
        var provider = fixture.Provider(new ClaudeCodeAgentProviderOptions
        {
            ExecutablePath = options.ExecutablePath, DedicatedWorkingDirectory = options.DedicatedWorkingDirectory,
            AppDataDirectory = options.AppDataDirectory, DatabasePath = options.DatabasePath, ProbeTimeout = options.ProbeTimeout,
            IsEnvironmentVariableSet = options.IsEnvironmentVariableSet, MaxLineBytes = 1024 * 1024, DefaultModel = "haiku",
        });
        await using var session = await SessionAsync(provider);

        var events = await ClaudeCodeFixture.RunAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.Null);
            Assert.That(ClaudeCodeFixture.Text(events), Is.EqualTo("ok"));
            Assert.That(session.LastTurn!.DiscardedLines, Is.EqualTo(2), "Uma linha não JSON e uma de 5 MB acima do limite de 1 MB.");
        });
    }

    [Test]
    public async Task FloodOfInvalidLinesAbortsAndKillsTheProcess()
    {
        using var fixture = new ClaudeCodeFixture().Turn("garbage-flood.jsonl");
        await using var session = await SessionAsync(fixture.Provider());

        var events = await ClaudeCodeFixture.RunAsync(session);

        Assert.That(ClaudeCodeFixture.Error(events), Is.EqualTo(ClaudeCodeErrorCodes.ProtocolViolation));
    }

    [TestCase("result-max-turns.jsonl", ClaudeCodeErrorCodes.MaxTurnsReached)]
    [TestCase("result-rate-limited.jsonl", ClaudeCodeErrorCodes.RateLimited)]
    [TestCase("result-auth-failed.jsonl", ClaudeCodeErrorCodes.AuthenticationFailed)]
    public async Task ErrorResultsMapToTypedCodesWithoutRetryOrFallback(string fixtureName, string code)
    {
        using var fixture = new ClaudeCodeFixture().Turn(fixtureName);
        await using var session = await SessionAsync(fixture.Provider());

        var events = await ClaudeCodeFixture.RunAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.EqualTo(code));
            Assert.That(fixture.TurnInvocations(), Has.Count.EqualTo(1), "Nenhuma repetição automática.");
        });
    }

    [Test]
    public async Task ExpiredSubscriptionAfterPreflightIsClassifiedThroughOfficialAuthStatus()
    {
        using var fixture = new ClaudeCodeFixture().Turn("result-auth-expired-no-status.jsonl")
            .AuthStatusAfterTurn("""{"loggedIn":false}""");
        await using var session = await SessionAsync(fixture.Provider());

        var events = await ClaudeCodeFixture.RunAsync(session);

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.EqualTo(ClaudeCodeErrorCodes.AuthenticationFailed));
            Assert.That(fixture.TurnInvocations(), Has.Count.EqualTo(1), "Não há retry ou troca para API.");
            Assert.That(fixture.Invocations().Count(args => args.Length >= 2 &&
                args[^2] == AuthStatusPair[0] && args[^1] == AuthStatusPair[1]),
                Is.GreaterThanOrEqualTo(3), "Criação da sessão, pré-envio e rechecagem após o erro.");
        });
    }

    [Test]
    public async Task CancellingKillsTheWholeProcessTreeAndLeavesTheTurnOutcomeUnknownButResumable()
    {
        using var fixture = new ClaudeCodeFixture().Turn("cancel-with-child.jsonl", resumeFixture: "basic-turn2-resume.jsonl");
        await using var session = await SessionAsync(fixture.Provider());
        var request = ClaudeCodeFixture.Request("Rode algo longo");
        var events = new List<AgentProviderEvent>();

        var run = Task.Run(async () =>
        {
            await foreach (var item in session.RunTurnAsync(request, CancellationToken.None))
            {
                events.Add(item);
            }
        });
        var childPid = await WaitForChildAsync(fixture);
        var parentPid = TurnPid(fixture);
        await session.CancelTurnAsync(request.TurnId, CancellationToken.None);
        await run.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Multiple(() =>
        {
            Assert.That(WaitUntilGone(childPid), Is.True, "O filho de ferramenta precisa morrer com a árvore (Job Object/grupo).");
            Assert.That(WaitUntilGone(parentPid), Is.True);
            Assert.That(events.Any(static e => e.Kind == AgentEventKind.AgentError), Is.False, "Cancelamento não é erro do provider.");
            Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown));
            Assert.That(session.CliSession.Established, Is.True, "O init já tinha sido validado: o próximo turno retoma.");
        });

        var next = await ClaudeCodeFixture.RunAsync(session);
        Assert.Multiple(() =>
        {
            Assert.That(fixture.TurnInvocations()[^1], Does.Contain("--resume"));
            Assert.That(ClaudeCodeFixture.Text(next), Is.EqualTo("ABACAXI-7"));
        });
    }

    [Test]
    public async Task CancellingBeforeThePromptIsWrittenIsACleanCancellationWithoutATurnProcess()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        await using var session = await SessionAsync(fixture.Provider());
        fixture.AuthDelay(10_000);
        using var cancel = new CancellationTokenSource();

        var run = ClaudeCodeFixture.RunAsync(session, cancellationToken: cancel.Token);
        await WaitUntilAsync(() => fixture.Invocations().Count(static argv => argv.Contains("status")) == 2);
        await cancel.CancelAsync();
        var events = await run.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Multiple(() =>
        {
            Assert.That(events, Is.Empty);
            Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Cancelled), "Nada saiu: cancelamento limpo, não OutcomeUnknown.");
            Assert.That(fixture.TurnInvocations(), Is.Empty);
        });
    }

    [Test]
    public async Task KillingOnlyTheCliProcessLeavesAnOrphanThatTheTreeCheckDetects()
    {
        // Controle negativo do teste acima: prova que a verificação de filho sobrevivente de fato detecta um órfão.
        using var fixture = new ClaudeCodeFixture().Turn("cancel-with-child.jsonl");
        var info = new ProcessStartInfo(ClaudeCodeFixture.FakeExecutable)
        {
            WorkingDirectory = fixture.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-p", "--session-id", Guid.NewGuid().ToString("D"), "--tools", "Read,Glob,Grep" })
        {
            info.ArgumentList.Add(argument);
        }

        using var parent = Process.Start(info)!;
        // O filho herda o stdout do pai: a leitura só termina depois que o filho também morre.
        _ = parent.StandardOutput.ReadToEndAsync();
        await parent.StandardInput.WriteLineAsync("{\"type\":\"user\"}");
        await parent.StandardInput.FlushAsync();
        var childPid = await WaitForChildAsync(fixture);
        try
        {
            parent.Kill(entireProcessTree: false);
            await parent.WaitForExitAsync();
            Assert.That(WaitUntilGone(childPid, TimeSpan.FromSeconds(2)), Is.False, "Sem Job/grupo o filho sobrevive (spike: PING órfão).");
        }
        finally
        {
            try
            {
                Process.GetProcessById(childPid).Kill();
            }
            catch (ArgumentException)
            {
            }
        }
    }

    [Test]
    public async Task TurnDeadlineKillsTheProcessAndReportsTimeout()
    {
        using var fixture = new ClaudeCodeFixture().Turn("hang-after-init.jsonl");
        await using var session = await SessionAsync(fixture.Provider(fixture.Options(turnDuration: TimeSpan.FromSeconds(3))));

        var events = await ClaudeCodeFixture.RunAsync(session).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.EqualTo(ClaudeCodeErrorCodes.TurnTimeout));
            Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.TimedOut));
            Assert.That(WaitUntilGone(TurnPid(fixture)), Is.True);
        });
    }

    [Test]
    public async Task CancellingSessionADoesNotAffectSessionB()
    {
        using var hanging = new ClaudeCodeFixture().Turn("cancel-with-child.jsonl");
        using var normal = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        await using var sessionA = await SessionAsync(hanging.Provider());
        await using var sessionB = await SessionAsync(normal.Provider());
        using var cancelA = new CancellationTokenSource();

        var runA = ClaudeCodeFixture.RunAsync(sessionA, cancellationToken: cancelA.Token);
        await WaitForChildAsync(hanging);
        var runB = ClaudeCodeFixture.RunAsync(sessionB);
        await cancelA.CancelAsync();
        await runA.WaitAsync(TimeSpan.FromSeconds(20));
        var eventsB = await runB.WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Multiple(() =>
        {
            Assert.That(sessionA.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.OutcomeUnknown));
            Assert.That(ClaudeCodeFixture.Text(eventsB), Is.EqualTo("ok"));
            Assert.That(sessionB.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        });
    }

    [Test]
    public async Task EmptyOrOversizedMessagesFailWithoutStartingAProcess()
    {
        using var fixture = new ClaudeCodeFixture().Turn("basic-turn1.jsonl");
        await using var session = await SessionAsync(fixture.Provider());
        var before = fixture.Invocations().Count;

        var empty = await ClaudeCodeFixture.RunAsync(session, "   ");
        var huge = await ClaudeCodeFixture.RunAsync(session, new string('a', 100_001));

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(empty), Is.EqualTo(ClaudeCodeErrorCodes.EmptyMessage));
            Assert.That(ClaudeCodeFixture.Error(huge), Is.EqualTo(ClaudeCodeErrorCodes.InputTooLarge));
            Assert.That(fixture.Invocations(), Has.Count.EqualTo(before));
        });
    }

    [Test]
    public async Task SecondConcurrentTurnOnTheSameSessionIsRejected()
    {
        using var fixture = new ClaudeCodeFixture().Turn("hang-after-init.jsonl");
        await using var session = await SessionAsync(fixture.Provider());
        using var cancel = new CancellationTokenSource();
        var first = ClaudeCodeFixture.RunAsync(session, cancellationToken: cancel.Token);
        await WaitUntilAsync(() => fixture.TurnInvocations().Count == 1);

        Assert.ThrowsAsync<InvalidOperationException>(() => ClaudeCodeFixture.RunAsync(session));
        await cancel.CancelAsync();
        await first.WaitAsync(TimeSpan.FromSeconds(20));
    }

    [Test]
    public async Task AccountDataAndStderrNeverReachEventsSummariesOrExceptions()
    {
        using var fixture = new ClaudeCodeFixture().Turn("crash-mid-stream.jsonl");
        var provider = fixture.Provider();
        await using var session = await SessionAsync(provider);
        var events = await ClaudeCodeFixture.RunAsync(session);
        var status = await provider.GetAuthenticationStatusAsync();
        var providerStatus = await provider.GetStatusAsync(CancellationToken.None);

        fixture.AuthStatus("""{"loggedIn":false,"email":"canario-conta@example.invalid"}""", exitCode: 1);
        var exception = Assert.ThrowsAsync<ClaudeCodeUnavailableException>(() => SessionAsync(provider));

        var surfaces = string.Join("|", events.Select(static e => e.Text + e.ToolName + e.ArgumentsJson)) + status + providerStatus +
            session.LastTurn + exception!.Message;
        Assert.Multiple(() =>
        {
            foreach (var canary in new[] { ClaudeCodeFixture.EmailCanary, ClaudeCodeFixture.OrgCanary, ClaudeCodeFixture.StderrCanary, "Org Canario", ".claude" })
            {
                Assert.That(surfaces, Does.Not.Contain(canary));
            }

            Assert.That(exception.Reason, Is.EqualTo(ClaudeCodeUnavailableReason.NotLoggedIn));
        });
    }

    private static int TurnPid(ClaudeCodeFixture fixture) =>
        fixture.Log().Where(static e => e.GetProperty("event").GetString() == "start" &&
                e.GetProperty("argv").EnumerateArray().Any(static a => a.GetString() == "-p"))
            .Select(static e => e.GetProperty("pid").GetInt32()).Last();

    private static async Task<int> WaitForChildAsync(ClaudeCodeFixture fixture)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (fixture.Log().FirstOrDefault(static e => e.GetProperty("event").GetString() == "child") is { ValueKind: JsonValueKind.Object } child)
            {
                return child.GetProperty("pid").GetInt32();
            }

            await Task.Delay(50);
        }

        Assert.Fail("O CLI falso não criou o processo filho.");
        return 0;
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

    private static bool WaitUntilGone(int pid, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var process = Process.GetProcessById(pid);
                if (process.HasExited)
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                return true;
            }
            catch (InvalidOperationException)
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return false;
    }
}
