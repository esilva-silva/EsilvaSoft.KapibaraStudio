using EsilvaSoft.KapibaraStudio.SystemAdapters.Copilot;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Copilot;
using GitHub.Copilot;
using GitHub.Copilot.Rpc;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Tests.Copilot;

#pragma warning disable GHCP001 // Homologação manual do SDK experimental pinado, em sessão isolada.
[TestFixture]
[Category("Integration")]
internal sealed class CopilotOfficialRuntimeManualTests
{
    [Test, Explicit("Verificação manual Windows sem inferência: compara a autenticação oficial entre modos do SDK.")]
    public async Task WindowsSessionRuntimeReportsOfficialAuthenticationAcrossModes()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True, "A homologação P7-COP está limitada ao Windows.");

        await using var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions());
        await accountClient.StartAsync(CancellationToken.None);
        var accountAuth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
        var emptyModeOptions = CopilotRuntimeSettings.AccountClientOptions();
        emptyModeOptions.Mode = CopilotClientMode.Empty;
        await using var emptyModeClient = new CopilotClient(emptyModeOptions);
        await emptyModeClient.StartAsync(CancellationToken.None);
        var emptyModeAuth = await emptyModeClient.GetAuthStatusAsync(CancellationToken.None);

        var installedCliPath = LocalCopilotAccountCommands.FindCliExecutable(Environment.GetEnvironmentVariable("PATH"));
        Assert.That(installedCliPath, Is.Not.Null, "A CLI oficial precisa estar instalada para comparar runtimes.");
        var installedCliOptions = CopilotRuntimeSettings.AccountClientOptions();
        installedCliOptions.Connection = RuntimeConnection.ForStdio(path: installedCliPath);
        await using var installedCliClient = new CopilotClient(installedCliOptions);
        await installedCliClient.StartAsync(CancellationToken.None);
        var installedCliAuth = await installedCliClient.GetAuthStatusAsync(CancellationToken.None);

        await using var store = new CopilotVolatileSessionFsStore();
        var sessionFs = CopilotVolatileSessionFsStore.CreateConfiguration(Environment.CurrentDirectory);
        await using var sessionClient = new CopilotClient(
            CopilotRuntimeSettings.SessionClientOptions(Environment.CurrentDirectory, sessionFs));
        await sessionClient.StartAsync(CancellationToken.None);
        var sessionAuth = await sessionClient.GetAuthStatusAsync(CancellationToken.None);

        TestContext.Progress.WriteLine($"Autenticação reportada pelo SDK: runtime empacotado CopilotCli={accountAuth.IsAuthenticated}; Empty={emptyModeAuth.IsAuthenticated}; runtime da CLI instalada={installedCliAuth.IsAuthenticated}; cliente de sessão={sessionAuth.IsAuthenticated}.");
        Assert.That(sessionAuth.IsAuthenticated, Is.EqualTo(accountAuth.IsAuthenticated),
            "A sessão precisa enxergar o mesmo estado de autenticação da consulta oficial da conta.");
        if (accountAuth.IsAuthenticated)
        {
            Assert.That(string.Equals(accountAuth.AuthType, "user", StringComparison.Ordinal) &&
                        string.Equals(sessionAuth.AuthType, "user", StringComparison.Ordinal), Is.True,
                "Os dois clientes de produção precisam confirmar a assinatura oficial.");
        }
    }

    [Test, Explicit("Verificação manual Windows sem inferência: compara autenticação e modelos elegíveis da conta e da sessão.")]
    public async Task WindowsOfficialAccountAndSessionExposeSameEligibleModels()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True, "A homologação P7-COP está limitada ao Windows.");

        await using var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions());
        await accountClient.StartAsync(CancellationToken.None);
        var accountAuth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
        Assert.That(accountAuth.IsAuthenticated && string.Equals(accountAuth.AuthType, "user", StringComparison.Ordinal),
            Is.True, "A CLI oficial precisa confirmar uma conta Copilot de usuário antes de consultar os modelos.");
        var accountModels = (await accountClient.ListModelsAsync(CancellationToken.None))
            .Select(static model => model.Id)
            .Where(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
            .Order(StringComparer.Ordinal).ToArray();

        await using var sessionClient = new CopilotClient(CopilotRuntimeSettings.SessionClientOptions());
        await sessionClient.StartAsync(CancellationToken.None);
        var sessionAuth = await sessionClient.GetAuthStatusAsync(CancellationToken.None);
        Assert.That(sessionAuth.IsAuthenticated && string.Equals(sessionAuth.AuthType, "user", StringComparison.Ordinal),
            Is.True, "O runtime de sessão precisa usar a mesma conta Copilot oficial.");
        var sessionModels = (await sessionClient.ListModelsAsync(CancellationToken.None))
            .Select(static model => model.Id)
            .Where(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
            .Order(StringComparer.Ordinal).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(accountModels, Is.Not.Empty, "A conta precisa ter ao menos um modelo elegível.");
            Assert.That(sessionModels, Is.EqualTo(accountModels),
                "O cliente de sessão deve expor os mesmos modelos elegíveis que a consulta da conta.");
        });
        TestContext.Progress.WriteLine($"Autenticação de usuário confirmada nos dois clientes; modelos elegíveis em ambos: {accountModels.Length}; sem inferência.");
    }

    [Test, Explicit("Verificação manual Windows sem inferência: cria e apaga uma sessão oficial com allowlist vazia.")]
    public async Task WindowsOfficialRuntimeCreatesAndDeletesEmptySessionWithoutPrompt()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True, "A homologação P7-COP está limitada ao Windows.");

        await using var store = new CopilotVolatileSessionFsStore();
        var sessionFs = CopilotVolatileSessionFsStore.CreateConfiguration(Environment.CurrentDirectory);
        await using var client = new CopilotClient(
            CopilotRuntimeSettings.SessionClientOptions(Environment.CurrentDirectory, sessionFs));
        await client.StartAsync(CancellationToken.None);

        var sessionId = Guid.NewGuid().ToString("D");
        store.CreateProvider(sessionId);
        var createConfig = new SessionConfig { SessionId = sessionId, Tools = [], AvailableTools = new ToolSet() };
        CopilotRuntimeSettings.ApplyProductSessionDefaults(createConfig);
        store.ConfigureSession(createConfig);

        var sessionCreated = false;
        try
        {
            await using (var created = await client.CreateSessionAsync(createConfig, CancellationToken.None))
            {
                sessionCreated = true;
                Assert.That(created.SessionId, Is.EqualTo(sessionId));
            }

            TestContext.Progress.WriteLine("Runtime oficial aceitou create com allowlist vazia; nenhum prompt foi enviado.");
        }
        finally
        {
            if (sessionCreated && await client.GetSessionMetadataAsync(sessionId, CancellationToken.None) is not null)
                await client.DeleteSessionAsync(sessionId, CancellationToken.None);
            await store.DeleteSessionAsync(sessionId, CancellationToken.None);
        }
    }

    [Test, Explicit("Verificação manual Windows sem inferência: aplica a lista vazia de agentes internos antes de permitir qualquer prompt.")]
    public async Task WindowsOfficialRuntimeCanDisableAllBuiltInAgentsBeforeAnyPrompt()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True, "A homologação P7-COP está limitada ao Windows.");

        await using var store = new CopilotVolatileSessionFsStore();
        var sessionFs = CopilotVolatileSessionFsStore.CreateConfiguration(Environment.CurrentDirectory);
        await using var client = new CopilotClient(
            CopilotRuntimeSettings.SessionClientOptions(Environment.CurrentDirectory, sessionFs));
        await client.StartAsync(CancellationToken.None);

        var sessionId = Guid.NewGuid().ToString("D");
        store.CreateProvider(sessionId);
        var config = new SessionConfig { SessionId = sessionId, Tools = [], AvailableTools = new ToolSet() };
        CopilotRuntimeSettings.ApplyProductSessionDefaults(config);
        store.ConfigureSession(config);

        var created = false;
        try
        {
            await using (var session = await client.CreateSessionAsync(config, CancellationToken.None))
            {
                created = true;
                var restriction = await session.Rpc.Options.UpdateAsync(
                    includedBuiltinAgents: [], cancellationToken: CancellationToken.None);
                Assert.That(restriction.Success, Is.True,
                    "The official runtime must acknowledge an empty built-in agent allowlist.");

                var agents = await session.Rpc.Agent.ListAsync(
                    new SessionAgentListRequest { IncludeBuiltInAgents = true }, CancellationToken.None);
                Assert.That(agents.Agents, Is.Empty,
                    "The runtime must expose no built-in or custom agents after the acknowledged empty allowlist.");
            }

            TestContext.Progress.WriteLine("Runtime oficial confirmou allowlist vazia e listagem sem agentes; nenhum prompt foi enviado.");
        }
        finally
        {
            try
            {
                if (created && await client.GetSessionMetadataAsync(sessionId, CancellationToken.None) is not null)
                    await client.DeleteSessionAsync(sessionId, CancellationToken.None);
            }
            finally
            {
                await store.DeleteSessionAsync(sessionId, CancellationToken.None);
            }
        }
    }

    [Test, Explicit("Aceite manual Windows com conta Copilot: cria uma sessão volátil, envia um prompt sintético sem tools e apaga a sessão ao final.")]
    public async Task WindowsOfficialRuntimeCreatesStreamsAndDeletesVolatileSession()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True, "A homologação P7-COP está limitada ao Windows.");

        string modelId;
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(CancellationToken.None);
            var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "The official CLI must report a signed-in Copilot user account.");

            var models = (await accountClient.ListModelsAsync(CancellationToken.None))
                .Select(static model => model.Id)
                .Where(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                .ToArray();
            Assert.That(models, Is.Not.Empty, "The account must have at least one eligible model.");
            modelId = models[0];
        }

        await using var store = new CopilotVolatileSessionFsStore();
        var sessionFs = CopilotVolatileSessionFsStore.CreateConfiguration(Environment.CurrentDirectory);
        await using var client = new CopilotClient(
            CopilotRuntimeSettings.SessionClientOptions(Environment.CurrentDirectory, sessionFs));
        await client.StartAsync(CancellationToken.None);

        var sessionId = Guid.NewGuid().ToString("D");
        store.CreateProvider(sessionId);
        var config = new SessionConfig
        {
            SessionId = sessionId,
            Model = modelId,
            Tools = [],
            AvailableTools = new ToolSet(),
            Streaming = true,
        };
        CopilotRuntimeSettings.ApplyProductSessionDefaults(config);
        store.ConfigureSession(config);

        CopilotSession? session = null;
        var sessionCreated = false;
        var sawDelta = 0;
        var terminalEvent = "pending";
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            session = await client.CreateSessionAsync(config, CancellationToken.None);
            sessionCreated = true;
            Assert.That(session.SessionId, Is.EqualTo(sessionId), "SessionFs must bind to the reserved session ID.");

            using var subscription = session.On<SessionEvent>(evt =>
            {
                switch (evt)
                {
                    case AssistantMessageDeltaEvent delta when !string.IsNullOrWhiteSpace(delta.Data?.DeltaContent):
                        Interlocked.Exchange(ref sawDelta, 1);
                        break;
                    case SessionErrorEvent error:
                        terminalEvent = $"session-error:{SafeDiagnosticToken(error.Data?.ErrorType)}:{SafeDiagnosticToken(error.Data?.ErrorCode)}:{error.Data?.StatusCode?.ToString(CultureInfo.InvariantCulture) ?? "none"}:{ClassifyError(error.Data?.Message)}";
                        completion.TrySetResult(false);
                        break;
                    case ExternalToolRequestedEvent:
                        terminalEvent = "unexpected-tool-request";
                        completion.TrySetResult(false);
                        break;
                    case SessionIdleEvent idle when !string.Equals(
                        idle.Data?.Mode.ToString(), "autopilot", StringComparison.OrdinalIgnoreCase):
                        terminalEvent = "idle";
                        completion.TrySetResult(Volatile.Read(ref sawDelta) != 0);
                        break;
                }
            });

            await session.SendAsync(new MessageOptions { Prompt = "Responda apenas com a palavra OK." }, CancellationToken.None);
            var streamedResponse = await completion.Task.WaitAsync(TimeSpan.FromSeconds(120));
            TestContext.Progress.WriteLine($"Tipo de evento terminal: {terminalEvent}; houve delta: {Volatile.Read(ref sawDelta) != 0}.");
            Assert.That(streamedResponse, Is.True,
                "The official session must stream a response and reach idle without requesting a tool.");
            TestContext.Progress.WriteLine("Conta Copilot autenticada; sessão volátil criada; houve delta de resposta e idle; texto e modelo não foram registrados.");
        }
        finally
        {
            try
            {
                if (session is not null)
                    await session.DisposeAsync();

                // SessionFs-only sessions have no native session file when EnableSessionStore=false.
                if (sessionCreated && await client.GetSessionMetadataAsync(sessionId, CancellationToken.None) is not null)
                    await client.DeleteSessionAsync(sessionId, CancellationToken.None);
            }
            finally
            {
                await store.DeleteSessionAsync(sessionId, CancellationToken.None);
            }
        }
    }

    [Test, Explicit("Aceite manual Windows com conta Copilot: valida persistência, retomada e erase SessionFs após resposta sintética sem tools.")]
    public async Task WindowsOfficialRuntimePersistsResumesAndErasesSessionFsConversationWithoutTools()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True, "A homologação P7-COP está limitada ao Windows.");

        string modelId;
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(CancellationToken.None);
            var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "The official CLI must report a signed-in Copilot user account.");
            modelId = (await accountClient.ListModelsAsync(CancellationToken.None))
                .Select(static model => model.Id)
                .FirstOrDefault(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                ?? throw new AssertionException("No eligible Copilot model was returned.");
        }

        var sessionRoot = Path.Combine(Path.GetTempPath(), $"KapibaraStudioCopilotPersistentManual-{Guid.NewGuid():N}");
        var sessionId = Guid.NewGuid().ToString("D");
        var sessionDirectory = Path.Combine(sessionRoot,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId))));
        var store = new CopilotPersistentSessionFsStore(sessionRoot);
        var sessionCreated = false;
        try
        {
            await using (var client = new CopilotClient(CopilotRuntimeSettings.SessionClientOptions(
                             Environment.CurrentDirectory,
                             CopilotPersistentSessionFsStore.CreateConfiguration(Environment.CurrentDirectory))))
            {
                await client.StartAsync(CancellationToken.None);
                var config = new SessionConfig
                {
                    SessionId = sessionId,
                    Model = modelId,
                    Tools = [],
                    AvailableTools = new ToolSet(),
                    Streaming = true,
                };
                CopilotRuntimeSettings.ApplyProductSessionDefaults(config);
                store.ConfigureSession(config);

                CopilotSession? session = null;
                var sawDelta = 0;
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                try
                {
                    session = await client.CreateSessionAsync(config, CancellationToken.None);
                    sessionCreated = true;
                    Assert.That(session.SessionId, Is.EqualTo(sessionId));

                    using var subscription = session.On<SessionEvent>(evt =>
                    {
                        switch (evt)
                        {
                            case AssistantMessageDeltaEvent delta when !string.IsNullOrWhiteSpace(delta.Data?.DeltaContent):
                                Interlocked.Exchange(ref sawDelta, 1);
                                break;
                            case SessionErrorEvent:
                                completion.TrySetResult(false);
                                break;
                            case ExternalToolRequestedEvent:
                                completion.TrySetResult(false);
                                break;
                            case SessionIdleEvent idle when !string.Equals(
                                idle.Data?.Mode.ToString(), "autopilot", StringComparison.OrdinalIgnoreCase):
                                completion.TrySetResult(Volatile.Read(ref sawDelta) != 0);
                                break;
                        }
                    });

                    await session.SendAsync(new MessageOptions { Prompt = "Responda apenas com a palavra OK." }, CancellationToken.None);
                    Assert.That(await completion.Task.WaitAsync(TimeSpan.FromSeconds(120)), Is.True,
                        "A sessão persistente precisa transmitir resposta e alcançar idle sem tools.");

                    var persistedFileCount = Directory.Exists(sessionDirectory)
                        ? Directory.EnumerateFiles(sessionDirectory, "*", SearchOption.AllDirectories).Count()
                        : 0;
                    Assert.That(persistedFileCount, Is.GreaterThan(0),
                        "O runtime precisa gravar estado real de sessão no SessionFs persistente.");
                    TestContext.Progress.WriteLine($"Resposta transmitida; SessionFs persistente recebeu {persistedFileCount} arquivo(s); conteúdo não foi lido.");
                }
                finally
                {
                    if (session is not null) await session.DisposeAsync();
                }
            }

            await using (var resumedClient = new CopilotClient(CopilotRuntimeSettings.SessionClientOptions(
                             Environment.CurrentDirectory,
                             CopilotPersistentSessionFsStore.CreateConfiguration(Environment.CurrentDirectory))))
            {
                await resumedClient.StartAsync(CancellationToken.None);
                var resumeConfig = new ResumeSessionConfig
                {
                    Model = modelId,
                    Tools = [],
                    AvailableTools = new ToolSet(),
                    Streaming = true,
                };
                CopilotRuntimeSettings.ApplyProductSessionDefaults(resumeConfig);
                store.ConfigureSession(resumeConfig);
                await using (var resumed = await resumedClient.ResumeSessionAsync(sessionId, resumeConfig, CancellationToken.None))
                {
                    Assert.That(resumed.SessionId, Is.EqualTo(sessionId));
                }

                await store.DeleteSessionAsync(sessionId, async token =>
                {
                    if (await resumedClient.GetSessionMetadataAsync(sessionId, token) is not null)
                        await resumedClient.DeleteSessionAsync(sessionId, token);
                }, CancellationToken.None);
            }

            Assert.That(Directory.Exists(sessionDirectory), Is.False,
                "O erase coordenado precisa remover a árvore SessionFs após a retomada e o delete nativo.");
        }
        finally
        {
            await store.DisposeAsync();
            if (Directory.Exists(sessionRoot)) Directory.Delete(sessionRoot, recursive: true);
        }

        Assert.That(sessionCreated, Is.True);
    }

    [Test, Explicit("Aceite manual Windows com conta Copilot: cancela após o primeiro delta e valida estado indeterminado sem alegar rollback.")]
    public async Task WindowsOfficialAdapterCancelsLiveTurnAndReportsPossibleEffect()
    {
        Assert.That(OperatingSystem.IsWindows(), Is.True, "A homologação P7-COP está limitada ao Windows.");

        string modelId;
        await using (var accountClient = new CopilotClient(CopilotRuntimeSettings.AccountClientOptions()))
        {
            await accountClient.StartAsync(CancellationToken.None);
            var auth = await accountClient.GetAuthStatusAsync(CancellationToken.None);
            Assert.That(auth.IsAuthenticated && string.Equals(auth.AuthType, "user", StringComparison.Ordinal),
                Is.True, "The official CLI must report a signed-in Copilot user account.");
            modelId = (await accountClient.ListModelsAsync(CancellationToken.None))
                .Select(static model => model.Id)
                .FirstOrDefault(static id => id is { Length: > 0 and <= 128 } && !id.Any(char.IsControl))
                ?? throw new AssertionException("No eligible Copilot model was returned.");
        }

        var store = new CopilotVolatileSessionFsStore();
        string? providerSessionId = null;
        try
        {
            var client = new CopilotClient(CopilotRuntimeSettings.SessionClientOptions(
                Environment.CurrentDirectory, CopilotVolatileSessionFsStore.CreateConfiguration(Environment.CurrentDirectory)));
            var options = new AgentSessionOptions(CopilotSubscriptionAgentProvider.Id, modelId, Environment.CurrentDirectory)
            {
                PersistProviderSession = false,
                ProviderSessionObserver = update =>
                {
                    if (update.Change == AgentProviderSessionChange.Established)
                        providerSessionId = update.ProviderSessionId;
                },
            };
            var turnId = AgentTurnId.New();
            await using (var session = new CopilotSubscriptionAgentSession(new EmptyManualToolRegistry(), options, client, store))
            {
                var request = new AgentTurnRequest(turnId,
                    "Escreva uma resposta detalhada com pelo menos 1200 palavras sobre astronomia. Não use ferramentas.",
                    "manual-cancel-test", 1)
                {
                    Plan = new AgentTurnPlan(AgentOperationMode.Agent, [], [], [], [],
                        AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None),
                };

                var sawDelta = false;
                await foreach (var item in session.RunTurnAsync(request, CancellationToken.None))
                {
                    if (!sawDelta && item.Kind == AgentEventKind.MessageDelta)
                    {
                        sawDelta = true;
                        await session.CancelTurnAsync(turnId, CancellationToken.None);
                    }
                }

                Assert.Multiple(() =>
                {
                    Assert.That(sawDelta, Is.True, "A sessão precisa transmitir um delta antes do cancelamento.");
                    Assert.That(session.GetCancellationReport(turnId), Is.EqualTo(AgentTurnCancellationReport.MayHaveTakenEffect),
                        "Após envio, cancelamento precisa declarar resultado potencialmente efetivo, sem alegar rollback.");
                });
            }

            if (providerSessionId is not null)
                await store.DeleteSessionAsync(providerSessionId, CancellationToken.None);
            TestContext.Progress.WriteLine("Turno real cancelado após delta; relatório marcou efeito potencial; sessão volátil removida.");
        }
        finally
        {
            await store.DisposeAsync();
        }
    }

    private sealed class EmptyManualToolRegistry : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => null;
        public string? GetInputSchemaJson(string? name) => null;
        public string? GetOutputSchemaJson(string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("No product tools are allowed in this manual cancellation test.");
    }

    private static string SafeDiagnosticToken(string? value) =>
        value is { Length: > 0 and <= 64 } && value.All(static character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-')
            ? value
            : "none";

    private static string ClassifyError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "no-message";
        var normalized = message.ToLowerInvariant();
        if (normalized.Contains("model", StringComparison.Ordinal)) return "model";
        if (normalized.Contains("auth", StringComparison.Ordinal) || normalized.Contains("login", StringComparison.Ordinal)) return "auth";
        if (normalized.Contains("quota", StringComparison.Ordinal) || normalized.Contains("limit", StringComparison.Ordinal)) return "quota";
        if (normalized.Contains("rate", StringComparison.Ordinal)) return "rate-limit";
        if (normalized.Contains("permission", StringComparison.Ordinal) || normalized.Contains("forbidden", StringComparison.Ordinal)) return "permission";
        if (normalized.Contains("timeout", StringComparison.Ordinal) || normalized.Contains("timed out", StringComparison.Ordinal)) return "timeout";
        if (normalized.Contains("network", StringComparison.Ordinal) || normalized.Contains("connect", StringComparison.Ordinal)) return "network";
        return "other-query-error";
    }
}
#pragma warning restore GHCP001
