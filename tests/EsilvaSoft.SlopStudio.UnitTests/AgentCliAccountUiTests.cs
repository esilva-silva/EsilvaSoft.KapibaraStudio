using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Desktop;
using EsilvaSoft.SlopStudio.Desktop.Agents;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.UnitTests;

/// <summary>
/// P7-CL5-02: settings of the CLI-delegated account in every state, the global sign-out confirmation and the mode chip
/// and read notice in the hosted chat, rendered with Skia in light and dark themes (settings 660 × 560 and 520 × 420 at
/// 200%; main window 960 × 620 and 1366 × 768, plus 1366 at 200%). Synthetic fixtures: scripted account manager (no
/// process, no real CLI, no account), fake catalog and channel runtime. Not screen-reader or native-dialog homologation.
/// </summary>
[TestFixture, NonParallelizable, Category("Ui")]
public sealed class AgentCliAccountUiTests
{
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    private static readonly string[] SettingsStates =
        ["not-checked", "not-found", "unsupported", "version-low", "signed-out", "subscription", "blocked", "waiting", "no-terminal"];

    private static readonly (double Width, double Height, double Scale)[] ChatSizes = [(400, 720, 1), (480, 768, 2)];
    private static readonly (Size Size, double Scale)[] HostSizes = [(new Size(960, 620), 1), (new Size(1366, 768), 1), (new Size(1366, 768), 2)];
    private static readonly string[] NarrowStates = ["subscription", "blocked"];
    private static readonly string[] HostModes = ["subscription-folder", "subscription-no-folder", "subscription-refused", "subscription-blocked", "api"];

    private static Task<bool> RunOnUiAsync(Func<Task> body)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiTestApp).Assembly);
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
                var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription(), MutableAgentCatalog.Api());
                var context = new WorkspaceTestContext();
                var profile = ConnectionProfile.Create("Desenvolvimento · Loja", "mongodb://localhost:27017");
                await context.Repository.SaveAsync(profile);
                var vm = new WorkspaceViewModel(context.Workspace, context.Repository,
                    agentChat: new AgentChatServicesFactory(() =>
                        new AgentChatServices(runtime, catalog, new FakeAgentContextProvider(), CliAccounts: accounts)));
                var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
                window.Show();
                await window.InitializationTask;
                // The chat reads the Files panel folder from the workspace (the value a new session would capture).
                vm.WorkspaceRootPath = mode is "subscription-folder" or "subscription-refused" or "subscription-blocked" ? SyntheticWorkspace : null;
                vm.IsAgentPanelOpen = true;
                await PumpAsync(() => vm.ActiveAgentChat is not null);
                var chat = vm.ActiveAgentChat!;
                if (mode == "api")
                {
                    chat.SelectedProvider = chat.Providers.Single(p => p.ProviderId == "api-key");
                }

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
                        var badge = panel.FindControl<TextBlock>("ModeBadge")!;
                        var destination = panel.FindControl<TextBlock>("DestinationBadge")!;
                        Assert.Multiple(() =>
                        {
                            Assert.That(badge.IsEffectivelyVisible, Is.True, mode);
                            Assert.That(badge.Text, Is.EqualTo(mode == "api" ? "Claude · API" : "Claude · assinatura"));
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
            var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription(), MutableAgentCatalog.Api());
            var tab = new AgentChatTabFixture { WorkspaceFolder = SyntheticWorkspace };
            var chat = new AgentChatViewModel(new AgentChatServices(runtime, catalog, new FakeAgentContextProvider(), CliAccounts: accounts)
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
        BuildSettingsAsync(string state, double width, double height)
    {
        var accounts = new FakeCliAccountManager { WorkspaceDirectory = state == "subscription" ? SyntheticWorkspace : null };
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
            "subscription" => AgentProviderAuthState.Configured,
            "blocked" => AgentProviderAuthState.Invalid,
            "signed-out" or "waiting" or "no-terminal" => AgentProviderAuthState.NotConfigured,
            _ => AgentProviderAuthState.Unknown,
        };
        var catalog = new MutableAgentCatalog(MutableAgentCatalog.Subscription(state == "subscription", auth, reason), MutableAgentCatalog.Api());
        accounts.Status = state switch
        {
            "not-found" => new AgentCliAccountStatus(AgentCliInstallState.NotFound, null, AgentCliAuthState.NotChecked),
            "unsupported" => new AgentCliAccountStatus(AgentCliInstallState.UnsupportedExecutable, null, AgentCliAuthState.NotChecked),
            "version-low" => new AgentCliAccountStatus(AgentCliInstallState.VersionTooLow, "2.0.14", AgentCliAuthState.NotChecked,
                ExecutablePath: FakeCliAccountManager.FakeExecutablePath),
            "subscription" => FakeCliAccountManager.Subscription(),
            "blocked" => FakeCliAccountManager.BlockedByApiKey(),
            _ => FakeCliAccountManager.SignedOut(),
        };
        if (state == "no-terminal")
        {
            accounts.SignInResult = new AgentCliCommandResult(AgentCliCommandOutcome.NoVisibleTerminal, null);
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
            Assert.That(window.FindControl<Border>("CliBlockedNotice")!.IsVisible, Is.EqualTo(state is "blocked" or "signed-out" or "no-terminal" or "waiting"));
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
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-evidence");
        Directory.CreateDirectory(directory);
        using var frame = window.CaptureRenderedFrame();
        frame!.Save(Path.Combine(directory, fileName), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
    }
}
