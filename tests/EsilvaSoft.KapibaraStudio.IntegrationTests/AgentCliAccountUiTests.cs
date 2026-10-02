using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.UnitTests;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// P7-CL5-02: settings of the CLI-delegated account in every state, the global sign-out confirmation and the mode chip
/// and read notice in the hosted chat, rendered with Skia in light and dark themes (settings 660 × 560 and 520 × 420 at
/// 200%; main window 960 × 620 and 1366 × 768, plus 1366 at 200%). Synthetic fixtures: scripted account manager (no
/// process, no real CLI, no account), fake catalog and channel runtime. Not screen-reader or native-dialog homologation.
/// </summary>
[TestFixture, NonParallelizable, Category("Ui"), Category("Integration")]
public sealed class AgentCliAccountUiTests
{
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    private static readonly string[] SettingsStates =
        ["not-checked", "not-found", "unsupported", "version-low", "signed-out", "subscription", "api-key", "api-key-helper",
            "environment-token", "cloud-provider", "native-unclassified", "blocked", "waiting", "no-terminal"];

    private static readonly (double Width, double Height, double Scale)[] ChatSizes = [(400, 720, 1), (480, 768, 2)];
    private static readonly (Size Size, double Scale)[] HostSizes = [(new Size(960, 620), 1), (new Size(1366, 768), 1), (new Size(1366, 768), 2)];
    private static readonly string[] NarrowStates = ["subscription", "blocked"];
    private static readonly string[] HostModes = ["subscription-folder", "subscription-no-folder", "subscription-refused", "subscription-blocked"];

    private static Task<bool> RunOnUiAsync(Func<Task> body)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IntegrationUiTestApp).Assembly);
        return session.Dispatch(async () =>
        {
            LocalizationViewModel.Current.Language = "pt-BR";
            try
            {
                await body();
            }
            finally
            {
                Avalonia.Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
            }

            return true;
        }, CancellationToken.None);
    }

    [Test]
    public async Task SettingsRenderEveryAccountStateInBothThemes()
    {
        await RunOnUiAsync(async () =>
        {
            foreach (var state in SettingsStates)
            {
                var (window, settings, accounts, pending) = await BuildSettingsAsync(state, 660, 560);
                foreach (var theme in Themes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    await PumpAsync(() => true);
                    AssertSettingsVisuals(window, settings, state);
                    ScrollTo(window, top: true);
                    Save(window, $"agent-cli-settings-{state}-{theme}-660.png");
                    ScrollTo(window, top: false);
                    Save(window, $"agent-cli-settings-{state}-{theme}-660-end.png");
                }

                if (pending is not null)
                {
                    settings.StopWaitingCommand.Execute(null);
                    await pending;
                }

                Assert.That(accounts.SignOuts, Is.Zero, "Renderizar nunca executa logout.");
                window.Close();
                await PumpAsync(() => true);
            }
        });
    }

    [Test]
    public async Task CopilotSubscriptionAccountRendersItsOwnNoticesInBothThemes()
    {
        await RunOnUiAsync(async () =>
        {
            var profile = new AgentCliProviderProfile(
                "GitHub Copilot CLI", "GitHub Copilot", "copilot login",
                "~/.copilot/session-state", "Copilot CLI keyring service", "Copilot CLI configuration",
                SignOutMessageKey: "agentCliCopilotSignOutMessage",
                CredentialNoticeKey: "agentCliCopilotCredentialNotice",
                SubscriptionReadyMessageKey: "agentCliCopilotAccountChecked",
                SignInCompletedMessageKey: "agentCliCopilotAccountChecked");
            var (window, settings, _, _) = await BuildSettingsAsync("subscription", 660, 560, profile, copilot: true);
            foreach (var theme in Themes)
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                await PumpAsync(() => true);
                ScrollTo(window, top: true);
                Assert.That(settings.CliProfile?.RecipientName, Is.EqualTo("GitHub Copilot"));
                Save(window, $"agent-copilot-settings-{theme}-660.png");
                ScrollTo(window, top: false);
                Save(window, $"agent-copilot-settings-{theme}-660-end.png");
            }

            settings.Dispose();
            window.Close();
        });
    }

    [Test]
    public async Task SettingsFitNarrowWindowAt200Percent()
    {
        await RunOnUiAsync(async () =>
        {
            foreach (var state in NarrowStates)
            {
                var (window, _, _, _) = await BuildSettingsAsync(state, 520, 420);
                foreach (var theme in Themes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.SetRenderScaling(2);
                    await PumpAsync(() => true);
                    var close = window.FindControl<Button>("CloseButton")!;
                    var origin = close.TranslatePoint(new Point(), window)!.Value;
                    Assert.That(origin.X + close.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width + 0.5), state);
                    Assert.That(origin.Y + close.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height + 0.5), state);
                    ScrollTo(window, top: false);
                    Save(window, $"agent-cli-settings-{state}-{theme}-520-2.png");
                    ScrollTo(window, top: true);
                    Save(window, $"agent-cli-settings-{state}-{theme}-520-2-top.png");
                }

                window.SetRenderScaling(1);
                window.Close();
                await PumpAsync(() => true);
            }
        });
    }

    [Test]
    public async Task SignOutDialogIsGlobalCancelHasFocusAndEscapeKeepsTheSession()
    {
        await RunOnUiAsync(async () =>
        {
            var (window, settings, accounts, _) = await BuildSettingsAsync("subscription", 660, 560);
            foreach (var theme in Themes)
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                var signOut = settings.SignOutCommand.ExecuteAsync(null);
                await PumpAsync(() => window.OpenSignOutConfirmation is { IsVisible: true });
                var dialog = window.OpenSignOutConfirmation!;
                await PumpAsync(() => dialog.FindControl<Button>("CancelButton")!.IsFocused);
                Assert.Multiple(() =>
                {
                    Assert.That(dialog.FindControl<TextBlock>("MessageText")!.Text, Does.Contain("todo o seu usuário do sistema operacional"));
                    Assert.That(dialog.FindControl<Button>("ConfirmButton")!.Content, Is.EqualTo("Sair do Claude Code em todo o sistema"));
                });
                Save(dialog, $"agent-cli-signout-dialog-{theme}.png");

                // Enter on the focused Cancel and Escape are both safe: nothing is signed out.
                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                await signOut;
                Assert.That(accounts.SignOuts, Is.Zero);
                Assert.That(settings.IsCliSignedIn, Is.True);
                Assert.That(settings.StatusText, Does.Contain("cancelado"));
                await PumpAsync(() => window.FindControl<Button>("SignOutButton")!.IsFocused, 2000);
            }

            // Explicit confirmation is the only path to the (scripted) global sign-out.
            var confirmed = settings.SignOutCommand.ExecuteAsync(null);
            await PumpAsync(() => window.OpenSignOutConfirmation is { IsVisible: true });
            window.OpenSignOutConfirmation!.FindControl<Button>("ConfirmButton")!
                .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await confirmed;
            Assert.That(accounts.SignOuts, Is.EqualTo(1));
            Assert.That(accounts.LastSignOutConfirmation, Is.True);
            window.Close();
            await PumpAsync(() => true);
        });
    }

    [Test]
    public async Task HostedChatShowsModeChipAndReadNoticeInBothThemesAndSizes()
    {
        await RunOnUiAsync(async () =>
        {
            foreach (var mode in HostModes)
            {
                var runtime = new ChannelAgentRuntime();
                var accounts = new FakeCliAccountManager
                {
                    ScopeFor = mode switch
                    {
                        "subscription-refused" => candidate => new AgentCliReadScope(candidate, FakeCliAccountManager.DedicatedFolder, false,
                            AgentCliReadScopeRejection.ProtectedArea, AgentCliProtectedArea.SshKeys, AgentCliProtectedRelation.Contains),
                        "subscription-blocked" => candidate => new AgentCliReadScope(candidate, null, false, AgentCliReadScopeRejection.ProtectedArea,
                            AgentCliProtectedArea.AppData, AgentCliProtectedRelation.SameOrInside, DedicatedDirectoryUsable: false),
                        _ => null,
                    },
                };
                var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription());
                var context = new WorkspaceTestContext();
                var profile = ConnectionProfile.Create("Desenvolvimento · Loja", "mongodb://localhost:27017");
                await context.Repository.SaveAsync(profile);
                var vm = new WorkspaceViewModel(context.Workspace, context.Repository,
                    agentChat: new AgentChatServicesFactory(() =>
                        new AgentChatServices(runtime, catalog, new FakeAgentContextProvider(), AccountManager: accounts)));
                var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
                window.Show();
                await window.InitializationTask;
                // The chat reads the Files panel folder from the workspace (the value a new session would capture).
                vm.WorkspaceRootPath = mode is "subscription-folder" or "subscription-refused" or "subscription-blocked" ? SyntheticWorkspace : null;
                vm.IsAgentPanelOpen = true;
                await PumpAsync(() => vm.ActiveAgentChat is not null);
                var chat = vm.ActiveAgentChat!;
                await PumpAsync(() => true);
                Assert.That(chat.IsReadScopeBlocked, Is.EqualTo(mode == "subscription-blocked"), mode);
                var panel = window.AgentChatPanel;
                foreach (var theme in Themes)
                    foreach (var (size, scale) in HostSizes)
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        window.Width = size.Width;
                        window.Height = size.Height;
                        window.SetRenderScaling(scale);
                        await PumpAsync(() => true);
                        var selector = panel.FindControl<ComboBox>("ProviderSelector")!;
                        var badge = selector.GetVisualDescendants().OfType<TextBlock>()
                            .Single(text => text.Text == chat.ProviderSummary);
                        var destination = panel.FindControl<TextBlock>("DestinationBadge")!;
                        Assert.Multiple(() =>
                        {
                            Assert.That(badge.IsEffectivelyVisible, Is.True, mode);
                            Assert.That(badge.Text, Is.EqualTo("Claude Code · CLI oficial"));
                            Assert.That(destination.Text, Is.EqualTo("Externo"));
                            Assert.That(((Border)destination.Parent!).Background, Is.Not.Null.And.Not.EqualTo(Brushes.Transparent));
                            Assert.That(panel.FindControl<Border>("ReadScopeNotice")!.IsEffectivelyVisible, Is.EqualTo(mode != "api"));
                        });
                        Save(window, $"agent-cli-host-{mode}-{theme}-{size.Width}x{size.Height}-{scale.ToString(CultureInfo.InvariantCulture)}.png");
                    }

                window.SetRenderScaling(1);
                Assert.That(runtime.LastRequest, Is.Null, "Nenhuma chamada ao modelo.");
                Assert.That(accounts.Checks + accounts.SignIns + accounts.SignOuts, Is.Zero, "O chat não executa a CLI.");
                window.Close();
                await PumpAsync(() => !window.IsVisible);
                vm.Dispose();
                context.Dispose();
            }
        });
    }

    [Test]
    public async Task NativeReadCardsAndCancelledAfterSendRenderInBothThemes()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var accounts = new FakeCliAccountManager { WorkspaceDirectory = SyntheticWorkspace };
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription());
            var tab = new AgentChatTabFixture { WorkspaceFolder = SyntheticWorkspace };
            var chat = new AgentChatViewModel(new AgentChatServices(runtime, catalog, new FakeAgentContextProvider(), AccountManager: accounts)
            {
                Permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default(FakeCliAccountManager.ProviderId) with
                {
                    ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
                }),
            }, tab.Capture);
            var window = new Window { Content = new AgentChatPanel { DataContext = chat }, Width = 400, Height = 720 };
            window.Show();
            await chat.Initialization;
            chat.ComposerText = "Leia o arquivo consultas/clientes.js e explique o filtro.";
            _ = chat.SendCommand.ExecuteAsync(null);
            await PumpAsync(() => runtime.LastRequest is not null);
            var turn = runtime.LastRequest!.TurnId;
            runtime.Push(turn, AgentEventKind.TaskStarted);
            var read = AgentToolCallId.New();
            runtime.Push(turn, AgentEventKind.ToolRequested, call: read, tool: "Read", origin: AgentToolOrigin.ProviderObserved);
            runtime.Push(turn, AgentEventKind.ToolStarted, call: read, tool: "Read", origin: AgentToolOrigin.ProviderObserved);
            runtime.Push(turn, AgentEventKind.ToolCompleted, call: read, tool: "Read", status: AgentToolResultStatus.Succeeded,
                origin: AgentToolOrigin.ProviderObserved);
            var grep = AgentToolCallId.New();
            runtime.Push(turn, AgentEventKind.ToolRequested, call: grep, tool: "Grep", origin: AgentToolOrigin.ProviderObserved);
            runtime.Push(turn, AgentEventKind.ToolFailed, call: grep, tool: "Grep", status: AgentToolResultStatus.Failed,
                errorCode: "NativeToolFailed", origin: AgentToolOrigin.ProviderObserved);
            var find = AgentToolCallId.New();
            runtime.Push(turn, AgentEventKind.ToolRequested, call: find, tool: "mongo_find", origin: AgentToolOrigin.Registry);
            runtime.Push(turn, AgentEventKind.ToolStarted, call: find, tool: "mongo_find", origin: AgentToolOrigin.Registry);
            var message = AgentMessageId.New();
            runtime.Push(turn, AgentEventKind.MessageStarted, message: message);
            runtime.Push(turn, AgentEventKind.MessageDelta, "O arquivo filtra clientes ativos por data de cadastro…", message: message);
            await PumpAsync(() => chat.Items.OfType<AgentToolCallItem>().Count() == 3 && chat.State == AgentChatState.WaitingTool);
            var cards = chat.Items.OfType<AgentToolCallItem>().ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(cards[0].Title, Is.EqualTo("Leitura nativa Read"));
                Assert.That(cards[0].OriginText, Does.Contain("Executada pelo próprio Claude Code").And.Contain("fora das ferramentas do KapibaraStudio"));
                Assert.That(cards[1].StatusText, Does.Contain("A leitura nativa falhou ou foi negada").And.Not.Contain("NativeToolFailed"));
                Assert.That(cards[2].Title, Is.EqualTo("Ferramenta mongo_find"));
                Assert.That(cards[2].OriginText, Does.Contain("auditoria"));
            });
            var confirmation = new AgentToolConfirmationCardItem(new AgentToolConfirmationRequest(
                chat.ActiveConversation.Id, "claude-code", "Read", AgentConfirmationCategories.NativeFileRead,
                "{\"file_path\":\"C:\\\\workspace\\\\README.md\"}", "toolu_approval"), CancellationToken.None);
            chat.Items.Add(confirmation);
            await PumpAsync(() => confirmation.IsPending);
            Assert.That(confirmation.CanApproveThisSession, Is.True);
            var documentReadConfirmation = new AgentToolConfirmationCardItem(new AgentToolConfirmationRequest(
                chat.ActiveConversation.Id, AgentProviderIds.GitHubCopilotSubscription, AgentToolRegistry.MongoFindToolName,
                AgentConfirmationCategories.MongoDocumentRead, "{}", null), CancellationToken.None);
            Assert.That(documentReadConfirmation.CanApproveThisSession, Is.False,
                "Document-value reads always require an individual approval, never a session grant.");
            var copilotMetadataConfirmation = new AgentToolConfirmationCardItem(new AgentToolConfirmationRequest(
                chat.ActiveConversation.Id, AgentProviderIds.GitHubCopilotSubscription,
                AgentToolRegistry.ListConnectionsToolName, AgentConfirmationCategories.MongoMetadataRead, "{}", null),
                CancellationToken.None);
            chat.Items.Add(copilotMetadataConfirmation);
            Assert.That(copilotMetadataConfirmation.CanApproveThisSession, Is.False,
                "Copilot registry approvals are one-call only, including metadata reads.");
            Assert.Multiple(() =>
            {
                Assert.That(copilotMetadataConfirmation.Title, Does.StartWith("O agente solicita:"),
                    "A confirmação do Copilot deve identificar o agente atual, sem atribuir a solicitação ao Claude.");
                Assert.That(copilotMetadataConfirmation.InputText, Is.EqualTo("{}"),
                    "A confirmação mostra os argumentos exatos do dispatch que será autorizado.");
                Assert.That(copilotMetadataConfirmation.ApproveOnceCommand.CanExecute(null), Is.True);
                Assert.That(copilotMetadataConfirmation.RejectCommand.CanExecute(null), Is.True);
            });
            foreach (var theme in Themes)
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                await PumpAsync(() => true);
                Save(window, $"agent-cli-chat-native-tools-{theme}.png");
            }

            // Cancel after the request reached the provider: uncertain outcome, no rollback, next message resumes.
            await chat.CancelTurnCommand.ExecuteAsync(null);
            runtime.Push(turn, AgentEventKind.ToolFailed, call: find, tool: "mongo_find", status: AgentToolResultStatus.Cancelled,
                origin: AgentToolOrigin.Registry);
            runtime.Push(turn, AgentEventKind.AgentError, errorCode: "CancelledAfterSend");
            runtime.Push(turn, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.OutcomeUnknown);
            await PumpAsync(() => chat.State == AgentChatState.OutcomeUnknown);
            Assert.That(chat.StatusText, Does.Contain("Resultado incerto").And.Contain("já foi enviado").And.Contain("retoma a sessão"));
            foreach (var theme in Themes)
                foreach (var (width, height, scale) in ChatSizes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    window.Width = width;
                    window.Height = height;
                    window.SetRenderScaling(scale);
                    await PumpAsync(() => true);
                    Save(window, $"agent-cli-chat-cancelled-after-send-{theme}-{width}-{scale.ToString(CultureInfo.InvariantCulture)}.png");
                }

            window.SetRenderScaling(1);
            window.Close();
            await chat.DisposeAsync();
        });
    }

    private static string SyntheticWorkspace => OperatingSystem.IsWindows()
        ? @"C:\Projetos\workspace-sintetico\consultas"
        : "/home/teste/workspace-sintetico/consultas";

    private static async Task<(AgentSettingsWindow Window, AgentSettingsViewModel Settings, FakeCliAccountManager Accounts, Task? Pending)>
        BuildSettingsAsync(string state, double width, double height, AgentCliProviderProfile? profile = null, bool copilot = false)
    {
        var accounts = new FakeCliAccountManager
        {
            WorkspaceDirectory = state == "subscription" ? SyntheticWorkspace : null,
            Profile = profile ?? FakeCliAccountManager.TestProfile,
        };
        var reason = state switch
        {
            "not-checked" => "StatusNotReported",
            "not-found" => "ExecutableNotFound",
            "unsupported" => "UnsupportedExecutable",
            "version-low" => "VersionTooLow",
            "blocked" => "BlockedEnvironment",
            _ => "NotLoggedIn",
        };
        var auth = state switch
        {
            "subscription" or "api-key" or "api-key-helper" or "environment-token" or "cloud-provider" or "native-unclassified" => AgentProviderAuthState.Configured,
            "blocked" => AgentProviderAuthState.Invalid,
            "signed-out" or "waiting" or "no-terminal" => AgentProviderAuthState.NotConfigured,
            _ => AgentProviderAuthState.Unknown,
        };
        var subscriptionProvider = copilot
            ? new AgentProviderPresentation(FakeCliAccountManager.ProviderId, "GitHub Copilot", AgentDataDestinationKind.External,
                false, state == "subscription" ? ["gpt-4.1", "claude-sonnet-4"] : [],
                [AgentAuthenticationMethod.OfficialCliDelegated], auth,
                UnavailableReason: state == "subscription" ? "CopilotSessionNotHomologated" : reason,
                FamilyName: "GitHub Copilot")
            : MutableAgentCatalog.Subscription(state is "subscription" or "api-key" or "api-key-helper" or "environment-token" or "cloud-provider" or "native-unclassified", auth, reason);
        var catalog = new MutableAgentCatalog(subscriptionProvider);
        accounts.Status = state switch
        {
            "not-found" => new AgentAccountStatus(AgentAccountInstallState.NotFound, null, AgentAccountAuthState.NotChecked),
            "unsupported" => new AgentAccountStatus(AgentAccountInstallState.UnsupportedExecutable, null, AgentAccountAuthState.NotChecked),
            "version-low" => new AgentAccountStatus(AgentAccountInstallState.VersionTooLow, "2.0.14", AgentAccountAuthState.NotChecked,
                ExecutablePath: FakeCliAccountManager.FakeExecutablePath),
            "subscription" when copilot => new AgentAccountStatus(AgentAccountInstallState.Installed, "1.0.85",
                AgentAccountAuthState.Subscription),
            "subscription" => FakeCliAccountManager.Subscription(),
            "api-key" => NativeAuth(AgentAccountAuthState.ApiKey),
            "api-key-helper" => NativeAuth(AgentAccountAuthState.ApiKeyHelper),
            "environment-token" => NativeAuth(AgentAccountAuthState.EnvironmentToken),
            "cloud-provider" => NativeAuth(AgentAccountAuthState.CloudProvider),
            "native-unclassified" => NativeAuth(AgentAccountAuthState.UnsupportedMethod),
            "blocked" => FakeCliAccountManager.BlockedByApiKey(),
            _ => FakeCliAccountManager.SignedOut(),
        };
        if (state == "no-terminal")
        {
            accounts.SignInResult = new AgentAccountCommandResult(AgentAccountCommandOutcome.NoVisibleTerminal, null);
        }

        var settings = new AgentSettingsViewModel(catalog, new RecordingCredentialSetup(), FakeCliAccountManager.ProviderId, accounts);
        var window = new AgentSettingsWindow { DataContext = settings, Width = width, Height = height };
        window.Show();
        await PumpAsync(() => true);
        Assert.That(accounts.Checks, Is.Zero, "Abrir as configurações não executa a CLI.");
        Task? pending = null;
        if (state != "not-checked")
        {
            await settings.TestConnectionCommand.ExecuteAsync(null);
        }

        if (state == "waiting")
        {
            accounts.SignInGate = new TaskCompletionSource();
            pending = settings.SignInCommand.ExecuteAsync(null);
            await PumpAsync(() => settings.IsWaitingForSignIn);
        }
        else if (state == "no-terminal")
        {
            await settings.SignInCommand.ExecuteAsync(null);
        }

        await PumpAsync(() => true);
        return (window, settings, accounts, pending);
    }

    private static AgentAccountStatus NativeAuth(AgentAccountAuthState auth) =>
        new(AgentAccountInstallState.Installed, "2.1.268", auth,
            BlockingSource: "credential-source-canary", ExecutablePath: FakeCliAccountManager.FakeExecutablePath);

    private static void AssertSettingsVisuals(AgentSettingsWindow window, AgentSettingsViewModel settings, string state)
    {
        var signIn = window.FindControl<Button>("SignInButton")!;
        Assert.Multiple(() =>
        {
            Assert.That(window.FindControl<StackPanel>("CliSection")!.IsVisible, Is.True, state);
            Assert.That(signIn.Content, Is.EqualTo("Entrar pelo Claude Code…"));
            Assert.That(signIn.Bounds.Height, Is.GreaterThanOrEqualTo(28));
            Assert.That(window.FindControl<TextBlock>("CliInstallText")!.Text, Is.EqualTo(settings.CliInstallText));
            Assert.That(window.FindControl<Border>("SelectedModeChip")!.IsVisible, Is.True);
            Assert.That(window.FindControl<StackPanel>("ManualCommandPanel")!.IsVisible, Is.EqualTo(state == "no-terminal"));
            Assert.That(window.FindControl<Button>("StopWaitingButton")!.IsVisible, Is.EqualTo(state == "waiting"));
            Assert.That(window.FindControl<ProgressBar>("BusyIndicator")!.IsVisible, Is.EqualTo(state == "waiting"));
            Assert.That(window.FindControl<TextBlock>("UserRulesNotice")!.IsVisible, Is.EqualTo(state == "subscription"));
            Assert.That(window.FindControl<Border>("CliAuthNotice")!.IsVisible, Is.EqualTo(settings.HasCliAuthExplanation));
        });
    }

    private static void ScrollTo(Window window, bool top)
    {
        var scroll = window.GetVisualDescendants().OfType<ScrollViewer>().First();
        if (top)
        {
            scroll.ScrollToHome();
        }
        else
        {
            scroll.ScrollToEnd();
        }

        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task PumpAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        do
        {
            Dispatcher.UIThread.RunJobs();
            if (condition())
            {
                Dispatcher.UIThread.RunJobs();
                return;
            }

            await Task.Delay(10);
        }
        while (Environment.TickCount64 < deadline);

        Assert.Fail("UI condition not reached in time.");
    }

    private static void Save(Window window, string fileName)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var directory = UiEvidenceDirectory.Current();
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, fileName), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
