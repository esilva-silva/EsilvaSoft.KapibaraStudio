using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// P7-L06-HOST: the AI Agent panel hosted in the real <see cref="MainWindow"/>, rendered with Skia in light and dark
/// themes at 960 × 620, 1366 × 768 and 1920 × 1080 (plus 150/200% at 1366), and driven by the keyboard. Fixtures are
/// synthetic (channel runtime, fake catalogs, no credentials, test MongoDB proxy). This is not screen-reader, IME or
/// native-dialog homologation.
/// </summary>
[TestFixture, NonParallelizable, Category("Ui"), Category("Integration")]
public sealed class AgentChatHostUiTests
{
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    private static readonly Size[] Sizes = [new(960, 620), new(1366, 768), new(1920, 1080)];
    private static readonly string[] HostStates = ["unavailable", "not-checked", "conversation"];
    private static readonly double[] Scales = [1.5, 2];

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

    private sealed class Host : IAsyncDisposable
    {
        public required WorkspaceTestContext Context { get; init; }
        public required WorkspaceViewModel Model { get; init; }
        public required MainWindow Window { get; init; }
        public ChannelAgentRuntime? Runtime { get; init; }

        public async ValueTask DisposeAsync()
        {
            if (Model.ActiveAgentChat is { ActiveTurnId: { } turn } && Runtime is not null)
            {
                Runtime.Push(turn, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Cancelled);
                await PumpAsync(() => Model.ActiveAgentChat?.ActiveTurnId is null);
            }

            // The window's normal close flow saves the session and disposes the workspace (and every tab chat).
            Window.Close();
            await PumpAsync(() => !Window.IsVisible);
            Model.Dispose();
            Context.Dispose();
        }
    }

    /// <summary>A workspace with one Console tab on a real explorer path; the given services back the agent chat.</summary>
    private static async Task<Host> BuildAsync(Func<AgentChatServices>? services, ChannelAgentRuntime? runtime = null)
    {
        var context = new WorkspaceTestContext();
        var profile = ConnectionProfile.Create("Desenvolvimento · Loja", "mongodb://localhost:27017");
        await context.Repository.SaveAsync(profile);
        var vm = new WorkspaceViewModel(context.Workspace, context.Repository,
            agentChat: services is null ? null : new AgentChatServicesFactory(services));
        var window = new MainWindow { DataContext = vm, Width = 1366, Height = 768 };
        window.Show();
        await window.InitializationTask;
        await vm.OpenConnectionAsync(profile);
        var bank = vm.Roots[0].Children[0];
        await bank.LoadAsync();
        bank.IsExpanded = true;
        vm.OpenCollection(bank.Children[0]);
        vm.ActiveTab!.Text = "db.getCollection(\"clientes\").find({ ativo: true }).limit(100)";
        await PumpAsync(() => true);
        return new Host { Context = context, Model = vm, Window = window, Runtime = runtime };
    }

    private static AgentChatServices NotCheckedServices() =>
        new(new ChannelAgentRuntime(),
            new DesktopAgentProviderCatalog(new AgentProviderCatalog([new ScriptedAgentProvider("provider-externo")])),
            new FakeAgentContextProvider());

    [Test]
    public async Task HostedPanelRendersDockedOrAsItsOwnSurfaceInBothThemesAndThreeSizes()
    {
        await RunOnUiAsync(async () =>
        {
            foreach (var state in HostStates)
            {
                var runtime = new ChannelAgentRuntime();
                Func<AgentChatServices>? services = state switch
                {
                    "unavailable" => null,
                    "not-checked" => NotCheckedServices,
                    _ => () => new AgentChatServices(runtime,
                        new FakeAgentCatalog(FakeAgentCatalog.External("ext", "Provider externo de teste"),
                            FakeAgentCatalog.Local("local", "Local de teste")), new FakeAgentContextProvider())
                    { Permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default("ext") with { ExternalDestinationConsentAt = DateTimeOffset.UtcNow }) },
                };
                await using var host = await BuildAsync(services, runtime);
                host.Model.IsAgentPanelOpen = true;
                await PumpAsync(() => host.Model.ActiveAgentChat is not null);
                if (state == "conversation")
                {
                    await StartConversationAsync(host.Model.ActiveAgentChat!, runtime);
                }

                foreach (var theme in Themes)
                    foreach (var size in Sizes)
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        Resize(host.Window, size, 1);
                        AssertHostedLayout(host, size);
                        if (state == "not-checked" && size == Sizes[0])
                        {
                            var panel = host.Window.GetVisualDescendants().OfType<AgentChatPanel>().Single();
                            var retryButton = panel.FindControl<Button>("RetryAvailabilityButton");
                            var statusCard = retryButton?.FindAncestorOfType<Border>();
                            Assert.That(retryButton is not null && statusCard is not null, Is.True);
                            var button = retryButton ?? throw new AssertionException("Retry action was not found.");
                            var card = statusCard ?? throw new AssertionException("Status card was not found.");
                            var buttonBottom = button.TranslatePoint(new Point(0, button.Bounds.Height), host.Window)!.Value.Y;
                            var cardBottom = card.TranslatePoint(new Point(0, card.Bounds.Height), host.Window)!.Value.Y;
                            Assert.That(buttonBottom, Is.LessThanOrEqualTo(cardBottom + 1),
                                "The availability action must remain inside its status card at the narrow layout.");
                        }

                        Save(host.Window, $"agent-host-{state}-{theme}-{size.Width}x{size.Height}.png");
                    }

                if (state == "conversation")
                {
                    foreach (var theme in Themes)
                        foreach (var scale in Scales)
                        {
                            Avalonia.Application.Current!.RequestedThemeVariant = theme;
                            Resize(host.Window, new Size(1366, 768), scale);
                            AssertHostedLayout(host, new Size(1366, 768));
                            Save(host.Window, $"agent-host-{state}-{theme}-1366x768-{scale.ToString(CultureInfo.InvariantCulture)}.png");
                        }

                    host.Window.SetRenderScaling(1);
                }
            }
        });
    }

    [Test]
    public async Task ShortcutOpensAndClosesThePanelAndChatKeysNeverRunOrCancelTheTabQuery()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            await using var host = await BuildAsync(() => new AgentChatServices(runtime,
                new FakeAgentCatalog(FakeAgentCatalog.Local("local", "Local de teste")), new FakeAgentContextProvider()), runtime);
            var window = host.Window;
            var tab = host.Model.ActiveTab!;
            var mongoCalls = 0;
            var inner = host.Context.Mongo.Handler!;
            host.Context.Mongo.Handler = (name, arguments) => { mongoCalls++; return inner(name, arguments); };
            Resize(window, new Size(1366, 768), 1);
            FocusEditor(window);

            // Ctrl+Shift+A: opens the panel for the active tab and moves focus to its composer.
            window.KeyPress(Key.A, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.A, "A");
            await PumpAsync(() => window.AgentChatPanel.ComposerBox.IsFocused);
            Assert.Multiple(() =>
            {
                Assert.That(host.Model.IsAgentPanelOpen, Is.True);
                Assert.That(window.AgentChatPanel.DataContext, Is.SameAs(host.Model.ActiveAgentChat));
                Assert.That(window.IsAgentPanelOverlay, Is.False);
            });

            // Ctrl+Enter sends directly to the agent; it never executes the tab's MongoDB script.
            window.AgentChatPanel.ComposerBox.Focus();
            window.KeyTextInput("explique a consulta");
            window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
            var chat = host.Model.ActiveAgentChat!;
            try { await PumpAsync(() => runtime.LastRequest is not null && chat.State == AgentChatState.Generating); }
            catch (Exception exception)
            {
                throw new AssertionException($"{exception.Message} State={chat.State}; busy={chat.IsBusy}; activeTurn={chat.ActiveTurnId}; detail={chat.StatusDetail}; block={chat.SendBlock}; provider={chat.SelectedProvider?.ProviderId}; available={chat.SelectedProvider?.Presentation.IsAvailable}; auth={chat.SelectedProvider?.Presentation.AuthState}; permissions={chat.CurrentPermissions?.IsWellFormed}; lastRequest={runtime.LastRequest is not null}; yielded={runtime.YieldedEvents.Count}; composer={chat.ComposerText}", exception);
            }
            window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
            await PumpAsync(() => true);
            Assert.Multiple(() =>
            {
                Assert.That(tab.IsRunning, Is.False, "F5/Ctrl+Enter typed in the panel do not run the query.");
                Assert.That(host.Context.Scripts.Calls, Is.Empty);
                Assert.That(mongoCalls, Is.Zero, "No MongoDB call was issued by chat keys.");
                Assert.That(tab.Errors, Is.Empty);
            });

            // Escape does not cancel or roll back the active agent turn.
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await PumpAsync(() => true);
            Assert.That(chat.State, Is.EqualTo(AgentChatState.Generating));
            var turn = runtime.LastRequest!.TurnId;
            var message = AgentMessageId.New();
            runtime.Push(turn, AgentEventKind.MessageStarted, message: message);
            runtime.Push(turn, AgentEventKind.MessageDelta, "Consulta analisada.", message: message);
            runtime.Push(turn, AgentEventKind.MessageCompleted, message: message);
            runtime.Push(turn, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
            await PumpAsync(() => chat.State == AgentChatState.Completed);
            window.KeyTextInput("rascunho preservado");

            // From inside the panel the shortcut collapses it and focus returns to the editor.
            window.KeyPress(Key.A, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.A, "A");
            await PumpAsync(() => window.FocusManager!.GetFocusedElement() is AvaloniaEdit.Editing.TextArea);
            Assert.Multiple(() =>
            {
                Assert.That(host.Model.IsAgentPanelOpen, Is.False);
                Assert.That(chat.ComposerText, Is.EqualTo("rascunho preservado"), "Collapsing keeps the draft.");
            });

            // Panel open but focus in the editor: the shortcut moves focus to the chat instead of closing it.
            host.Model.IsAgentPanelOpen = true;
            await PumpAsync(() => window.AgentChatPanel.ComposerBox.IsFocused);
            FocusEditor(window);
            window.KeyPress(Key.A, RawInputModifiers.Control | RawInputModifiers.Shift, PhysicalKey.A, "A");
            await PumpAsync(() => window.AgentChatPanel.ComposerBox.IsFocused);
            Assert.That(host.Model.IsAgentPanelOpen, Is.True);

            // Control: the same F5 from the editor does reach the tab (the guard above is scoped to the panel).
            FocusEditor(window);
            window.KeyPress(Key.F5, RawInputModifiers.None, PhysicalKey.F5, null);
            await PumpAsync(() => tab.IsRunning || mongoCalls > 0 || tab.Errors.Length > 0);
            if (tab.IsRunning)
            {
                tab.CancelCommand.Execute(null);
                await PumpAsync(() => !tab.IsRunning);
            }
        });
    }

    [Test]
    public async Task AtTheMinimumWindowThePanelReplacesTheTabsAndReturnsToTheEditor()
    {
        await RunOnUiAsync(async () =>
        {
            await using var host = await BuildAsync(NotCheckedServices);
            var window = host.Window;
            Resize(window, new Size(960, 620), 1);
            host.Model.IsAgentPanelOpen = true;
            await PumpAsync(() => window.IsAgentPanelOverlay);
            var tabs = window.FindControl<Grid>("TabsArea")!;
            var close = window.AgentChatPanel.ClosePanel;
            Assert.Multiple(() =>
            {
                Assert.That(tabs.IsVisible, Is.False, "The editor is not squeezed under its minimum; the panel takes its place.");
                Assert.That(close.IsVisible, Is.True);
                Assert.That(Avalonia.Automation.AutomationProperties.GetName(close), Does.Contain("Voltar ao editor"));
                Assert.That(close.Content, Is.EqualTo("Voltar ao editor"), "The way back is visible text, not only an icon.");
                Assert.That(window.FindControl<Border>("ConnectionsPanel")!.IsVisible, Is.True, "The explorer stays.");
            });

            // Growing the window docks the same panel next to the tabs.
            Resize(window, new Size(1366, 768), 1);
            Assert.Multiple(() =>
            {
                Assert.That(window.IsAgentPanelOverlay, Is.False);
                Assert.That(tabs.IsVisible, Is.True);
                Assert.That(Avalonia.Automation.AutomationProperties.GetName(close), Does.Contain("Recolher"));
            });

            Resize(window, new Size(960, 620), 1);
            close.Focus();
            close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await PumpAsync(() => window.FocusManager!.GetFocusedElement() is AvaloniaEdit.Editing.TextArea);
            Assert.Multiple(() =>
            {
                Assert.That(host.Model.IsAgentPanelOpen, Is.False);
                Assert.That(tabs.IsVisible, Is.True);
            });
        });
    }

    private static async Task StartConversationAsync(AgentChatViewModel chat, ChannelAgentRuntime runtime)
    {
        chat.ComposerText = "Quais índices ajudariam a consulta de clientes ativos?";
        _ = chat.SendCommand.ExecuteAsync(null);
        await PumpAsync(() => chat.ActiveTurnId is not null && chat.State == AgentChatState.Generating);
        await PumpAsync(() => runtime.LastRequest is not null);
        var turn = runtime.LastRequest!.TurnId;
        var message = AgentMessageId.New();
        runtime.Push(turn, AgentEventKind.MessageStarted, message: message);
        runtime.Push(turn, AgentEventKind.MessageDelta, "Um índice em { ativo: 1 } atende o filtro. Nenhuma consulta foi executada; ",
            message: message);
        runtime.Push(turn, AgentEventKind.MessageDelta, "revise o plano com explain antes de criar o índice.", message: message);
        runtime.Push(turn, AgentEventKind.MessageCompleted, message: message);
        runtime.Push(turn, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Completed);
        try { await PumpAsync(() => chat.State == AgentChatState.Completed); }
        catch (Exception exception)
        {
            var events = string.Join(",", runtime.YieldedEvents.Select(e => $"{e.Kind}:{e.Outcome}:{e.TurnId}:{e.SessionId}"));
            throw new AssertionException($"{exception.Message} State={chat.State}; conversationTurnState={chat.ActiveConversation.TurnState}; runtimeRequestTurn={runtime.LastRequest?.TurnId}; pushedTurn={turn}; activeTurn={chat.ActiveTurnId}; session={runtime.SessionId}; events={events}", exception);
        }
        chat.ComposerText = "E para ordenar por nome?";
    }

    private static void AssertHostedLayout(Host host, Size size)
    {
        var window = host.Window;
        var panelHost = window.FindControl<Border>("AgentPanelHost")!;
        var tabs = window.FindControl<Grid>("TabsArea")!;
        var panel = window.AgentChatPanel;
        Assert.That(panelHost.IsVisible, Is.True, $"{size}");
        var origin = panelHost.TranslatePoint(new Point(), window)!.Value;
        // The headless compositor rounds client dimensions to physical pixels at fractional scales; allow half a
        // DIP plus half a device pixel on the right edge while still catching any visible layout spill.
        var edgeTolerance = 0.5 + 0.5 / window.RenderScaling;
        Assert.Multiple(() =>
        {
            Assert.That(origin.X + panelHost.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width + edgeTolerance), $"{size}");
            Assert.That(panelHost.Bounds.Width, Is.GreaterThanOrEqualTo(MainWindow.AgentPanelMinWidth - 0.5), $"{size}");
            foreach (var control in new Control[] { panel.ComposerBox, panel.FindControl<Button>("ConfigureButton")!, panel.ClosePanel })
            {
                var point = control.TranslatePoint(new Point(), window)!.Value;
                Assert.That(point.Y + control.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height + 0.5), $"{control.Name} {size}");
                Assert.That(point.X + control.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width + 0.5), $"{control.Name} {size}");
            }
        });

        if (window.IsAgentPanelOverlay)
        {
            Assert.That(tabs.IsVisible, Is.False, $"{size}");
            return;
        }

        var editor = window.GetVisualDescendants().OfType<MongoTextEditor>().First(e => e.Name == "CodeEditor");
        Assert.Multiple(() =>
        {
            Assert.That(tabs.IsVisible, Is.True, $"{size}");
            Assert.That(tabs.Bounds.Width, Is.GreaterThanOrEqualTo(499.5), $"tabs keep their minimum at {size}");
            Assert.That(panelHost.Bounds.Width, Is.LessThanOrEqualTo(MainWindow.AgentPanelMaxWidth + 0.5), $"{size}");
            Assert.That(editor.Bounds.Width, Is.GreaterThan(400), $"{size}");
            Assert.That(tabs.Bounds.Right, Is.LessThanOrEqualTo(panelHost.Bounds.Left + 0.5), $"{size}");
        });
    }

    private static void FocusEditor(Window window)
    {
        window.GetVisualDescendants().OfType<MongoTextEditor>().First(e => e.Name == "CodeEditor").Focus();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Resize(Window window, Size size, double scale)
    {
        window.Width = size.Width;
        window.Height = size.Height;
        window.SetRenderScaling(scale);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
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

        Assert.Fail("Condition not reached in time.");
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
