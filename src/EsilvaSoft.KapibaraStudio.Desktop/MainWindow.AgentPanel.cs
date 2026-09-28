using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>
/// Hosting of the AI Agent panel (P7-L06-HOST). Collapsed at startup. When open, it docks at the right with its own
/// 5-unit divider as long as the tab area keeps its minimum-window width next to the explorer (see
/// <see cref="TabsMinWidth"/>); below that (e.g. the 960 minimum window) it becomes its own surface in place of the tab
/// area, and "×" / Ctrl+Shift+A return to the editor. The panel never shrinks the editor/results under the
/// minimum-window baseline and never replaces the explorer.
/// </summary>
public partial class MainWindow
{
    public const double AgentPanelMinWidth = 320;
    public const double AgentPanelMaxWidth = 560;
    public const double AgentPanelPreferredWidth = 340;
    /// <summary>
    /// Docking keeps the tab area at least as wide as it is at the 960 × 620 minimum window (960 − 32 − 240 − 5 = 683,
    /// rounded up): the editor, which shares the tab with its 240 side column, is never narrower next to the panel than
    /// the minimum-window baseline. Below that the panel becomes its own surface instead of squeezing the editor.
    /// </summary>
    private const double TabsMinWidth = 690;
    private const double DividerWidth = 5;
    private double _agentPanelWidth = AgentPanelPreferredWidth;
    private bool _agentPanelInitialized;

    /// <summary>True while the open panel replaces the tab area because the window is too narrow to dock it.</summary>
    public bool IsAgentPanelOverlay { get; private set; }

    /// <summary>The hosted panel (its DataContext is the active tab's chat while open).</summary>
    public AgentChatPanel AgentChatPanel => AgentChat;

    private void InitializeAgentPanel(WorkspaceViewModel vm)
    {
        if (_agentPanelInitialized)
        {
            return;
        }

        _agentPanelInitialized = true;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WorkspaceViewModel.IsAgentPanelOpen))
            {
                OnAgentPanelToggled(vm);
            }
        };
        vm.LanguageChanged += (_, _) => ApplyAgentPanelLayout();
        AgentChat.CloseRequested += (_, _) => vm.IsAgentPanelOpen = false;
        ApplyAgentPanelLayout();
    }

    private void OnAgentPanelToggled(WorkspaceViewModel vm)
    {
        var focusWasInPanel = AgentPanelHost.IsKeyboardFocusWithin;
        ApplyAgentPanelLayout();
        if (vm.IsAgentPanelOpen)
        {
            // After the layout pass that makes the panel visible; an explicit opening moves focus to the chat.
            Dispatcher.UIThread.Post(FocusAgentPanel, DispatcherPriority.Loaded);
        }
        else if (focusWasInPanel || TabsArea.IsVisible)
        {
            Dispatcher.UIThread.Post(FocusActiveEditor, DispatcherPriority.Loaded);
        }
    }

    /// <summary>Recomputes docked/overlay placement for the current window and explorer widths.</summary>
    private void ApplyAgentPanelLayout()
    {
        if (WorkspaceModel is not { } vm || AgentPanelHost is null)
        {
            return;
        }

        var columns = WorkspaceGrid.ColumnDefinitions;
        if (!vm.IsAgentPanelOpen)
        {
            CollapseAgentColumns(columns);
            Grid.SetColumn(AgentPanelHost, 5);
            AgentPanelHost.IsVisible = false;
            AgentPanelSplitter.IsVisible = false;
            TabsArea.IsVisible = true;
            IsAgentPanelOverlay = false;
            AgentChat.CloseLabel = T("agentPanelClose");
            AgentChat.CloseContent = "×";
            return;
        }

        var windowWidth = ClientSize.Width > 0 ? ClientSize.Width : Width;
        var left = WidthOf(columns[0]) + WidthOf(columns[1]) + WidthOf(columns[2]);
        var available = windowWidth - left - TabsMinWidth - DividerWidth;
        IsAgentPanelOverlay = available < AgentPanelMinWidth;
        AgentPanelHost.IsVisible = true;
        if (IsAgentPanelOverlay)
        {
            CollapseAgentColumns(columns);
            Grid.SetColumn(AgentPanelHost, 3);
            AgentPanelSplitter.IsVisible = false;
            TabsArea.IsVisible = false;
            AgentChat.CloseLabel = T("agentPanelBackToEditor");
            AgentChat.CloseContent = T("agentPanelBackToEditorShort");
            return;
        }

        // Keep a small logical-pixel inset clear of the right window edge. Fractional render scales round grid columns to
        // physical pixels; without this reserve the final column can extend by a fraction of a DIP.
        var width = Math.Clamp(Math.Min(_agentPanelWidth, Math.Max(AgentPanelMinWidth, available - 2)),
            AgentPanelMinWidth, AgentPanelMaxWidth);
        columns[4].Width = new GridLength(DividerWidth);
        columns[5].MinWidth = AgentPanelMinWidth;
        columns[5].MaxWidth = AgentPanelMaxWidth;
        columns[5].Width = new GridLength(width);
        Grid.SetColumn(AgentPanelHost, 5);
        AgentPanelSplitter.IsVisible = true;
        TabsArea.IsVisible = true;
        AgentChat.CloseLabel = T("agentPanelClose");
        AgentChat.CloseContent = "×";
    }

    private static void CollapseAgentColumns(ColumnDefinitions columns)
    {
        columns[4].Width = new GridLength(0);
        columns[5].MinWidth = 0;
        columns[5].MaxWidth = double.PositiveInfinity;
        columns[5].Width = new GridLength(0);
    }

    private static double WidthOf(ColumnDefinition column) =>
        column.Width.IsAbsolute ? column.Width.Value : column.ActualWidth;

    private void AgentPanelResized(object? sender, Avalonia.Input.VectorEventArgs e)
    {
        var actual = WorkspaceGrid.ColumnDefinitions[5].ActualWidth;
        if (actual > 0)
        {
            _agentPanelWidth = Math.Clamp(actual, AgentPanelMinWidth, AgentPanelMaxWidth);
        }
    }

    /// <summary>
    /// Ctrl+Shift+A: opens the panel (focus on the chat); from inside the panel it collapses it and returns to the
    /// editor; from elsewhere with the panel open it moves focus to the chat. No other shortcut is overridden.
    /// </summary>
    private void HandleAgentShortcut(WorkspaceViewModel vm, bool focusInPanel)
    {
        if (!vm.IsAgentPanelOpen)
        {
            vm.IsAgentPanelOpen = true;
        }
        else if (focusInPanel)
        {
            vm.IsAgentPanelOpen = false;
        }
        else
        {
            FocusAgentPanel();
        }
    }

    private void FocusAgentPanel()
    {
        if (!AgentPanelHost.IsVisible)
        {
            return;
        }

        Control target = AgentChat.ComposerBox.IsEffectivelyEnabled ? AgentChat.ComposerBox
            : AgentChat.FindControl<Button>("RefreshProvidersButton") is { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } refresh
                ? refresh
                : AgentChat.ClosePanel;
        target.Focus(NavigationMethod.Tab);
    }

    private void FocusActiveEditor()
    {
        if (WorkspaceModel?.ActiveTab is not { } tab || !TabsArea.IsVisible)
        {
            return;
        }

        var view = this.GetVisualDescendants().OfType<WorkspaceTabView>().FirstOrDefault(v => v.DataContext == tab);
        view?.FindControl<SyntaxHighlighting.MongoTextEditor>("CodeEditor")?.Focus();
    }

    public void FocusEditorForAgent(WorkspaceTabViewModel tab)
    {
        var view = this.GetVisualDescendants().OfType<WorkspaceTabView>().FirstOrDefault(candidate => candidate.DataContext == tab);
        view?.FindControl<SyntaxHighlighting.MongoTextEditor>("CodeEditor")?.Focus();
    }
}
