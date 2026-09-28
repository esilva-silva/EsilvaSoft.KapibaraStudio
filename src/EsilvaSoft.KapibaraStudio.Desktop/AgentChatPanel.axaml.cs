using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>
/// Native agent chat of one tab. The view owns focus, scrolling and dialogs; the view model owns state and the runtime.
/// Ctrl+Enter acts only while the composer has focus; Escape there discards the preview and never cancels a turn.
/// Streaming updates neither move focus nor scroll away from a message the user is reading.
/// </summary>
public partial class AgentChatPanel : UserControl
{
    private const double StickThreshold = 8;
    private AgentChatViewModel? _viewModel;
    private ScrollViewer? _historyScroll;
    private bool _stickToBottom = true;

    public AgentChatPanel()
    {
        InitializeComponent();
        Composer.AddHandler(KeyDownEvent, OnComposerKeyDown, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) => Attach(DataContext as AgentChatViewModel);
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

    public TextBox ComposerBox => Composer;

    private void Attach(AgentChatViewModel? viewModel)
    {
        if (_viewModel is not null)
        {
            _viewModel.ApprovalRequested -= OnApprovalRequested;
            _viewModel.SettingsRequested -= OnSettingsRequested;
            _viewModel.ComposerFocusRequested -= OnComposerFocusRequested;
            _viewModel.ExternalFilePickRequested -= OnExternalFilePickRequested;
            _viewModel.ProposalReviewRequested -= OnProposalReviewRequested;
        }

        _viewModel = viewModel;
        if (viewModel is not null)
        {
            viewModel.ApprovalRequested += OnApprovalRequested;
            viewModel.SettingsRequested += OnSettingsRequested;
            viewModel.ComposerFocusRequested += OnComposerFocusRequested;
            viewModel.ExternalFilePickRequested += OnExternalFilePickRequested;
            viewModel.ProposalReviewRequested += OnProposalReviewRequested;
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
    }

    private async void OnExternalFilePickRequested(object? sender, EventArgs e)
    {
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

    private void AttachHistoryScroll()
    {
        if (_historyScroll is not null)
        {
            return;
        }

        _historyScroll = History.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
        if (_historyScroll is not null)
        {
            _historyScroll.ScrollChanged += OnHistoryScrollChanged;
        }
    }

    private void OnHistoryScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_historyScroll is not { } scroll)
        {
            return;
        }

        if (e.ExtentDelta.Y != 0 && e.OffsetDelta.Y == 0)
        {
            // New content: follow the stream only if the reader was already at the end.
            if (_stickToBottom)
            {
                scroll.Offset = new Vector(scroll.Offset.X, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height));
            }

            return;
        }

        _stickToBottom = scroll.Offset.Y + scroll.Viewport.Height >= scroll.Extent.Height - StickThreshold;
    }

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
