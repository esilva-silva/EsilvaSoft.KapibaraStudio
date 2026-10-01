using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Interactivity;
using Avalonia.Controls.Primitives;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Desktop;
using EsilvaSoft.KapibaraStudio.Desktop.Agents;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

/// <summary>
/// Real Avalonia controls rendered with Skia for the native agent chat, approval and settings surfaces, in light and
/// dark themes. Fixtures are synthetic (channel runtime, no provider, no credentials, no MongoDB); they are not a
/// capture of a real provider session and do not replace screen-reader or native-dialog homologation.
/// </summary>
[TestFixture, NonParallelizable, Category("Ui"), Category("Integration")]
public sealed class AgentChatUiTests
{
    private static readonly ThemeVariant[] Themes = [ThemeVariant.Light, ThemeVariant.Dark];
    private static readonly string[] PanelLanguages = ["pt-BR", "en", "es", "zh-CN"];
    private static readonly int[] PanelWidths = [320, 380, 560];
    private static readonly double[] PanelScales = [1, 1.5, 2];
    private static readonly Key[] DialogKeys = [Key.Enter, Key.Escape];
    private static readonly AgentApprovalOutcome[] OnlyDenied = [AgentApprovalOutcome.Denied];
    private static readonly string[] ChatStates = ["unavailable", "ready", "streaming", "permission-denied", "confirmation-denied", "confirmation-expired", "outcome-unknown"];

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
    public async Task ProviderOpensOneBoundedListAndCanBeSelectedByKeyboard()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var tab = new AgentChatTabFixture();
            var catalog = new FakeAgentCatalog(FakeAgentCatalog.External("api", "OpenAI API"),
                FakeAgentCatalog.External("claude", "Claude (Anthropic API)"),
                FakeAgentCatalog.External("subscription", "Claude — assinatura via Claude Code"),
                FakeAgentCatalog.External("codex", "Codex — assinatura ChatGPT com nome longo para revisão"),
                FakeAgentCatalog.External("copilot", "GitHub Copilot"), FakeAgentCatalog.Local("local", "IA local"));
            var chat = new AgentChatViewModel(new AgentChatServices(runtime, catalog, new FakeAgentContextProvider()), tab.Capture);
            var panel = new AgentChatPanel { DataContext = chat };
            var window = new Window { Content = panel, Width = 340, Height = 820 };
            window.Show();
            await chat.Initialization;
            try
            {
                var selector = panel.FindControl<ComboBox>("ProviderSelector")!;
                foreach (var theme in Themes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    selector.Focus();
                    selector.IsDropDownOpen = true;
                    await PumpAsync(() => true);
                    var popup = selector.GetVisualDescendants().OfType<Popup>().Single();
                    Assert.That(popup.Child, Is.Not.Null);
                    Assert.That(popup.Child!.Bounds.Width, Is.LessThanOrEqualTo(320),
                        "Long provider names must not create the oversized popup from the user capture.");
                    Assert.That(popup.Child.GetVisualDescendants().OfType<ComboBox>(), Is.Empty,
                        "The provider list must not contain a second selector.");
                    var directory = UiEvidenceDirectory.Current();
                    Directory.CreateDirectory(directory);
                    using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(
                        PixelSize.FromSize(popup.Child.Bounds.Size, 1));
                    bitmap.Render(popup.Child);
                    bitmap.Save(Path.Combine(directory, $"agent-provider-list-{theme}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                    selector.IsDropDownOpen = false;
                    await PumpAsync(() => true);
                    Save(window, $"agent-provider-single-selector-{theme}.png");
                }
                selector.Focus();
                window.KeyPress(Key.Down, RawInputModifiers.None, PhysicalKey.ArrowDown, null);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                await PumpAsync(() => true);
                Assert.That(chat.SelectedProvider!.ProviderId, Is.EqualTo("claude"));
                Assert.That(runtime.LastRequest, Is.Null, "Choosing a provider does not send a turn.");
            }
            finally { await CloseAsync(window, chat, runtime); }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ConversationHasPriorityAndContextRemainsReviewableAcrossLanguagesAndScales(bool officialCli)
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var tab = new AgentChatTabFixture { Selection = "db.orders.find({ active: true }).limit(10)" };
            var accounts = new EsilvaSoft.KapibaraStudio.Testing.FakeCliAccountManager();
            var provider = officialCli ? EsilvaSoft.KapibaraStudio.Testing.MutableAgentCatalog.Subscription() with
                { SupportsTurnPlan = true } : FakeAgentCatalog.External("ext", "Provider externo com nome longo") with
                { SupportsTurnPlan = true };
            var permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default(provider.ProviderId) with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            });
            var services = new AgentChatServices(runtime, new FakeAgentCatalog(provider), new FakeAgentContextProvider(),
                AccountManager: accounts) { Permissions = permissions };
            var chat = new AgentChatViewModel(services, tab);
            var panel = new AgentChatPanel { DataContext = chat };
            var window = new Window { Content = panel, Width = 380, Height = 820 };
            window.Show();
            await chat.Initialization;
            await PumpAsync(() => chat.Chips.Count == 2);
            try
            {
                foreach (var language in PanelLanguages)
                    foreach (var theme in Themes)
                        foreach (var width in PanelWidths)
                            foreach (var scale in PanelScales)
                            {
                                LocalizationViewModel.Current.Language = language;
                                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                                window.Width = width;
                                window.SetRenderScaling(scale);
                                await PumpAsync(() => true);
                                var history = panel.FindControl<ListBox>("History")!;
                                if (width == 380 && scale == 1 && language == "pt-BR")
                                    Assert.That(history.Bounds.Height / (panel.Bounds.Height - 16), Is.GreaterThanOrEqualTo(.55),
                                        "The conversation must receive at least 55% of usable panel height.");
                                TestContext.WriteLine($"UX panel: CLI={officialCli}, {language}, {theme}, {width}@{scale}; history={history.Bounds.Height:F1}/{panel.Bounds.Height - 16:F1}");
                                foreach (var control in new Control[] { panel.ComposerBox,
                                    panel.FindControl<Button>("SendButton")!, panel.FindControl<Button>("AttachButton")!,
                                    panel.FindControl<ComboBox>("ModeSelector")!, panel.FindControl<ComboBox>("ModelSelector")! })
                                {
                                    var origin = control.TranslatePoint(new Point(), panel)!.Value;
                                    Assert.That(origin.X, Is.GreaterThanOrEqualTo(0));
                                    Assert.That(origin.Y + control.Bounds.Height, Is.LessThanOrEqualTo(panel.Bounds.Height + .5));
                                    Assert.That(origin.X + control.Bounds.Width, Is.LessThanOrEqualTo(panel.Bounds.Width + .5));
                                }
                                Save(window, $"agent-ux-context-{officialCli}-{language}-{theme}-{width}-{scale.ToString(CultureInfo.InvariantCulture)}.png");
                            }

                window.Width = 320;
                window.SetRenderScaling(1);
                LocalizationViewModel.Current.Language = "pt-BR";
                var details = panel.FindControl<Expander>("ContextDetails")!;
                details.IsExpanded = true;
                await PumpAsync(() => true);
                Assert.That(panel.FindControl<ItemsControl>("ContextChips")!.IsEffectivelyVisible, Is.True);
                Save(window, $"agent-ux-context-expanded-{officialCli}.png");
                var active = chat.Chips.Single(chip => chip.Kind == AgentAttachmentKind.ActiveFile);
                chat.RemoveChipCommand.Execute(active);
                await PumpAsync(() => chat.Chips.Count == 1);
                Assert.That(((TextBlock)details.Header!).Text, Is.EqualTo(chat.ContextSummary), "Removal updates the context summary.");
                Assert.That(runtime.LastRequest, Is.Null, "Reviewing or removing context must not send a message.");
                for (var index = 0; index < 12; index++)
                    chat.Chips.Add(new AgentContextChipViewModel(AgentAttachmentKind.WorkspaceFile,
                        $"consulta-com-nome-longo-{index}.json", null, $"consulta-{index}.json", isAutomatic: false));
                chat.OnWorkspaceContextChanged();
                chat.ComposerText = string.Join("\n", Enumerable.Repeat("Uma instrução longa com BSON, contexto e filtro.", 80));
                window.Height = 620;
                panel.FindControl<Expander>("ReadScopeDetails")!.IsExpanded = true;
                panel.ComposerBox.Focus();
                await PumpAsync(() => true);
                var send = panel.FindControl<Button>("SendButton")!;
                var sendOrigin = send.TranslatePoint(new Point(), panel)!.Value;
                Assert.That(sendOrigin.Y + send.Bounds.Height, Is.LessThanOrEqualTo(panel.Bounds.Height + .5));
                Assert.That(panel.FindControl<ListBox>("History")!.Bounds.Height, Is.GreaterThanOrEqualTo(96));
                Assert.That(panel.FindControl<ScrollViewer>("ComposerDetailsScroll")!.Extent.Height,
                    Is.GreaterThan(panel.FindControl<ScrollViewer>("ComposerDetailsScroll")!.Viewport.Height),
                    "Large context and prompt use local scroll, preserving the send action and conversation.");
                Save(window, $"agent-ux-context-overflow-{officialCli}.png");
            }
            finally
            {
                LocalizationViewModel.Current.Language = "pt-BR";
                window.SetRenderScaling(1);
                await CloseAsync(window, chat, runtime);
            }
        });
    }

    [Test]
    public async Task StreamingCodePreservesReaderPositionAndExplicitReturnToLatest()
    {
        await RunOnUiAsync(async () =>
        {
            var (window, chat, runtime) = await BuildAsync("ready", 380, 820);
            var panel = (AgentChatPanel)window.Content!;
            try
            {
                _ = chat.SendCommand.ExecuteAsync(null);
                await PumpAsync(() => runtime.LastRequest is not null);
                var turn = runtime.LastRequest!.TurnId;
                Assert.That(chat.ShowStatusLine, Is.True, "Progress is visible before a response starts.");
                var message = AgentMessageId.New();
                runtime.Push(turn, AgentEventKind.MessageStarted, message: message);
                runtime.Push(turn, AgentEventKind.MessageDelta,
                    "Código para revisão, sem executar:\n```javascript\n" +
                    "db.getCollection(\"clientes\").find({ ativo: true, nome: \"um valor suficientemente longo para exigir rolagem local\" }).limit(10)\n" +
                    "```\n" + string.Join("\n", Enumerable.Range(0, 80).Select(index => $"Explicação da consulta, linha {index}.")), message: message);
                await PumpAsync(() => chat.Items.OfType<AgentChatMessageItem>().Any(item => item.Blocks.Count == 3));
                Assert.That(chat.ShowStatusLine, Is.False, "The active reply carries progress instead of a duplicated status card.");
                var scroll = panel.FindControl<ListBox>("History")!.GetVisualDescendants().OfType<ScrollViewer>().First();
                await PumpAsync(() => scroll.Extent.Height > scroll.Viewport.Height);
                scroll.Offset = new Vector(0, 0);
                panel.ComposerBox.Focus();
                await PumpAsync(() => panel.FindControl<Button>("LatestMessageButton")!.IsVisible);
                runtime.Push(turn, AgentEventKind.MessageDelta, "\nNova parte do stream.", message: message);
                await PumpAsync(() => chat.Items.OfType<AgentChatMessageItem>().Last().Content.EndsWith("Nova parte do stream.", StringComparison.Ordinal));
                Assert.That(scroll.Offset.Y, Is.LessThanOrEqualTo(8), "Streaming does not drag a reader away from the beginning.");
                Assert.That(window.FocusManager!.GetFocusedElement(), Is.SameAs(panel.ComposerBox));
                foreach (var theme in Themes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    await PumpAsync(() => true);
                    Save(window, $"agent-ux-code-reading-{theme}.png");
                }
                panel.FindControl<Button>("LatestMessageButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await PumpAsync(() => !panel.FindControl<Button>("LatestMessageButton")!.IsVisible);
                Assert.That(scroll.Offset.Y + scroll.Viewport.Height, Is.GreaterThanOrEqualTo(scroll.Extent.Height - 8));
                Assert.That(window.FocusManager!.GetFocusedElement(), Is.SameAs(panel.ComposerBox));
            }
            finally { await CloseAsync(window, chat, runtime); }
        });
    }

    [Test]
    public async Task ResumeNoticeKeepsTheOriginatingProviderAndConversation()
    {
        await RunOnUiAsync(async () =>
        {
            var (window, chat, runtime) = await BuildAsync("ready", 380, 820);
            try
            {
                _ = chat.SendCommand.ExecuteAsync(null);
                await PumpAsync(() => runtime.LastRequest is not null);
                var original = chat.ActiveConversation;
                var options = runtime.SessionOptions.Single();
                chat.NewConversationCommand.Execute(null);
                await PumpAsync(() => !ReferenceEquals(chat.ActiveConversation, original));
                options.ProviderSessionObserver!(new AgentProviderSessionUpdate(options.ConversationId,
                    AgentProviderSessionChange.ResumeFallback, null));
                await PumpAsync(() => original.Items.OfType<AgentChatNoticeItem>().Any());
                var notice = original.Items.OfType<AgentChatNoticeItem>().Last();
                Assert.That(notice.Content, Does.Contain("Provider externo de teste").And.Not.Contains("Claude"));
                Assert.That(chat.Items.OfType<AgentChatNoticeItem>(), Is.Empty, "An event cannot leak into the newly visible conversation.");
                runtime.Push(runtime.LastRequest!.TurnId, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Cancelled);
                await PumpAsync(() => !original.IsBusy);
            }
            finally { await CloseAsync(window, chat, runtime); }
        });
    }

    [Test]
    public async Task PersistenceFailureStaysVisibleDuringStreamingAndDoesNotLeakIntoAnotherConversation()
    {
        await RunOnUiAsync(async () =>
        {
            var (window, chat, runtime) = await BuildAsync("ready", 380, 820);
            var panel = (AgentChatPanel)window.Content!;
            try
            {
                _ = chat.SendCommand.ExecuteAsync(null);
                await PumpAsync(() => runtime.LastRequest is not null);
                var turn = runtime.LastRequest!.TurnId;
                var message = AgentMessageId.New();
                runtime.Push(turn, AgentEventKind.MessageStarted, message: message);
                runtime.Push(turn, AgentEventKind.MessageDelta, "Resposta ainda em andamento.", message: message);
                await PumpAsync(() => !chat.ShowStatusLine);
                var original = chat.ActiveConversation;
                original.PersistenceText = "Falha ao guardar histórico. A conversa continua em memória.";
                original.PersistenceIsError = true;
                await PumpAsync(() => panel.FindControl<TextBlock>("PersistenceNotice")!.IsEffectivelyVisible);
                Assert.That(panel.FindControl<TextBlock>("PersistenceNotice")!.Text, Is.EqualTo(original.PersistenceText));
                Assert.That(chat.ShowStatusArea, Is.True, "Streaming cannot hide persistence failure.");
                foreach (var theme in Themes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    await PumpAsync(() => true);
                    Save(window, $"agent-ux-persistence-failure-{theme}.png");
                }
                chat.NewConversationCommand.Execute(null);
                await PumpAsync(() => !ReferenceEquals(chat.ActiveConversation, original));
                Assert.That(panel.FindControl<TextBlock>("PersistenceNotice")!.IsEffectivelyVisible, Is.False);
                Assert.That(original.PersistenceText, Is.Not.Empty, "Switching conversations does not erase the failed save.");
                runtime.Push(turn, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Cancelled);
                await PumpAsync(() => !original.IsBusy);
            }
            finally { await CloseAsync(window, chat, runtime); }
        });
    }

    [Test]
    public async Task ChatStatesRenderInLightAndDarkThemes()
    {
        await RunOnUiAsync(async () =>
        {
            foreach (var state in ChatStates)
            {
                var (window, chat, runtime) = await BuildAsync(state, 400, 720);
                foreach (var theme in Themes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    await PumpAsync(() => true);
                    AssertStateVisuals(window, chat, state);
                    Save(window, $"agent-chat-{state}-{theme}.png");
                    if (state == "streaming" && window.Content is AgentChatPanel { OpenApprovalWindow: { } approval })
                    {
                        Save(approval, $"agent-chat-streaming-approval-dialog-{theme}.png");
                    }
                    if (state == "permission-denied" && window.Content is AgentChatPanel { OpenPermissionsWindow: { } permissions })
                    {
                        Save(permissions, $"agent-chat-permission-denied-dialog-{theme}.png");
                    }
                }

                await CloseAsync(window, chat, runtime);
            }
        });
    }

    [Test]
    public async Task StreamingChatFitsNarrowAndScaledLayouts()
    {
        await RunOnUiAsync(async () =>
        {
            var (window, chat, runtime) = await BuildAsync("streaming", 360, 620);
            if (window.Content is AgentChatPanel { OpenApprovalWindow: { } approval })
            {
                approval.Close();
                await PumpAsync(() => ((AgentChatPanel)window.Content).OpenApprovalWindow is null);
            }

            foreach (var theme in Themes)
                foreach (var (width, height) in new[] { (360, 620), (480, 768) })
                    foreach (var scale in new[] { 1d, 1.5, 2 })
                    {
                        Avalonia.Application.Current!.RequestedThemeVariant = theme;
                        window.Width = width; window.Height = height; window.SetRenderScaling(scale);
                        await PumpAsync(() => true);
                        var panel = (AgentChatPanel)window.Content!;
                        var send = panel.FindControl<Button>("CancelTurnButton")!;
                        var origin = send.TranslatePoint(new Point(), window)!.Value;
                        Assert.That(origin.X + send.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width + 0.5), $"{width}@{scale}");
                        Assert.That(origin.Y + send.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height + 0.5), $"{width}@{scale}");
                        Assert.That(send.Bounds.Height, Is.GreaterThanOrEqualTo(28));
                        Save(window, $"agent-chat-streaming-{theme}-{width}-{scale.ToString(CultureInfo.InvariantCulture)}.png");
                    }

            window.SetRenderScaling(1);
            await CloseAsync(window, chat, runtime);
        });
    }

    [Test]
    public async Task KeyboardSendsDirectlyFromTheComposerAndStreamingDoesNotStealFocus()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var tab = new AgentChatTabFixture();
            var permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default("ext") with
            {
                ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
            });
            var services = new AgentChatServices(runtime,
                new FakeAgentCatalog(FakeAgentCatalog.External("ext", "Provider externo de teste")), new FakeAgentContextProvider())
            { Permissions = permissions };
            var chat = new AgentChatViewModel(services, tab.Capture);
            var panel = new AgentChatPanel { DataContext = chat };
            var window = new Window { Content = panel, Width = 400, Height = 720 };
            window.Show();
            await PumpAsync(() => true);

            panel.ComposerBox.Focus();
            window.KeyTextInput("linha 1");
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            window.KeyTextInput("linha 2");
            await PumpAsync(() => true);
            Assert.That(chat.ComposerText.Replace("\r", "", StringComparison.Ordinal), Is.EqualTo("linha 1\nlinha 2"), "Enter inserts a line.");
            window.KeyPress(Key.Enter, RawInputModifiers.Control, PhysicalKey.Enter, null);
            await PumpAsync(() => runtime.LastRequest is not null && chat.State == AgentChatState.Generating);
            Assert.That(window.FocusManager!.GetFocusedElement(), Is.SameAs(panel.ComposerBox));
            Assert.That(runtime.LastRequest!.UserMessage.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n'), Is.EqualTo("linha 1\nlinha 2"));
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            var turn = runtime.LastRequest!.TurnId;
            var message = AgentMessageId.New();
            runtime.Push(turn, AgentEventKind.MessageStarted, message: message);
            for (var i = 0; i < 40; i++)
            {
                runtime.Push(turn, AgentEventKind.MessageDelta, $"fragmento {i} do stream\n", message: message);
            }

            await PumpAsync(() => chat.Items.OfType<AgentChatMessageItem>().Any(item => item.Content.Contains("fragmento 39", StringComparison.Ordinal)));
            Assert.That(window.FocusManager!.GetFocusedElement(), Is.SameAs(panel.ComposerBox), "Streaming must not move focus.");
                Assert.That(chat.CancelTurnCommand.CanExecute(null), Is.True);

            // Keyboard reaches the explicit cancel action from the composer with Tab.
            window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            await PumpAsync(() => true);
            Assert.That(window.FocusManager!.GetFocusedElement(), Is.SameAs(panel.FindControl<Button>("CancelTurnButton")));
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, null);
            await PumpAsync(() => runtime.CancelCalls == 1);
            runtime.Push(turn, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Cancelled);
            await PumpAsync(() => chat.State == AgentChatState.Cancelled);
            await CloseAsync(window, chat, runtime);
        });
    }

    [Test]
    public async Task DesktopConfirmationPromptRoutesCopilotDecisionThroughInlineCard()
    {
        await RunOnUiAsync(async () =>
        {
            var runtime = new ChannelAgentRuntime();
            var prompt = new DesktopAgentToolConfirmationPrompt();
            var tab = new AgentChatTabFixture();
            var permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default(
                AgentProviderIds.GitHubCopilotSubscription) with { ExternalDestinationConsentAt = DateTimeOffset.UtcNow });
            var services = new AgentChatServices(runtime,
                new FakeAgentCatalog(FakeAgentCatalog.External(AgentProviderIds.GitHubCopilotSubscription,
                    "GitHub Copilot · assinatura")), new FakeAgentContextProvider())
            {
                Permissions = permissions,
                Confirmations = prompt,
            };
            var chat = new AgentChatViewModel(services, tab.Capture);
            var window = new Window { Content = new AgentChatPanel { DataContext = chat }, Width = 420, Height = 700 };
            window.Show();
            await chat.Initialization;
            await PumpAsync(() => true);

            var request = new AgentToolConfirmationRequest(chat.ActiveConversation.Id,
                AgentProviderIds.GitHubCopilotSubscription, AgentToolRegistry.ListConnectionsToolName,
                AgentConfirmationCategories.MongoMetadataRead, "{}", null);
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var pendingDecision = prompt.ConfirmAsync(request, deadline.Token);
            await PumpAsync(() => chat.Items.OfType<AgentToolConfirmationCardItem>().Any());
            var card = chat.Items.OfType<AgentToolConfirmationCardItem>().Single();
            Assert.Multiple(() =>
            {
                Assert.That(card.Title, Does.StartWith("O agente solicita:"));
                Assert.That(card.InputText, Is.EqualTo("{}"));
                Assert.That(card.CanApproveThisSession, Is.False,
                    "Copilot registry grants are always scoped to this single call.");
                Assert.That(card.ApproveOnceCommand.CanExecute(null), Is.True);
                Assert.That(card.RejectCommand.CanExecute(null), Is.True);
            });

            foreach (var theme in Themes)
            {
                Avalonia.Application.Current!.RequestedThemeVariant = theme;
                await PumpAsync(() => true);
                Save(window, $"agent-chat-confirmation-pending-{theme}.png");
            }

            card.ApproveOnceCommand.Execute(null);
            Assert.That(await pendingDecision, Is.EqualTo(AgentToolConfirmationDecision.ApprovedOnce));
            Assert.That(card.State, Is.EqualTo(AgentToolConfirmationState.ApprovedOnce));

            var deniedRequest = request with { ToolName = AgentToolRegistry.MongoFindToolName,
                Category = AgentConfirmationCategories.MongoDocumentRead };
            var deniedDecision = prompt.ConfirmAsync(deniedRequest, deadline.Token);
            await PumpAsync(() => chat.Items.OfType<AgentToolConfirmationCardItem>().Count() == 2);
            var deniedCard = chat.Items.OfType<AgentToolConfirmationCardItem>().Last();
            deniedCard.RejectCommand.Execute(null);
            Assert.That(await deniedDecision, Is.EqualTo(AgentToolConfirmationDecision.Rejected));
            Assert.That(deniedCard.State, Is.EqualTo(AgentToolConfirmationState.Rejected));

            var longArguments = "{\"filter\":{\"value\":\"" + new string('x', 2500) + "\"}}";
            var longRequest = request with { InputJson = longArguments };
            var longDecision = prompt.ConfirmAsync(longRequest, deadline.Token);
            await PumpAsync(() => chat.Items.OfType<AgentToolConfirmationCardItem>().Count() == 3);
            var longCard = chat.Items.OfType<AgentToolConfirmationCardItem>().Last();
            Assert.That(longCard.InputText, Is.EqualTo(longArguments),
                "The approval card must retain all arguments so the user can inspect the exact invocation.");
            longCard.RejectCommand.Execute(null);
            Assert.That(await longDecision, Is.EqualTo(AgentToolConfirmationDecision.Rejected));

            deadline.Cancel();
            window.Close();
            await chat.DisposeAsync();
        });
    }

    [Test]
    public async Task ApprovalDialogFocusesRejectAndEscapeRejectsWithoutApproving()
    {
        await RunOnUiAsync(async () =>
        {
            foreach (var key in DialogKeys)
            {
                var runtime = new ChannelAgentRuntime();
                var clock = new AgentChatClock(DateTimeOffset.UtcNow);
                var approval = new AgentApprovalViewModel(runtime, runtime.SessionId, AgentTurnId.New(), AgentApprovalId.New(),
                    new FakeApprovalDetailsSource(Details(clock.Now.AddSeconds(95), AgentToolRisk.Write)), clock);
                await approval.LoadAsync(CancellationToken.None);
                var window = new AgentApprovalWindow { DataContext = approval };
                var closed = false;
                window.Closed += (_, _) => closed = true;
                window.Show();
                await PumpAsync(() => true);
                Assert.That(window.FocusManager!.GetFocusedElement(), Is.SameAs(window.FindControl<Button>("RejectButton")));
                // Enter activates the focused safe action (Reject); Escape rejects explicitly. Neither approves.
                window.KeyPress(key, RawInputModifiers.None, key == Key.Enter ? PhysicalKey.Enter : PhysicalKey.Escape, null);
                await PumpAsync(() => closed);
                Assert.That(runtime.Decisions.Select(d => d.Outcome), Is.EqualTo(OnlyDenied), key.ToString());
                Assert.That(approval.Phase, Is.EqualTo(AgentApprovalPhase.Rejected));
            }
        });
    }

    [Test]
    public async Task ApprovalDialogStatesRenderInLightAndDarkThemes()
    {
        await RunOnUiAsync(async () =>
        {
            foreach (var (name, width, height) in new[]
                     {
                         ("pending", 760, 560), ("pending", 600, 420), ("destructive", 760, 560), ("destructive", 600, 420),
                         ("expired", 760, 560), ("missing", 760, 560),
                     })
            {
                var runtime = new ChannelAgentRuntime();
                var clock = new AgentChatClock(DateTimeOffset.UtcNow);
                var risk = name == "destructive" ? AgentToolRisk.Destructive : AgentToolRisk.Write;
                var details = name == "missing" ? null : Details(clock.Now.AddSeconds(95), risk);
                var approval = new AgentApprovalViewModel(runtime, runtime.SessionId, AgentTurnId.New(), AgentApprovalId.New(),
                    new FakeApprovalDetailsSource(details), clock);
                await approval.LoadAsync(CancellationToken.None);
                if (name == "destructive") approval.DestructiveConfirmation = "ord";
                if (name == "expired") { clock.Now = clock.Now.AddSeconds(96); approval.Tick(); }
                var window = new AgentApprovalWindow { DataContext = approval, Width = width, Height = height };
                window.Show();
                foreach (var theme in Themes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    await PumpAsync(() => true);
                    var approve = window.FindControl<Button>("ApproveButton")!;
                    Assert.That(approve.IsEffectivelyEnabled, Is.EqualTo(name == "pending"), name);
                    var origin = approve.TranslatePoint(new Point(), window)!.Value;
                    Assert.That(origin.Y + approve.Bounds.Height, Is.LessThanOrEqualTo(window.ClientSize.Height + 0.5));
                    Assert.That(origin.X + approve.Bounds.Width, Is.LessThanOrEqualTo(window.ClientSize.Width + 0.5));
                    Save(window, $"agent-approval-{name}-{theme}-{width}.png");
                }

                // Close without triggering the reject-on-close path twice for terminal states.
                window.Close();
                await PumpAsync(() => true);
            }
        });
    }

    [Test]
    public async Task SettingsRenderInLightAndDarkThemesWithoutAuthenticatingOnOpen()
    {
        await RunOnUiAsync(async () =>
        {
            var credentials = new RecordingCredentialSetup();
            foreach (var (name, catalog, width, height) in new (string, FakeAgentCatalog, int, int)[]
                     {
                         ("providers", new FakeAgentCatalog(
                             FakeAgentCatalog.External("ext", "Provider externo de teste", AgentProviderAuthState.NotConfigured),
                             FakeAgentCatalog.Local("local", "Local de teste"),
                             new AgentProviderPresentation("off", "Provider desabilitado", AgentDataDestinationKind.External, false, [],
                                 [AgentAuthenticationMethod.ApiKey], AgentProviderAuthState.VaultUnavailable,
                                 UnavailableReason: "cofre do sistema indisponível")), 660, 560),
                         ("providers", new FakeAgentCatalog(
                             FakeAgentCatalog.External("ext", "Provider externo de teste", AgentProviderAuthState.NotConfigured)), 520, 420),
                         ("empty", new FakeAgentCatalog(), 660, 560),
                     })
            {
                var settings = new AgentSettingsViewModel(catalog, credentials, "ext");
                settings.ApiKey = "sk-test-CANARY";
                var window = new AgentSettingsWindow { DataContext = settings, Width = width, Height = height };
                window.Show();
                foreach (var theme in Themes)
                {
                    Avalonia.Application.Current!.RequestedThemeVariant = theme;
                    await PumpAsync(() => true);
                    if (name == "providers")
                    {
                        var input = window.FindControl<TextBox>("ApiKeyInput")!;
                        Assert.That(input.PasswordChar, Is.EqualTo('•'));
                    }

                    Save(window, $"agent-settings-{name}-{theme}-{width}.png");
                }

                window.Close();
                await PumpAsync(() => true);
                Assert.That(settings.ApiKey, Is.Empty, "The typed key does not outlive the window.");
            }

            Assert.That(credentials.Calls, Is.Zero, "Rendering and opening settings never touches the vault.");
        });
    }

    private static async Task<(Window Window, AgentChatViewModel Chat, ChannelAgentRuntime Runtime)> BuildAsync(
        string state, double width, double height)
    {
        var runtime = new ChannelAgentRuntime();
        var tab = new AgentChatTabFixture
        {
            ConnectionLabel = "Produção — cluster analítico com nome longo",
            Selection = "db.getCollection(\"orders\").find({ status: \"late\" }).limit(20)",
        };
        var catalog = new FakeAgentCatalog(FakeAgentCatalog.External("ext", "Provider externo de teste"),
            FakeAgentCatalog.Local("local", "Local de teste"));
        var details = new FakeApprovalDetailsSource(Details(DateTimeOffset.UtcNow.AddSeconds(110), AgentToolRisk.Write));
        var permissions = new FakeAgentPermissionsRepository(AgentProviderPermissions.Default("ext") with
        {
            ExternalDestinationConsentAt = DateTimeOffset.UtcNow,
        });
        var services = state == "unavailable"
            ? AgentChatServices.Unavailable
            : new AgentChatServices(runtime, catalog, new FakeAgentContextProvider(), details) { Permissions = permissions };
        var chat = new AgentChatViewModel(services, tab.Capture);
        var panel = new AgentChatPanel { DataContext = chat };
        var window = new Window { Content = panel, Width = width, Height = height };
        window.Show();
        await chat.Initialization;
        await PumpAsync(() => true);
        if (state == "unavailable")
        {
            return (window, chat, runtime);
        }

        chat.ComposerText = "Quais pedidos atrasados desta coleção precisam de revisão?";
        if (state == "ready")
        {
            return (window, chat, runtime);
        }

        _ = chat.SendCommand.ExecuteAsync(null);
        await PumpAsync(() => runtime.LastRequest is not null);
        var turn = runtime.LastRequest!.TurnId;
        var message = AgentMessageId.New();
        runtime.Push(turn, AgentEventKind.TaskStarted);
        runtime.Push(turn, AgentEventKind.MessageStarted, message: message);
        runtime.Push(turn, AgentEventKind.MessageDelta,
            "Vou consultar a coleção orders com filtro literal e limite de 20 documentos. Nenhuma escrita será feita sem sua aprovação.",
            message: message);
        runtime.Push(turn, AgentEventKind.MessageCompleted, message: message);
        var find = AgentToolCallId.New();
        runtime.Push(turn, AgentEventKind.ToolRequested, call: find, tool: "mongo_find");
        runtime.Push(turn, AgentEventKind.ToolStarted, call: find, tool: "mongo_find");
        runtime.Push(turn, AgentEventKind.ToolCompleted, call: find, tool: "mongo_find", status: AgentToolResultStatus.Succeeded);
        var denied = AgentToolCallId.New();
        var deniedTool = state is "permission-denied" or "confirmation-denied" or "confirmation-expired" ? "mongo_find" : "get_collection_schema";
        runtime.Push(turn, AgentEventKind.ToolRequested, call: denied, tool: deniedTool);
        runtime.Push(turn, AgentEventKind.ToolFailed, call: denied, tool: deniedTool,
            status: AgentToolResultStatus.Denied,
            errorCode: state switch
            {
                "confirmation-denied" => "ConfirmationRejected",
                "confirmation-expired" => "ConfirmationExpired",
                _ => "PermissionDenied"
            });
        if (state == "permission-denied")
        {
            await PumpAsync(() => panel.OpenPermissionsWindow is not null);
            return (window, chat, runtime);
        }
        if (state is "confirmation-denied" or "confirmation-expired")
        {
            await PumpAsync(() => chat.Items.OfType<AgentToolCallItem>().Last().State == AgentToolCallState.Denied);
            return (window, chat, runtime);
        }
        var second = AgentMessageId.New();
        runtime.Push(turn, AgentEventKind.MessageStarted, message: second);
        runtime.Push(turn, AgentEventKind.MessageDelta, "Encontrei 3 pedidos. Proponho marcar o primeiro como enviado.", message: second);
        if (state == "streaming")
        {
            runtime.Push(turn, AgentEventKind.ApprovalRequested, approval: AgentApprovalId.New());
            await PumpAsync(() => chat.State == AgentChatState.WaitingApproval &&
                ((AgentChatPanel)window.Content!).OpenApprovalWindow is not null);
            return (window, chat, runtime);
        }

        var uncertain = AgentToolCallId.New();
        runtime.Push(turn, AgentEventKind.ToolRequested, call: uncertain, tool: "mongo_find");
        runtime.Push(turn, AgentEventKind.ToolStarted, call: uncertain, tool: "mongo_find");
        runtime.Push(turn, AgentEventKind.ToolFailed, call: uncertain, tool: "mongo_find",
            status: AgentToolResultStatus.OutcomeUnknown, errorCode: "ToolOutcomeUnknown");
        runtime.Push(turn, AgentEventKind.AgentError, errorCode: "InterruptionUnconfirmed");
        runtime.Push(turn, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.OutcomeUnknown);
        await PumpAsync(() => chat.State == AgentChatState.OutcomeUnknown);
        return (window, chat, runtime);
    }

    private static void AssertStateVisuals(Window window, AgentChatViewModel chat, string state)
    {
        var panel = (AgentChatPanel)window.Content!;
        var status = panel.FindControl<TextBlock>("StatusLine")!;
        Assert.That(status.Text, Is.EqualTo(chat.StatusSummary));
        Assert.That(Avalonia.Automation.AutomationProperties.GetHelpText(status), Is.EqualTo(chat.StatusText),
            "The full state remains available to assistive technology.");
        switch (state)
        {
            case "unavailable":
                Assert.That(chat.State, Is.EqualTo(AgentChatState.Unavailable));
                Assert.That(panel.FindControl<Button>("SendButton")!.IsEffectivelyEnabled, Is.False);
                Assert.That(panel.FindControl<StackPanel>("EmptyState")!.IsVisible, Is.False, "No chat invitation when unavailable.");
                Assert.That(panel.ComposerBox.IsEffectivelyEnabled, Is.False);
                break;
            case "ready":
                Assert.That(panel.FindControl<TextBlock>("DestinationBadge")!.Text, Is.EqualTo("Externo"));
                break;
            case "streaming":
                Assert.That(chat.Items.OfType<AgentToolCallItem>().Select(t => t.State),
                    Is.EqualTo(new[] { AgentToolCallState.Succeeded, AgentToolCallState.Denied }));
                Assert.That(panel.FindControl<Button>("CancelTurnButton")!.IsVisible, Is.True);
                break;
            case "permission-denied":
                Assert.That(panel.OpenPermissionsWindow, Is.Not.Null,
                    "A denied product tool call opens the permissions window for review.");
                Assert.That(panel.OpenPermissionsWindow!.DataContext, Is.TypeOf<AgentPermissionsViewModel>());
                break;
            case "confirmation-denied":
            case "confirmation-expired":
                var confirmation = chat.Items.OfType<AgentToolCallItem>().Last();
                Assert.That(panel.OpenPermissionsWindow, Is.Null,
                    "A one-call confirmation rejection must not open persistent permission settings.");
                Assert.That(confirmation.CanReviewPermissions, Is.False);
                Assert.That(confirmation.StatusText, Does.Contain(state == "confirmation-denied"
                    ? "Confirmação negada" : "confirmação expirou"));
                break;
            case "outcome-unknown":
                Assert.That(chat.IsStatusError, Is.True);
                Assert.That(chat.StatusText, Does.Contain("Resultado incerto"));
                break;
        }
    }

    private static async Task CloseAsync(Window window, AgentChatViewModel chat, ChannelAgentRuntime runtime)
    {
        if (window.Content is AgentChatPanel { OpenApprovalWindow: { } approval })
        {
            approval.Close();
        }
        if (window.Content is AgentChatPanel { OpenPermissionsWindow: { } permissions })
        {
            permissions.Close();
        }

        if (chat.ActiveTurnId is { } turn)
        {
            runtime.Push(turn, AgentEventKind.TaskCompleted, outcome: AgentTurnOutcome.Cancelled);
        }

        await PumpAsync(() => chat.ActiveTurnId is null);
        window.Close();
        await chat.DisposeAsync();
    }

    private static AgentApprovalDetails Details(DateTimeOffset expires, AgentToolRisk risk) =>
        new(risk == AgentToolRisk.Destructive ? "mongo_delete_one" : "mongo_update_one",
            "Produção — cluster analítico com nome longo", "shop", "orders",
            "{ \"_id\": ObjectId(\"65a1f0c2e4b0a1b2c3d4e5f6\") }",
            risk == AgentToolRisk.Destructive ? "Excluir 1 documento identificado por _id."
                : "{ \"$set\": { \"status\": \"shipped\", \"shippedAt\": ISODate(\"2026-09-24T12:00:00.000Z\") } }",
            1, risk, expires);

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
