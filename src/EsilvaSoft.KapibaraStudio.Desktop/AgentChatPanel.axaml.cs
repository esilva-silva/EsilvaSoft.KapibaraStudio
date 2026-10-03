using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using System.Collections.Specialized;
using System.ComponentModel;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>
/// Native workspace agent chat. The view owns focus, scrolling and dialogs; the view model owns state and the runtime.
/// Ctrl+Enter acts only while the composer has focus; Escape there discards the preview and never cancels a turn.
/// Streaming updates neither move focus nor scroll away from a message the user is reading.
/// </summary>
public partial class AgentChatPanel : UserControl
{
    private const double StickThreshold = 8;
    private AgentChatViewModel? _viewModel;
    private ScrollViewer? _historyScroll;
    private bool _stickToBottom = true;
    private AgentChatConversation? _observedConversation;
    private readonly Dictionary<Guid, HistoryScrollState> _conversationScrollStates = [];
    private int _scrollRequestVersion;
    private bool _isAttached;
    private int _unreadMessageCount;

    public AgentChatPanel()
    {
        InitializeComponent();
        Composer.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => Attach(DataContext as AgentChatViewModel);
        SizeChanged += (_, _) => UpdateRegionLimits();
        History.TemplateApplied += (_, _) => AttachHistoryScroll();
        History.LayoutUpdated += (_, _) => AttachHistoryScroll();
    }

    /// <summary>
    /// Set by a host that can collapse the panel (the main window): shows the header "×" with this accessible name and
    /// tooltip, and raises <see cref="CloseRequested"/> when activated. Null hides the button (standalone use).
    /// </summary>
    public static readonly StyledProperty<string?> CloseLabelProperty =
        AvaloniaProperty.Register<AgentChatPanel, string?>(nameof(CloseLabel));

    public string? CloseLabel
    {
        get => GetValue(CloseLabelProperty);
        set => SetValue(CloseLabelProperty, value);
    }

    /// <summary>Visible content of the host close button: "×" when docked, a text such as "Voltar ao editor" when the
    /// panel replaces the editor area.</summary>
    public static readonly StyledProperty<string> CloseContentProperty =
        AvaloniaProperty.Register<AgentChatPanel, string>(nameof(CloseContent), "×");

    public string CloseContent
    {
        get => GetValue(CloseContentProperty);
        set => SetValue(CloseContentProperty, value);
    }

    /// <summary>The user asked the host to collapse the panel (or return to the editor in the compact layout).</summary>
    public event EventHandler? CloseRequested;

    public Button ClosePanel => ClosePanelButton;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CloseLabelProperty && ClosePanelButton is not null)
        {
            var label = CloseLabel;
            ClosePanelButton.IsVisible = !string.IsNullOrEmpty(label);
            ToolTip.SetTip(ClosePanelButton, label);
            Avalonia.Automation.AutomationProperties.SetName(ClosePanelButton, label ?? "");
        }
        else if (change.Property == CloseContentProperty && ClosePanelButton is not null)
        {
            ClosePanelButton.Content = CloseContent;
        }
    }

    private void OnClosePanelClick(object? sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>The approval dialog currently open from this panel, if any.</summary>
    public AgentApprovalWindow? OpenApprovalWindow { get; private set; }

    /// <summary>The settings dialog currently open from this panel, if any.</summary>
    public AgentSettingsWindow? OpenSettingsWindow { get; private set; }

    /// <summary>The permissions dialog currently open from the chat panel, if any.</summary>
    public AgentPermissionsWindow? OpenPermissionsWindow { get; private set; }

    public TextBox ComposerBox => Composer;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _isAttached = true;
        LocalizationViewModel.Current.PropertyChanged += OnLocalizationPropertyChanged;
        Attach(DataContext as AgentChatViewModel);
        UpdateLatestMessageButton();
        AttachHistoryScroll();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _isAttached = false;
        LocalizationViewModel.Current.PropertyChanged -= OnLocalizationPropertyChanged;
        _scrollRequestVersion++;
        SaveCurrentScrollState();
        if (_historyScroll is not null)
        {
            _historyScroll.ScrollChanged -= OnHistoryScrollChanged;
            _historyScroll = null;
        }

        Attach(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void Attach(AgentChatViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.ApprovalRequested -= OnApprovalRequested;
            _viewModel.SettingsRequested -= OnSettingsRequested;
            _viewModel.PermissionsRequested -= OnPermissionsRequested;
            _viewModel.ComposerFocusRequested -= OnComposerFocusRequested;
            _viewModel.ExternalFilePickRequested -= OnExternalFilePickRequested;
            _viewModel.ProposalReviewRequested -= OnProposalReviewRequested;
        }

        _viewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            viewModel.ApprovalRequested += OnApprovalRequested;
            viewModel.SettingsRequested += OnSettingsRequested;
            viewModel.PermissionsRequested += OnPermissionsRequested;
            viewModel.ComposerFocusRequested += OnComposerFocusRequested;
            viewModel.ExternalFilePickRequested += OnExternalFilePickRequested;
            viewModel.ProposalReviewRequested += OnProposalReviewRequested;
        }

        ObserveConversation(viewModel?.ActiveConversation);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AgentChatViewModel.ActiveConversation))
        {
            ObserveConversation(_viewModel?.ActiveConversation);
        }
    }

    private void ObserveConversation(AgentChatConversation? conversation)
    {
        if (ReferenceEquals(conversation, _observedConversation))
        {
            if (conversation is not null && _isAttached)
            {
                conversation.Items.CollectionChanged -= OnConversationItemsChanged;
                conversation.Items.CollectionChanged += OnConversationItemsChanged;
            }

            return;
        }
        SaveCurrentScrollState();
        if (_observedConversation is not null)
            _observedConversation.Items.CollectionChanged -= OnConversationItemsChanged;

        _observedConversation = conversation;
        if (conversation is not null && _isAttached)
            conversation.Items.CollectionChanged += OnConversationItemsChanged;

        _scrollRequestVersion++;
        if (conversation is null || !_isAttached) return;
        if (_conversationScrollStates.TryGetValue(conversation.Id, out var state))
        {
            _stickToBottom = state.StickToBottom;
            _unreadMessageCount = state.UnreadMessageCount;
            UpdateLatestMessageButton();
            QueueRestore(state.OffsetY, state.StickToBottom);
        }
        else
        {
            _stickToBottom = true;
            _unreadMessageCount = 0;
            UpdateLatestMessageButton();
            QueueRestore(null, stickToBottom: true);
        }
    }

    private void OnConversationItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_isAttached || _observedConversation is null) return;
        var messages = e.NewItems?.OfType<AgentChatMessageItem>().ToArray() ?? [];
        if (messages.Any(static item => item.IsUser))
        {
            _stickToBottom = true;
            _unreadMessageCount = 0;
            QueueRestore(null, stickToBottom: true);
        }
        else if (_stickToBottom)
        {
            QueueRestore(null, stickToBottom: true);
        }
        else if (messages.Any(static item => !item.IsUser))
        {
            _unreadMessageCount += messages.Count(static item => !item.IsUser);
            UpdateLatestMessageButton();
            SaveCurrentScrollState();
        }
    }

    private void ShowHistory(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null) return;
        _ = _viewModel.LoadHistoryCommand.ExecuteAsync(null);
        HistoryButton.Flyout?.ShowAt(HistoryButton);
    }

    private void SelectWorkspaceFile(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is null || sender is not Control { DataContext: AgentWorkspaceFileChoice file }) return;
        _viewModel.AddWorkspaceFile(file.FullPath);
        WorkspaceFileButton.Flyout?.Hide();
        AttachButton.Flyout?.Hide();
        Composer.Focus();
    }

    private async void OnExternalFilePickRequested(object? sender, EventArgs e)
    {
        AttachButton.Flyout?.Hide();
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storage || _viewModel is null) return;
        try
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = true,
                Title = "Selecionar arquivos externos"
            });
            foreach (var file in files)
            {
                if (file.TryGetLocalPath() is { Length: > 0 } path) _viewModel.AddExternalFile(path);
            }
        }
        catch (Exception)
        {
            // Picker failures are non-fatal; no attachment was added.
        }
    }

    private void OnComposerKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        if (e.Key == Key.Enter && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Plain Enter inserts a line; only Ctrl+Enter in this scope reviews or sends.
            e.Handled = true;
            if (_viewModel.SendCommand.CanExecute(null))
            {
                _ = _viewModel.SendCommand.ExecuteAsync(null);
            }
        }
    }

    private void OnComposerFocusRequested(object? sender, EventArgs e) => Composer.Focus();

    private void OnProposalReviewRequested(object? sender, AgentEditProposalReviewViewModel review) => _ = ShowProposalReviewAsync(review);

    private async Task ShowProposalReviewAsync(AgentEditProposalReviewViewModel review)
    {
        var window = new AgentEditProposalReviewWindow { DataContext = review };
        try
        {
            if (TopLevel.GetTopLevel(this) is Window owner && owner.IsVisible)
                await window.ShowDialog(owner);
            else
            {
                var closed = new TaskCompletionSource();
                window.Closed += (_, _) => closed.TrySetResult();
                window.Show();
                await closed.Task;
            }
        }
        catch (Exception)
        {
            // The active editor still contains the original or selected hunks; no file has been saved.
        }
        finally
        {
            Composer.Focus();
        }
    }

    private void UpdateRegionLimits()
    {
        // Exceptional combinations (expanded context, long prompt, account error) scroll locally.
        // Reserve space for the history, helper and send/cancel row even in the minimum host size.
        HeaderScroll.MaxHeight = Math.Max(80, Bounds.Height * .2);
        StatusScroll.MaxHeight = Math.Max(48, Bounds.Height * .1);
        ComposerDetailsScroll.MaxHeight = Math.Max(96, Bounds.Height - HeaderScroll.MaxHeight - StatusScroll.MaxHeight - 200);
    }

    private void AttachHistoryScroll()
    {
        if (!_isAttached) return;
        var current = History.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (ReferenceEquals(current, _historyScroll)) return;
        SaveCurrentScrollState();
        if (_historyScroll is not null) _historyScroll.ScrollChanged -= OnHistoryScrollChanged;
        _historyScroll = current;
        if (_historyScroll is not null)
        {
            _historyScroll.ScrollChanged += OnHistoryScrollChanged;
            if (_observedConversation is { } conversation && _conversationScrollStates.TryGetValue(conversation.Id, out var state))
                QueueRestore(state.OffsetY, state.StickToBottom);
            else
                QueueRestore(null, stickToBottom: true);
        }
    }

    private void QueueRestore(double? offsetY, bool stickToBottom)
    {
        if (!_isAttached || _historyScroll is null) return;
        var version = ++_scrollRequestVersion;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isAttached || version != _scrollRequestVersion || _historyScroll is not { } scroll) return;
            var target = stickToBottom
                ? Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height)
                : Math.Clamp(offsetY ?? scroll.Offset.Y, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
            scroll.Offset = new Vector(scroll.Offset.X, target);
            _stickToBottom = stickToBottom;
            if (stickToBottom) _unreadMessageCount = 0;
            UpdateLatestMessageButton();
            SaveCurrentScrollState();
        }, DispatcherPriority.Background);
    }

    private void SaveCurrentScrollState()
    {
        if (_observedConversation is not { } conversation || _historyScroll is not { } scroll) return;
        _conversationScrollStates[conversation.Id] = new HistoryScrollState(scroll.Offset.Y, _stickToBottom, _unreadMessageCount);
    }

    private void OnLatestMessageClick(object? sender, RoutedEventArgs e)
    {
        if (_historyScroll is not { } scroll) return;
        _stickToBottom = true;
        _unreadMessageCount = 0;
        QueueRestore(null, stickToBottom: true);
    }

    private void OnHistoryScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_historyScroll is not { } scroll)
        {
            return;
        }

        var geometryChanged = e.ExtentDelta.Y != 0 || e.ViewportDelta.Y != 0;
        if (geometryChanged)
        {
            // Decide from the gap before layout changed; the layout itself may also adjust Offset.
            var oldOffset = scroll.Offset.Y - e.OffsetDelta.Y;
            var oldExtent = scroll.Extent.Height - e.ExtentDelta.Y;
            var oldViewport = scroll.Viewport.Height - e.ViewportDelta.Y;
            var wasAtEnd = oldOffset + oldViewport >= oldExtent - StickThreshold;
            if (wasAtEnd)
            {
                _stickToBottom = true;
                QueueRestore(null, stickToBottom: true);
            }
            else
            {
                _stickToBottom = IsAtEnd(scroll);
                if (_stickToBottom) _unreadMessageCount = 0;
                UpdateLatestMessageButton();
            }
        }
        else if (Math.Abs(e.OffsetDelta.Y) > 0)
        {
            // With no geometry change, offset movement is a user scroll (or an explicit restore).
            _stickToBottom = IsAtEnd(scroll);
            if (_stickToBottom) _unreadMessageCount = 0;
            UpdateLatestMessageButton();
        }
        SaveCurrentScrollState();
    }

    private static bool IsAtEnd(ScrollViewer scroll) =>
        scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - StickThreshold;

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            UpdateLatestMessageButton();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_isAttached) UpdateLatestMessageButton();
        });
    }

    private void UpdateLatestMessageButton()
    {
        if (LatestMessageButton is null) return;
        var text = _unreadMessageCount > 0
            ? LocalizationViewModel.Current.Format("agentLatestMessageCount", _unreadMessageCount)
            : LocalizationViewModel.Current.Resolve("agentLatestMessage");
        LatestMessageButton.Content = text;
        LatestMessageButton.IsVisible = !_stickToBottom;
        Avalonia.Automation.AutomationProperties.SetName(LatestMessageButton, text);
    }

    private sealed record HistoryScrollState(double OffsetY, bool StickToBottom, int UnreadMessageCount);

    private void OnApprovalRequested(object? sender, AgentApprovalViewModel approval) => _ = ShowApprovalAsync(approval);

    private async Task ShowApprovalAsync(AgentApprovalViewModel approval)
    {
        if (OpenApprovalWindow is not null)
        {
            return; // One modal at a time; the card keeps "Revisar aprovação…" for the others.
        }

        var window = new AgentApprovalWindow { DataContext = approval };
        OpenApprovalWindow = window;
        try
        {
            if (TopLevel.GetTopLevel(this) is Window owner && owner.IsVisible)
            {
                await window.ShowDialog(owner);
            }
            else
            {
                var closed = new TaskCompletionSource();
                window.Closed += (_, _) => closed.TrySetResult();
                window.Show();
                await closed.Task;
            }
        }
        catch (Exception)
        {
            // A dialog that cannot open leaves the approval pending; the runtime denies it on expiry.
        }
        finally
        {
            OpenApprovalWindow = null;
            Composer.Focus();
        }
    }

    private void OnSettingsRequested(object? sender, EventArgs e) => _ = ShowSettingsAsync();

    private void OnPermissionsRequested(object? sender, string? section) => _ = ShowPermissionsDialogAsync(section);

    private async Task ShowPermissionsDialogAsync(string? section)
    {
        if (_viewModel is null || OpenPermissionsWindow is not null || _viewModel.SelectedProvider is not { } selected)
        {
            return;
        }

        var permissions = _viewModel.CreatePermissionsViewModel(selected.ProviderId, section);
        var window = new AgentPermissionsWindow { DataContext = permissions };
        OpenPermissionsWindow = window;
        try
        {
            if (TopLevel.GetTopLevel(this) is Window owner && owner.IsVisible)
            {
                await window.ShowDialog(owner);
            }
            else
            {
                var closed = new TaskCompletionSource();
                window.Closed += (_, _) => closed.TrySetResult();
                window.Show();
                await closed.Task;
            }
        }
        catch (Exception)
        {
            // The current permission set remains in effect; no denied tool is retried automatically.
        }
        finally
        {
            OpenPermissionsWindow = null;
            Composer.Focus();
        }
    }

    private async Task ShowSettingsAsync()
    {
        if (_viewModel is null || OpenSettingsWindow is not null)
        {
            return;
        }

        var window = new AgentSettingsWindow { DataContext = _viewModel.CreateSettingsViewModel() };
        window.PermissionsRequested += async (_, _) => await ShowPermissionsAsync(window);
        OpenSettingsWindow = window;
        try
        {
            if (TopLevel.GetTopLevel(this) is Window owner && owner.IsVisible)
            {
                await window.ShowDialog(owner);
            }
            else
            {
                var closed = new TaskCompletionSource();
                window.Closed += (_, _) => closed.TrySetResult();
                window.Show();
                await closed.Task;
            }
        }
        catch (Exception)
        {
            // Nothing changed; the chat keeps its previous provider state.
        }
        finally
        {
            OpenSettingsWindow = null;
            _viewModel?.ReloadProviders();
            ConfigureButton.Focus();
        }
    }

    private async Task ShowPermissionsAsync(Window settingsOwner)
    {
        if (_viewModel is null || settingsOwner.DataContext is not AgentSettingsViewModel settings ||
            settings.SelectedProvider is not { } selected)
        {
            return;
        }

        var permissions = _viewModel.CreatePermissionsViewModel(selected.ProviderId);
        var window = new AgentPermissionsWindow { DataContext = permissions };
        permissions.Saved = _viewModel.OnPermissionsSaved;
        try
        {
            await window.ShowDialog(settingsOwner);
        }
        catch (Exception)
        {
            // Provider settings remain open; the failed permissions operation is reported in its own window.
        }
        finally
        {
            permissions.Saved = null;
            settingsOwner.Activate();
        }
    }
}
