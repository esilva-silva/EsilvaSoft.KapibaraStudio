using System.Reflection;
using Avalonia.Headless;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Settings and chat states of a CLI-delegated account (P7-CL5-02) with a scripted manager: no process, network,
/// credential or model call. Covers explicit states, the global sign-out confirmation, the mode chip, the read notice
/// and that no account data other than allowlisted tokens is ever shown.
/// </summary>
[TestFixture, NonParallelizable, Category("Unit")]
public sealed class AgentCliAccountViewModelTests
{
    private static readonly string[] RuntimeCodes = ["CancelledAfterSend", "ObservedToolUnconfirmed", "NativeToolFailed"];
    private static readonly string[] Locales = ["pt-BR", "en", "es", "zh-CN"];
    private static readonly string[] UnavailableCodes =
    [
        "ExecutableNotFound", "UnsupportedExecutable", "VersionTooLow", "VersionUnreadable", "ProbeTimedOut",
        "ProbeFailed", "NotLoggedIn", "NonSubscriptionAuthentication", "BlockedEnvironment", "AuthStatusUnreadable",
        "InvalidConfiguration", "ModelNotAllowed", "ModelUnavailableForSubscription", "NoModelSelected",
    ];

    private static Task<bool> RunOnUiAsync(Func<Task> body)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
        return session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            await body();
            return true;
        }, CancellationToken.None);
    }

    private static AgentSettingsViewModel Settings(MutableAgentCatalog catalog, FakeCliAccountManager accounts) =>
        new(catalog, new RecordingCredentialSetup(), FakeCliAccountManager.ProviderId, accounts, accounts);

    private static FakeAgentPermissionsRepository ConsentedPermissions() => new(
        AgentProviderPermissions.Default(FakeCliAccountManager.ProviderId) with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
        });

    [Test]
    public async Task ExperimentalOfficialCliIsExplicitInTheProviderModeChip()
    {
        await RunOnUiAsync(() =>
        {
            var presentation = new AgentProviderPresentation("codex-subscription", "Codex — assinatura ChatGPT",
                AgentDataDestinationKind.External, false, [], [AgentAuthenticationMethod.OfficialCliDelegated],
                AgentProviderAuthState.Unknown, FamilyName: "OpenAI", IsExperimental: true);
            var option = new AgentProviderOption(presentation);

            Assert.That(option.ModeText, Is.EqualTo("OpenAI · CLI oficial · EXPERIMENTAL"));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task OpeningSettingsRunsNothingAndShowsNotCheckedStates()
    {
        await RunOnUiAsync(() =>
        {
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription(available: false, AgentProviderAuthState.Unknown, "StatusNotReported"),
                MutableAgentCatalog.Api());
            var accounts = new FakeCliAccountManager();
            using var settings = Settings(catalog, accounts);

            Assert.Multiple(() =>
            {
                Assert.That(accounts.Checks + accounts.SignIns + accounts.SignOuts, Is.Zero, "Abrir não executa a CLI.");
                Assert.That(catalog.FullRefreshes + catalog.ProviderRefreshes.Count, Is.Zero);
                Assert.That(settings.IsCliProvider, Is.True);
                Assert.That(settings.IsApiKeyProvider, Is.False);
                Assert.That(settings.CliInstallText, Does.Contain("Não verificado"));
                Assert.That(settings.CliAuthText, Does.Contain("Não verificado"));
                Assert.That(settings.SignInButtonText, Is.EqualTo("Entrar pelo Claude Code…"));
                Assert.That(settings.SignInHint, Does.Contain("terminal visível").And.Contain("Anthropic").And.Contain("não recebe"));
                Assert.That(settings.SignOutHint, Does.Contain("global"));
                Assert.That(settings.SignOutCommand.CanExecute(null), Is.False, "Sem verificação não há logout.");
                Assert.That(settings.SelectedModeText, Is.EqualTo("Claude Code · CLI oficial"));
                Assert.That(settings.TranscriptNotice, Does.Contain("~/.claude/projects"));
                Assert.That(settings.CredentialNotice, Does.Contain("~/.claude/.credentials.json").And.Contain("nunca abre"));
                Assert.That(settings.EnvironmentNotice, Does.Contain("destino").And.Contain("Claude Code"));
            });
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task TestConnectionReportsEachInstallationStateWithoutCallingTheModel()
    {
        await RunOnUiAsync(async () =>
        {
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription(false, AgentProviderAuthState.Unknown, "ExecutableNotFound"));
            var accounts = new FakeCliAccountManager();
            using var settings = Settings(catalog, accounts);
            (AgentAccountInstallState State, string Expected)[] cases =
            [
                (AgentAccountInstallState.NotFound, "não encontrado"),
                (AgentAccountInstallState.UnsupportedExecutable, "executável nativo"),
                (AgentAccountInstallState.VersionTooLow, "Versão incompatível"),
                (AgentAccountInstallState.TimedOut, "não respondeu"),
            ];
            foreach (var (state, expected) in cases)
            {
                accounts.Status = new AgentAccountStatus(state, state == AgentAccountInstallState.VersionTooLow ? "2.0.1" : null, AgentAccountAuthState.NotChecked);
                await settings.TestConnectionCommand.ExecuteAsync(null);
                Assert.Multiple(() =>
                {
                    Assert.That(settings.CliInstallText, Does.Contain(expected), state.ToString());
                    Assert.That(settings.SignInCommand.CanExecute(null), Is.False, "Sem instalação utilizável não há login.");
                    Assert.That(settings.SignOutCommand.CanExecute(null), Is.False);
                    Assert.That(settings.IsStatusError, Is.True);
                    Assert.That(settings.IsBusy, Is.False);
                });
            }

            Assert.That(accounts.Checks, Is.EqualTo(cases.Length));
            Assert.That(catalog.ProviderRefreshes, Is.All.EqualTo(FakeCliAccountManager.ProviderId), "Só o provider testado é reverificado.");
            Assert.That(catalog.FullRefreshes, Is.Zero, "Testar um provider não lê o cofre dos demais.");
            Assert.That(accounts.SignIns + accounts.SignOuts, Is.Zero);
        });
    }

    [Test]
    public async Task TestConnectionUsesTheSharedForcedInitializationFlight()
    {
        await RunOnUiAsync(async () =>
        {
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription());
            var accounts = new FakeCliAccountManager { Status = FakeCliAccountManager.Subscription() };
            var availability = new AgentProviderAvailabilityService(catalog, accounts: accounts);
            using var settings = new AgentSettingsViewModel(catalog, new RecordingCredentialSetup(),
                FakeCliAccountManager.ProviderId, accounts, accounts, availability);

            await settings.TestConnectionCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(accounts.Checks, Is.EqualTo(1));
                Assert.That(catalog.ProviderRefreshes, Is.EqualTo(new[] { FakeCliAccountManager.ProviderId }));
                Assert.That(settings.CliStatus?.Auth, Is.EqualTo(AgentAccountAuthState.Subscription));
                Assert.That(settings.IsCliSignedIn, Is.True);
                Assert.That(settings.IsStatusError, Is.False);
            });
        });
    }

    [Test]
    public async Task SubscriptionShowsOnlyTheTierAndBlockedMethodExplainsWithoutValues()
    {
        await RunOnUiAsync(async () =>
        {
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription());
            var accounts = new FakeCliAccountManager { Status = FakeCliAccountManager.Subscription() };
            using var settings = Settings(catalog, accounts);
            await settings.TestConnectionCommand.ExecuteAsync(null);
            Assert.Multiple(() =>
            {
                Assert.That(settings.CliInstallText, Is.EqualTo("Instalado · versão 2.1.268"));
                Assert.That(settings.CliExecutableText, Does.Contain(FakeCliAccountManager.FakeExecutablePath));
                Assert.That(settings.CliAuthText, Is.EqualTo("Autenticado com assinatura"));
                Assert.That(settings.CliAccountTypeText, Is.EqualTo("Assinatura (Pro)"));
                Assert.That(settings.SignInCommand.CanExecute(null), Is.False, "Já autenticado.");
                Assert.That(settings.SignOutCommand.CanExecute(null), Is.True);
                Assert.That(settings.IsStatusError, Is.False);
            });

            accounts.Status = FakeCliAccountManager.BlockedByApiKey();
            await settings.TestConnectionCommand.ExecuteAsync(null);
            Assert.Multiple(() =>
            {
                Assert.That(settings.IsCliAuthBlocked, Is.True);
                Assert.That(settings.CliAuthText, Does.Contain("Bloqueado"));
                Assert.That(settings.CliAuthExplanation, Does.Contain("ANTHROPIC_API_KEY").And.Contain("CLI oficial").And.Contain("não lê nem remove"));
                Assert.That(settings.CliAccountTypeText, Does.StartWith("Não informado"));
                Assert.That(settings.SignInCommand.CanExecute(null), Is.False, "Login não resolve variável no ambiente.");
                Assert.That(settings.StatusText, Does.Contain("Bloqueado"));
                Assert.That(settings.IsStatusError, Is.True);
            });
        });
    }

    [TestCase(AgentAccountAuthState.ApiKey, "Autenticado com API key", "Cobrança da API pelo Claude Code")]
    [TestCase(AgentAccountAuthState.ApiKeyHelper, "Autenticado por um auxiliar de API key", "auxiliar de API key configurado no Claude Code")]
    [TestCase(AgentAccountAuthState.EnvironmentToken, "Autenticado com token do ambiente", "token configurado no Claude Code")]
    [TestCase(AgentAccountAuthState.CloudProvider, "Autenticado por um provedor de nuvem configurado", "provedor de nuvem configurado no Claude Code")]
    [TestCase(AgentAccountAuthState.UnsupportedMethod, "Autenticado pelo Claude Code com um método nativo não classificado pelo painel", "login Console sem chave usa a cobrança da API Console")]
    public async Task NativeCliAuthenticationShowsMethodAndBillingWithoutShowingCredentialValues(
        AgentAccountAuthState auth, string expectedAuthText, string expectedBillingText)
    {
        await RunOnUiAsync(async () =>
        {
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription());
            var accounts = new FakeCliAccountManager
            {
                Status = new AgentAccountStatus(AgentAccountInstallState.Installed, "2.1.268", auth,
                    BlockingSource: "sk-ant-secret-canary", ExecutablePath: FakeCliAccountManager.FakeExecutablePath),
            };
            using var settings = Settings(catalog, accounts);
            await settings.TestConnectionCommand.ExecuteAsync(null);

            Assert.Multiple(() =>
            {
                Assert.That(settings.CliAuthText, Is.EqualTo(expectedAuthText));
                Assert.That(settings.CliAccountTypeText, Does.Contain(expectedBillingText));
                Assert.That(settings.CliAuthExplanation.ToLowerInvariant(), Does.Contain("claude code").And.Contain("o app não lê"));
                Assert.That(settings.IsCliAuthenticationConfigured, Is.True);
                Assert.That(settings.IsCliAuthBlocked, Is.False);
                Assert.That(settings.IsCliSignedIn, Is.False, "Métodos nativos sem assinatura não devem ser rotulados como login por assinatura.");
                Assert.That(settings.SignInCommand.CanExecute(null), Is.False, "Não abrir login de assinatura sobre método nativo já configurado.");
                Assert.That(settings.SignOutCommand.CanExecute(null), Is.False, "Não oferecer logout global da conta de assinatura para outras credenciais.");
                Assert.That(settings.StatusText, Does.Contain("Autenticação nativa configurada"));
                Assert.That(settings.StatusText, Does.Not.Contain("Bloqueado"));
                Assert.That(settings.IsStatusError, Is.False);
            });
            if (auth == AgentAccountAuthState.UnsupportedMethod)
            {
                Assert.That(settings.CliAuthExplanation, Does.Contain("Console sem API key").And.Contain("/status"),
                    "O login keyless do Console é perfil OAuth nativo e continua sob billing configurado na CLI.");
            }

            var texts = typeof(AgentSettingsViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                .Select(p => (string?)p.GetValue(settings) ?? "")
                .ToArray();
            Assert.That(texts.Any(t => t.Contains("sk-ant-secret-canary", StringComparison.Ordinal)), Is.False,
                "A origem/valor de credencial recebido da CLI nunca é exibido.");
        });
    }

    [Test]
    public async Task ClaudeCodeModeChipNamesTheOfficialCliInsteadOfAssumingSubscriptionBilling()
    {
        await RunOnUiAsync(() =>
        {
            var option = new AgentProviderOption(new AgentProviderPresentation("claude-code", "Claude Code (CLI oficial)",
                AgentDataDestinationKind.External, true, [], [AgentAuthenticationMethod.OfficialCliDelegated],
                AgentProviderAuthState.Configured, FamilyName: "Claude Code"));

                Assert.That(option.ModeText, Is.EqualTo("Claude Code · CLI oficial"));
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task SignOutRequiresExplicitGlobalConfirmationAndCancellingKeepsTheSession()
    {
        await RunOnUiAsync(async () =>
        {
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription());
            var accounts = new FakeCliAccountManager { Status = FakeCliAccountManager.Subscription() };
            using var settings = Settings(catalog, accounts);
            await settings.TestConnectionCommand.ExecuteAsync(null);

            AgentCliSignOutPrompt? seen = null;
            settings.ConfirmSignOut = prompt =>
            {
                seen = prompt;
                return Task.FromResult(false);
            };
            await settings.SignOutCommand.ExecuteAsync(null);
            Assert.Multiple(() =>
            {
                Assert.That(seen, Is.Not.Null);
                Assert.That(seen!.Message, Does.Contain("todo o seu usuário do sistema operacional").And.Contain("terminais fora do KapibaraStudio"));
                Assert.That(seen.ConfirmText, Is.EqualTo("Sair do Claude Code em todo o sistema"));
                Assert.That(seen.CancelText, Is.EqualTo("Cancelar"));
                Assert.That(accounts.SignOuts, Is.Zero, "Cancelar a confirmação não executa o logout.");
                Assert.That(settings.IsCliSignedIn, Is.True);
                Assert.That(settings.StatusText, Does.Contain("cancelado"));
            });

            settings.ConfirmSignOut = null;
            await settings.SignOutCommand.ExecuteAsync(null);
            Assert.That(accounts.SignOuts, Is.Zero, "Sem diálogo de confirmação nunca há logout.");

            settings.ConfirmSignOut = _ => Task.FromResult(true);
            catalog.OnRefresh = p => MutableAgentCatalog.Subscription(false, AgentProviderAuthState.NotConfigured, "NotLoggedIn");
            await settings.SignOutCommand.ExecuteAsync(null);
            Assert.Multiple(() =>
            {
                Assert.That(accounts.SignOuts, Is.EqualTo(1));
                Assert.That(accounts.LastSignOutConfirmation, Is.True);
                Assert.That(settings.CliAuthText, Is.EqualTo("Não autenticado"));
                Assert.That(settings.StatusText, Does.Contain("Sessão encerrada"));
                Assert.That(settings.SelectedProvider!.UnavailableText, Does.Contain("Entrar pelo Claude Code"));
                Assert.That(settings.SignInCommand.CanExecute(null), Is.True);
            });
        });
    }

    [Test]
    public async Task SignInWaitsWithoutBlockingAndStopWaitingRereadsTheState()
    {
        await RunOnUiAsync(async () =>
        {
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription(false, AgentProviderAuthState.NotConfigured, "NotLoggedIn"));
            var accounts = new FakeCliAccountManager { Status = FakeCliAccountManager.SignedOut(), SignInGate = new TaskCompletionSource() };
            using var settings = Settings(catalog, accounts);
            await settings.TestConnectionCommand.ExecuteAsync(null);
            Assert.That(settings.SignInCommand.CanExecute(null), Is.True);

            var signIn = settings.SignInCommand.ExecuteAsync(null);
            Assert.Multiple(() =>
            {
                Assert.That(signIn.IsCompleted, Is.False, "A espera pela janela da CLI é assíncrona.");
                Assert.That(settings.IsBusy, Is.True);
                Assert.That(settings.IsWaitingForSignIn, Is.True);
                Assert.That(settings.ProgressText, Does.Contain("Aguardando o login na janela do Claude Code"));
                Assert.That(settings.TestConnectionCommand.CanExecute(null), Is.False);
                Assert.That(settings.StopWaitingCommand.CanExecute(null), Is.True);
            });

            accounts.Status = FakeCliAccountManager.Subscription();
            settings.StopWaitingCommand.Execute(null);
            await signIn;
            Assert.Multiple(() =>
            {
                Assert.That(settings.IsBusy, Is.False);
                Assert.That(settings.IsWaitingForSignIn, Is.False);
                Assert.That(settings.IsCliSignedIn, Is.True, "Parar de aguardar relê o estado.");
                Assert.That(settings.StatusText, Does.Contain("continua aberta"));
            });
        });
    }

    [Test]
    public async Task NoVisibleTerminalShowsTheExactCommandToRunManually()
    {
        await RunOnUiAsync(async () =>
        {
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription(false, AgentProviderAuthState.NotConfigured, "NotLoggedIn"));
            var accounts = new FakeCliAccountManager
            {
                Status = FakeCliAccountManager.SignedOut(),
                SignInResult = new AgentAccountCommandResult(AgentAccountCommandOutcome.NoVisibleTerminal, null),
            };
            using var settings = Settings(catalog, accounts);
            await settings.TestConnectionCommand.ExecuteAsync(null);
            await settings.SignInCommand.ExecuteAsync(null);
            Assert.Multiple(() =>
            {
                Assert.That(settings.ShowManualCommand, Is.True);
                Assert.That(settings.ManualCommand, Is.EqualTo("claude auth login"));
                Assert.That(settings.StatusText, Does.Contain("terminal visível"));
            });
        });
    }

    [Test]
    public async Task NoAccountDataBeyondAllowlistedTokensAppearsInAnyText()
    {
        await RunOnUiAsync(async () =>
        {
            // The port cannot carry e-mail/org/tokens; the check proves the VM never builds text from anything else
            // (e.g. an unexpected tier value is shown only as the safe token given).
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription());
            var accounts = new FakeCliAccountManager { Status = FakeCliAccountManager.Subscription("max"), WorkspaceDirectory = null };
            using var settings = Settings(catalog, accounts);
            await settings.TestConnectionCommand.ExecuteAsync(null);
            var texts = typeof(AgentSettingsViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(static p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0)
                .Select(p => (string?)p.GetValue(settings) ?? "")
                .ToArray();
            string[] forbidden = ["@", "sk-ant", "orgId", "oauth", "token="];
            Assert.That(texts.Where(t => forbidden.Any(f => t.Contains(f, StringComparison.OrdinalIgnoreCase))), Is.Empty);
            Assert.That(settings.CliAccountTypeText, Is.EqualTo("Assinatura (Max)"));
        });
    }

    [Test]
    public async Task ChatShowsModeChipReadNoticeAndBlocksSendingWithReason()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var catalog = new MutableAgentCatalog(
                MutableAgentCatalog.Subscription(false, AgentProviderAuthState.Invalid, "BlockedEnvironment"), MutableAgentCatalog.Api());
            var accounts = new FakeCliAccountManager();
            var tab = new AgentChatTabFixture();
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, catalog, new FakeAgentContextProvider(), AccountManager: accounts), tab.Capture);
            chat.SelectedProvider = chat.Providers.Single(p => p.ProviderId == FakeCliAccountManager.ProviderId);
            chat.ComposerText = "resuma a coleção";

            Assert.Multiple(() =>
            {
                Assert.That(chat.ModeText, Is.EqualTo("Claude Code · CLI oficial"));
                Assert.That(chat.ReadScopeText, Does.Contain("Sem pasta de workspace").And.Contain("toda leitura pedirá aprovação").And.Contain("Anthropic"));
                Assert.That(chat.State, Is.EqualTo(AgentChatState.CredentialExpired));
                Assert.That(chat.StatusText, Does.Contain("bloqueado, envio desabilitado").And.Contain("destino").And.Contain("reinicie"));
                Assert.That(chat.IsStatusError, Is.True);
                Assert.That(chat.SendCommand.CanExecute(null), Is.False, "Bloqueado: sem fallback e sem envio.");
            });

            accounts.WorkspaceDirectory = SyntheticPaths.Combine("workspace-sintetico");
            chat.RefreshReadScope();
            Assert.That(chat.ReadScopeText, Does.Contain("workspace-sintetico").And.Contain("enviado à Anthropic pelo Claude Code"));

            chat.SelectedProvider = chat.Providers.Single(p => p.ProviderId == "api-key");
            Assert.Multiple(() =>
            {
                Assert.That(chat.ModeText, Is.EqualTo("Claude · API"));
                Assert.That(chat.HasReadScope, Is.False, "O aviso de leitura nativa só existe no modo assinatura.");
            });
            Assert.That(runtime.LastRequest, Is.Null, "Nenhuma chamada ao modelo.");
            Assert.That(accounts.Checks + accounts.SignIns + accounts.SignOuts, Is.Zero, "O chat não executa a CLI.");
        });
    }

    [Test]
    public async Task SessionWorkingDirectoryIsTheFolderCapturedOnTheUiThreadWhenTheSessionStarts()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime { SessionGate = new TaskCompletionSource() };
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription());
            var folderA = SyntheticPaths.Combine("workspace-a");
            var folderB = SyntheticPaths.Combine("workspace-b");
            var tab = new AgentChatTabFixture { WorkspaceFolder = folderA };
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(runtime, catalog, new FakeAgentContextProvider(), AccountManager: new FakeCliAccountManager())
                {
                    Permissions = ConsentedPermissions(),
                }, tab.Capture);
            await chat.Initialization;
            chat.ComposerText = "leia o arquivo";
            Assert.That(chat.SendCommand.CanExecute(null), Is.True,
                $"state={chat.State}; block={chat.SendBlock}; permissions={chat.CurrentPermissions is not null}; scope={chat.ReadScopeText}");
            _ = chat.SendCommand.ExecuteAsync(null);

            // The folder changes while the session start is still awaiting: the session keeps the captured value.
            tab.WorkspaceFolder = folderB;
            chat.RefreshReadScope();
            runtime.SessionGate.SetResult();
            await PumpUntilAsync(() => runtime.LastRequest is not null);
            Assert.Multiple(() =>
            {
                Assert.That(runtime.SessionOptions.Single().WorkingDirectory, Is.EqualTo(folderA));
                Assert.That(chat.SessionWorkingDirectory, Is.EqualTo(folderA));
                Assert.That(chat.ReadScopeText, Does.Contain(folderB).And.Contain("vale só para sessões novas")
                    .And.Contain("A sessão atual continua com: " + folderA));
            });

            runtime.Push(runtime.LastRequest!.TurnId, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
            await PumpUntilAsync(() => chat.State == AgentChatState.Completed);
            chat.ComposerText = "e agora?";
            _ = chat.SendCommand.ExecuteAsync(null);
            await PumpUntilAsync(() => chat.State is AgentChatState.Generating);
            Assert.That(runtime.SessionOptions, Has.Count.EqualTo(1), "A sessão existente é reutilizada com a pasta original.");
            runtime.Push(runtime.LastRequest!.TurnId, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
            await PumpUntilAsync(() => chat.State == AgentChatState.Completed);
        });
    }

    [Test]
    public async Task WithoutFolderTheSessionReceivesNullWorkingDirectory()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var tab = new AgentChatTabFixture();
            await using var chat = new AgentChatViewModel(new AgentChatServices(runtime,
                new MutableAgentCatalog(MutableAgentCatalog.Subscription()), new FakeAgentContextProvider(),
                AccountManager: new FakeCliAccountManager()) { Permissions = ConsentedPermissions() }, tab.Capture);
            await chat.Initialization;
            chat.ComposerText = "oi";
            _ = chat.SendCommand.ExecuteAsync(null);
            await PumpUntilAsync(() => runtime.LastRequest is not null);
            Assert.That(runtime.SessionOptions.Single().WorkingDirectory, Is.Null);
            Assert.That(chat.ReadScopeText, Does.Contain("Sem pasta de workspace"));
            runtime.Push(runtime.LastRequest!.TurnId, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
            await PumpUntilAsync(() => chat.State == AgentChatState.Completed);
        });
    }

    private static async Task PumpUntilAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 5000;
        while (!condition())
        {
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (Environment.TickCount64 > deadline)
            {
                Assert.Fail("Condição não atingida.");
            }

            await Task.Delay(10);
        }
    }

    [Test]
    public async Task RefusedFolderIsExplainedAndUnusableDedicatedFolderBlocksSending()
    {
        await RunOnUiAsync(async () =>
        {
            var folder = SyntheticPaths.Combine("perfil-com-ssh");
            var accounts = new FakeCliAccountManager
            {
                ScopeFor = candidate => new AgentCliReadScope(candidate, FakeCliAccountManager.DedicatedFolder, false,
                    AgentCliReadScopeRejection.ProtectedArea, AgentCliProtectedArea.SshKeys, AgentCliProtectedRelation.Contains),
            };
            var runtime = new ChannelAgentRuntime();
            var tab = new AgentChatTabFixture { WorkspaceFolder = folder };
            await using var chat = new AgentChatViewModel(new AgentChatServices(runtime,
                new MutableAgentCatalog(MutableAgentCatalog.Subscription()), new FakeAgentContextProvider(), AccountManager: accounts),
                tab.Capture);
            chat.ComposerText = "leia";
            Assert.Multiple(() =>
            {
                Assert.That(chat.ReadScopeText, Does.Contain("A pasta " + folder + " foi recusada porque contém ~/.ssh")
                    .And.Contain("pasta dedicada vazia").And.Contain("toda leitura pedirá aprovação"));
                Assert.That(chat.IsReadScopeBlocked, Is.False, "Recusa com pasta dedicada utilizável não bloqueia.");
                Assert.That(chat.IsReadScopeBlocked, Is.False);
            });

            accounts.ScopeFor = candidate => new AgentCliReadScope(candidate, null, false, AgentCliReadScopeRejection.ProtectedArea,
                AgentCliProtectedArea.AppData, AgentCliProtectedRelation.SameOrInside, DedicatedDirectoryUsable: false);
            chat.RefreshReadScope();
            Assert.Multiple(() =>
            {
                Assert.That(chat.IsReadScopeBlocked, Is.True);
                Assert.That(chat.State, Is.EqualTo(AgentChatState.ReadScopeUnavailable));
                Assert.That(chat.StatusText, Does.Contain("sem pasta de trabalho utilizável, envio desabilitado"));
                Assert.That(chat.IsStatusError, Is.True);
                Assert.That(chat.SendCommand.CanExecute(null), Is.False, "Sem fallback: o envio fica desabilitado.");
                Assert.That(chat.ReadScopeText, Does.Contain("Nenhuma pasta de trabalho utilizável"));
            });
            Assert.That(accounts.ScopeCandidates, Is.All.EqualTo(folder), "A candidata é a pasta capturada na UI.");
            Assert.That(runtime.LastRequest, Is.Null);
        });
    }

    [Test]
    public async Task SignedOutCliProviderPointsToSignInNotToAnApiKey()
    {
        await RunOnUiAsync(async () =>
        {
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription(false, AgentProviderAuthState.NotConfigured, "NotLoggedIn"));
            var tab = new AgentChatTabFixture();
            await using var chat = new AgentChatViewModel(
                new AgentChatServices(new ChannelAgentRuntime(), catalog, new FakeAgentContextProvider(), AccountManager: new FakeCliAccountManager()),
                tab.Capture);
            Assert.That(chat.State, Is.EqualTo(AgentChatState.NotAuthenticated));
            Assert.That(chat.StatusText, Does.Contain("não está conectado").And.Contain("CLI oficial").And.Not.Contain("chave de API"));
        });
    }

    [Test]
    public async Task EveryTypedTurnCodeOfTheSubscriptionModeIsLocalizedInFourLanguages()
    {
        await RunOnUiAsync(() =>
        {
            foreach (var language in Locales)
            {
                LocalizationViewModel.Current.Language = language;
                foreach (var code in EsilvaSoft.KapibaraStudio.Infrastructure.Agents.ClaudeCode.ClaudeCodeErrorCodes.TurnErrorCodes.Concat(RuntimeCodes))
                {
                    Assert.That(LocalizationViewModel.Current.HasTranslation(AgentChatViewModel.ErrorCodePrefix + code), Is.True, language + ": " + code);
                }
            }

            LocalizationViewModel.Current.Language = "pt-BR";
            return Task.CompletedTask;
        });
    }

    [Test]
    public async Task UnknownAndKnownUnavailableCodesAreLocalizedNeverRaw()
    {
        await RunOnUiAsync(() =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            foreach (var code in UnavailableCodes)
            {
                var option = new AgentProviderOption(MutableAgentCatalog.Subscription(false, AgentProviderAuthState.Unknown, code));
                Assert.That(option.UnavailableText, Is.Not.EqualTo(code).And.Not.StartWith("[["), code);
            }

            var unknown = new AgentProviderOption(MutableAgentCatalog.Subscription(false, AgentProviderAuthState.Unknown, "SomeFutureCode"));
            Assert.That(unknown.UnavailableText, Does.Contain("não descreve").And.Contain("SomeFutureCode"));
            foreach (var language in Locales.Skip(1))
            {
                LocalizationViewModel.Current.Language = language;
                Assert.That(new AgentProviderOption(MutableAgentCatalog.Subscription(false, AgentProviderAuthState.Unknown, "BlockedEnvironment"))
                    .UnavailableText, Does.Not.Contain("ANTHROPIC_API_KEY"), language);
            }

            LocalizationViewModel.Current.Language = "pt-BR";
            return Task.CompletedTask;
        });
    }
}
