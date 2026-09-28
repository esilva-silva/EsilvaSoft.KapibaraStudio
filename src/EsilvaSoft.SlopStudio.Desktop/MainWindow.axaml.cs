using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.VisualTree;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class MainWindow : Window
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    private bool _allowClose;
    private bool _closing;
    private bool _restartAfterClose;
    public Task InitializationTask { get; private set; } = Task.CompletedTask;
    public Task ConnectionDialogTask { get; private set; } = Task.CompletedTask;
    public MainWindow()
    {
        InitializeComponent();
        Opened += InitializeWorkspace;
        AddHandler(KeyDownEvent, OnWorkspaceKeyDown, RoutingStrategies.Tunnel);
    }
    public WorkspaceViewModel? WorkspaceModel => DataContext as WorkspaceViewModel;
    private void InitializeWorkspace(object? sender, EventArgs e)
    {
        if (WorkspaceModel is not { } vm) return;
        vm.ThemeChanged += (_, _) => ApplyTheme();
        vm.LayoutChanged += (_, _) => ApplyWorkspaceColumns(vm);
        InitializeAgentPanel(vm);
        InitializationTask = vm.InitializeAsync();
        ApplyTheme();
    }
    private void ApplyTheme()
    {
        if (Avalonia.Application.Current is { } app)
            app.RequestedThemeVariant = WorkspaceModel?.Theme switch { "Claro" => ThemeVariant.Light, "Escuro" => ThemeVariant.Dark, _ => ThemeVariant.Default };
    }
    private void OpenConnections(object? sender, RoutedEventArgs e) => ConnectionDialogTask = OpenConnectionDialogAsync();
    private async Task OpenConnectionDialogAsync()
    {
        if (WorkspaceModel is not { } vm) return;
        var dialog = new ConnectionsWindow(vm) { Width = Math.Min(800, Bounds.Width - 40), Height = Math.Min(560, Bounds.Height - 40) };
        await dialog.ShowDialog<bool>(this);
        ConnectionsButton.Focus();
        try { await vm.ReloadProfilesAsync(); } catch (Exception ex) { vm.SessionStatus = DesktopOperationErrorMessages.Describe(ex); }
    }
    private void OpenExplorerCollection(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel?.SelectedNode is { } node) WorkspaceModel.OpenCollection(node);
    }
    private void ExplorerKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OpenExplorerCollection(sender, e); e.Handled = true; }
        if (e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var host = Explorer.GetVisualDescendants().OfType<Grid>().FirstOrDefault(grid => ReferenceEquals(grid.DataContext, WorkspaceModel?.SelectedNode) && grid.ContextMenu is not null);
            if (host?.ContextMenu is { } menu) { menu.Open(host); e.Handled = true; }
        }
    }
    private void ExplorerResized(object? sender, VectorEventArgs e)
    {
        if (WorkspaceModel is { } vm) vm.ExplorerWidth = Math.Clamp(WorkspaceGrid.ColumnDefinitions[1].ActualWidth, 200, 420);
    }
    private async void OpenFile(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel is not { } vm) return;
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = T("openScriptPicker"), AllowMultiple = false, FileTypeFilter = [FilePickerFileTypes.All] });
        if (files.Count == 0) return;
        try { await vm.OpenTextFileAsync(files[0].Path.LocalPath); }
        catch (Exception ex) { await Dialogs.ChooseAsync(this, T("openFailed"), DesktopOperationErrorMessages.Describe(ex), T("close")); }
    }
    private void ApplyWorkspaceColumns(WorkspaceViewModel vm)
    {
        var compact = ClientSize.Width > 0 && ClientSize.Width < 1100;
        WorkspaceGrid.ColumnDefinitions[0].Width = new GridLength(compact ? 32 : 40);
        WorkspaceGrid.ColumnDefinitions[1].Width = new GridLength(compact ? 240 : vm.ExplorerWidth);
        ApplyAgentPanelLayout();
    }
    private void ShowConnectionsPanel(object? sender, RoutedEventArgs e) { if (WorkspaceModel is { } vm) vm.SelectedSidebar = "Connections"; }
    private void ShowFilesPanel(object? sender, RoutedEventArgs e) { if (WorkspaceModel is { } vm) vm.SelectedSidebar = "Files"; }
    private async void OpenFolder(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel is not { } vm) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Abrir pasta como workspace", AllowMultiple = false });
        if (folders.Count == 0) return;
        try { await vm.SetWorkspaceFolderAsync(folders[0].Path.LocalPath); ShowFilesPanel(sender, e); }
        catch (Exception ex) { await Dialogs.ChooseAsync(this, "Não foi possível abrir a pasta", ex.Message, T("close")); }
    }
    private void CloseFolder(object? sender, RoutedEventArgs e) => WorkspaceModel?.CloseWorkspaceFolder();
    private async void RefreshWorkspace(object? sender, RoutedEventArgs e) { if (WorkspaceModel is { } vm) await vm.RefreshWorkspaceFolderAsync(); }
    private async void NewWorkspaceFile(object? sender, RoutedEventArgs e) => await CreateWorkspaceEntryAsync(false);
    private async void NewWorkspaceFolder(object? sender, RoutedEventArgs e) => await CreateWorkspaceEntryAsync(true);
    private async Task CreateWorkspaceEntryAsync(bool directory)
    {
        if (WorkspaceModel is not { HasWorkspace: true } vm) return;
        var selected = vm.SelectedWorkspaceFile;
        var root = vm.WorkspaceRootPath;
        var name = await Dialogs.PromptAsync(this, directory ? "Nova pasta" : "Novo arquivo", "Nome", "");
        if (string.IsNullOrWhiteSpace(name) || vm.WorkspaceRootPath != root) return;
        try { await vm.CreateWorkspaceEntryAsync(name, directory, selected); }
        catch (Exception ex) { await Dialogs.ChooseAsync(this, "Não foi possível criar", DesktopOperationErrorMessages.Describe(ex), T("close")); }
    }
    private async void OpenWorkspaceFile(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel is not { } vm) return;
        try
        {
            if (vm.SelectedWorkspaceFile is { IsDirectory: false, IsPlaceholder: false } node) await vm.OpenTextFileAsync(node.FullPath);
            else if (vm.SelectedWorkspaceFile is { IsDirectory: true } directory) directory.IsExpanded = !directory.IsExpanded;
        }
        catch (Exception ex) { await Dialogs.ChooseAsync(this, T("openFailed"), DesktopOperationErrorMessages.Describe(ex), T("close")); }
    }
    private void WorkspaceFileKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; OpenWorkspaceFile(sender, e); }
        else if (e.Key == Key.Apps || e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            var host = WorkspaceTree.GetVisualDescendants().OfType<Grid>().FirstOrDefault(grid => ReferenceEquals(grid.DataContext, WorkspaceModel?.SelectedWorkspaceFile) && grid.ContextMenu is not null);
            if (host?.ContextMenu is { } menu) { menu.Open(host); e.Handled = true; }
        }
    }
    private void WorkspaceFileContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is Control control && control.DataContext is ViewModels.WorkspaceFileNodeViewModel node && WorkspaceModel is { } vm) vm.SelectedWorkspaceFile = node;
    }
    private async void RenameWorkspaceFile(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel?.SelectedWorkspaceFile is not { } node) return;
        var name = await Dialogs.PromptAsync(this, "Renomear", "Novo nome", node.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        try { await WorkspaceModel.RenameWorkspaceNodeAsync(node, name); } catch (Exception ex) { await Dialogs.ChooseAsync(this, "Não foi possível renomear", ex.Message, T("close")); }
    }
    private async void DeleteWorkspaceFile(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel?.SelectedWorkspaceFile is not { } node) return;
        if (await Dialogs.ChooseAsync(this, "Excluir item", $"Enviar {node.Name} para a lixeira?", "Excluir", T("cancel")) != "Excluir") return;
        try { await WorkspaceModel.DeleteWorkspaceNodeAsync(node); } catch (Exception ex) { await Dialogs.ChooseAsync(this, "Não foi possível excluir", ex.Message, T("close")); }
    }
    private async void SaveFile(object? sender, RoutedEventArgs e) { if (WorkspaceModel?.ActiveTab is { } tab) await SaveTabAsync(tab); }
    private async void SaveAs(object? sender, RoutedEventArgs e) { if (WorkspaceModel?.ActiveTab is { } tab) await SaveTabAsync(tab, choosePath: true); }
    public async Task<bool> SaveTabAsync(WorkspaceTabViewModel tab, bool choosePath = false)
    {
        var path = choosePath ? "" : tab.FilePath;
        if (string.IsNullOrEmpty(path))
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = T("saveScriptPicker"),
                SuggestedFileName = tab.Mode == "Texto" ? "sem-titulo.txt" : tab.IsConsole || tab.IsScript ? "consulta.js" : "consulta.json",
                FileTypeChoices = [new FilePickerFileType("Arquivo de texto") { Patterns = ["*.txt", "*.md", "*.json", "*.js", "*.*"] }, FilePickerFileTypes.All]
            });
            if (file is null) return false;
            path = file.Path.LocalPath;
        }
        try { await tab.SaveAsync(path); return true; }
        catch (TextFileConflictException conflict)
        {
            var resolution = await Dialogs.ChooseAsync(this, "Arquivo alterado externamente", $"{Path.GetFileName(conflict.FilePath)} foi alterado fora do {Branding.ProductName}.", "Recarregar", "Sobrescrever", T("cancel"));
            if (resolution == "Recarregar")
            {
                if (tab.IsDirty)
                {
                    var discard = await Dialogs.ChooseAsync(this, "Descartar alterações locais", "Recarregar substituirá o texto editado nesta aba. Continuar?", "Continuar", T("cancel"));
                    if (discard != "Continuar") return false;
                }
                try { await tab.OpenAsync(conflict.FilePath); return true; }
                catch (Exception reloadError) { await Dialogs.ChooseAsync(this, T("openFailed"), DesktopOperationErrorMessages.Describe(reloadError), T("close")); return false; }
            }
            if (resolution != "Sobrescrever") return false;
            try { await tab.SaveAsync(path, overwriteExternalChanges: true); return true; }
            catch (Exception retryError) { await Dialogs.ChooseAsync(this, T("notSaved"), DesktopOperationErrorMessages.Describe(retryError), T("close")); return false; }
        }
        catch (Exception ex) { await Dialogs.ChooseAsync(this, T("notSaved"), DesktopOperationErrorMessages.Describe(ex), T("close")); return false; }
    }
    private async void CloseTab(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is Control { DataContext: WorkspaceTabViewModel tab }) await TryCloseTabAsync(tab);
    }
    private async Task<bool> StopTabAsync(WorkspaceTabViewModel tab)
    {
        if (!tab.IsRunning) return true;
        if (await Dialogs.ChooseAsync(this, T("executionInProgress"), T("stopExecutionPrompt"), T("interrupt"), T("cancel")) != T("interrupt")) return false;
        tab.CancelCommand.Execute(null);
        if (tab.ExecuteCommand.ExecutionTask is { } task) await task;
        return !tab.IsRunning;
    }
    private async Task<bool> TryCloseTabAsync(WorkspaceTabViewModel tab)
    {
        if (!await StopTabAsync(tab)) return false;
        if (tab.IsDirty)
        {
            var choice = await Dialogs.ChooseAsync(this, T("tabClosePrompt"), F("saveChangesPrompt", tab.Title), T("save"), T("discard"), T("cancel"));
            if (choice is null || choice == T("cancel")) return false;
            if (choice == T("save") && !await SaveTabAsync(tab)) return false;
        }
        WorkspaceModel!.RemoveTab(tab);
        try { await WorkspaceModel.SaveSessionAsync(); } catch (Exception ex) { await Dialogs.ChooseAsync(this, T("sessionNotSaved"), DesktopOperationErrorMessages.Describe(ex), T("close")); }
        return true;
    }
    private void OpenTools(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel?.ActiveTab is not { Profile: not null, IsConnected: true } tab) { WorkspaceModel!.SessionStatus = T("openConnectionTarget"); return; }
        var vm = new MainWindowViewModel(WorkspaceModel.Workspace, autoLoadCollections: false)
        {
            SelectedProfile = tab.Profile, SelectedDatabase = tab.Database, SelectedCollection = tab.Collection,
            QueryFilter = tab.IsQuery ? tab.Text : "{}", QueryProjection = tab.Projection, QuerySort = tab.Sort,
            QueryHint = tab.Hint, QueryComment = tab.Comment, QueryCollation = tab.Collation,
            QueryLimit = tab.Limit, QuerySkip = tab.Skip, QueryBatchSize = tab.BatchSize, QueryMaxTimeMs = tab.MaxTimeMs,
            UuidRepresentation = WorkspaceModel.CaptureUuidPolicy().Resolve(tab.Profile.Id), IdentifierMode = WorkspaceModel.IdentifierMode
        };
        var window = new WorkspaceToolsWindow { DataContext = vm, Title = T("toolsWindowTitle") + " · " + tab.Context };
        window.Closing += (_, args) => { if (vm.IsOperationRunning) { args.Cancel = true; vm.StatusMessage = T("cancelBeforeClose"); } };
        _ = window.ShowDialog(this);
    }
    private void OpenEnvironments(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel is not { } workspace) return;
        var model = new EnvironmentsViewModel(workspace.Workspace);
        model.Saved += (_, _) => workspace.InvalidateEnvironment();
        var window = new EnvironmentsWindow { DataContext = model, Width = Math.Min(760, Bounds.Width - 40), Height = Math.Min(540, Bounds.Height - 40) };
        _ = window.ShowDialog(this);
    }

    private void OpenPreferences(object? sender, RoutedEventArgs e) => ShowPreferences();

    private async void OnWorkspaceKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || WorkspaceModel is not { } vm) return;
        var control = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        // The agent panel is its own focus scope: F5, Ctrl+Enter and Escape typed there never execute or cancel the
        // tab's MongoDB operation (Ctrl+Enter in the composer only reviews/sends the chat message).
        var inAgentPanel = AgentPanelHost.IsKeyboardFocusWithin;
        if (control && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.A)
        { e.Handled = true; HandleAgentShortcut(vm, inAgentPanel); return; }
        if (inAgentPanel && (e.Key is Key.F5 or Key.Escape || (control && e.Key == Key.Enter))) return;
        if (inAgentPanel && e.Key == Key.F6 && IsAgentPanelOverlay) { e.Handled = true; vm.IsAgentPanelOpen = false; return; }
        if (e.Key == Key.Escape && this.GetVisualDescendants().OfType<WorkspaceTabView>()
            .FirstOrDefault(v => v.DataContext == vm.ActiveTab)?.DismissCompletion() == true)
        { e.Handled = true; return; }
        if (control && e.Key == Key.T) { e.Handled = true; vm.NewTabCommand.Execute(null); }
        else if (control && e.Key == Key.Tab && vm.Tabs.Count > 0)
        {
            e.Handled = true;
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1;
            vm.ActiveTab = vm.Tabs[(vm.Tabs.IndexOf(vm.ActiveTab!) + step + vm.Tabs.Count) % vm.Tabs.Count];
        }
        else if (control && e.Key == Key.W && vm.ActiveTab is { } closingTab) { e.Handled = true; await TryCloseTabAsync(closingTab); }
        else if (e.Key == Key.F6)
        {
            e.Handled = true;
            var view = this.GetVisualDescendants().OfType<WorkspaceTabView>().FirstOrDefault(v => v.DataContext == vm.ActiveTab);
            var editor = view?.FindControl<SyntaxHighlighting.MongoTextEditor>("CodeEditor");
            if (editor?.IsKeyboardFocusWithin == true) { if (vm.IsFilesSidebar) WorkspaceTree.Focus(); else Explorer.Focus(); } else editor?.Focus();
        }
        else if (control && e.Key == Key.O && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { e.Handled = true; OpenFolder(sender, e); }
        else if (control && e.Key == Key.O) { e.Handled = true; OpenFile(sender, e); }
        else if (control && e.Key == Key.S) { e.Handled = true; if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) SaveAs(sender, e); else SaveFile(sender, e); }
        else if (e.Key == Key.F5 || (control && e.Key == Key.Enter))
        {
            e.Handled = true;
            if (vm.ActiveTab is { } tab)
            {
                var view = this.GetVisualDescendants().OfType<WorkspaceTabView>().FirstOrDefault(v => v.DataContext == tab);
                try { await tab.ExecuteCommand.ExecuteAsync(view?.ExecutionCode(control)); }
                catch (Exception ex) { tab.Errors = DesktopOperationErrorMessages.Describe(ex); tab.ResultTabIndex = 2; }
            }
        }
        else if (e.Key == Key.Escape && vm.ActiveTab is { IsRunning: true } tab) { e.Handled = true; tab.CancelCommand.Execute(null); }
    }
    private const double CompactTopBarWidth = 1100;
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Near the minimum width the top bar has no room for another labelled command.
        if (change.Property == ClientSizeProperty)
        {
            if (UpdateButton is not null) UpdateButton.Classes.Set("compact", ClientSize.Width < CompactTopBarWidth);
            if (AgentPanelButton is not null) AgentPanelButton.Classes.Set("compact", ClientSize.Width < CompactTopBarWidth);
            if (WorkspaceModel is { } vm) ApplyWorkspaceColumns(vm);
        }
    }

    /// <summary>Action started by the update button; exposed for UI verification.</summary>
    public Task UpdateActionTask { get; private set; } = Task.CompletedTask;
    private void UpdateButtonClick(object? sender, RoutedEventArgs e) => UpdateActionTask = HandleUpdateAsync();
    private async Task HandleUpdateAsync()
    {
        if (WorkspaceModel?.Updates is not { } updates) return;
        switch (updates.State)
        {
            case AppUpdateUiState.Available:
                await updates.DownloadCommand.ExecuteAsync(null);
                break;
            case AppUpdateUiState.ManualOnly when updates.Release is { } release:
                await Launcher.LaunchUriAsync(release.PageUrl);
                break;
            case AppUpdateUiState.Ready:
                var choice = await Dialogs.ChooseAsync(this, T("updateRestart"), F("updateReadyPrompt", updates.ReadyVersion), T("restartNow"), T("later"));
                if (choice == T("restartNow")) RestartForUpdate();
                break;
        }
    }

    /// <summary>Closes through the normal flow; the entry point installs the update and starts the new version.</summary>
    public void RestartForUpdate()
    {
        _restartAfterClose = true;
        Close();
    }

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (_allowClose) { base.OnClosing(e); return; }
        e.Cancel = true; base.OnClosing(e);
        if (_closing || WorkspaceModel is not { } vm) return;
        _closing = true;
        try
        {
            if (OwnedWindows.OfType<WorkspaceToolsWindow>().Any(w => w.DataContext is MainWindowViewModel { IsOperationRunning: true }))
            {
                await Dialogs.ChooseAsync(this, T("runningTool"), T("finishToolBeforeExit"), T("back"));
                return;
            }
            foreach (var tab in vm.Tabs.ToArray())
            {
                if (!await StopTabAsync(tab)) return;
                if (tab.IsDirty && (!vm.RecoverDrafts || ((tab.Profile?.Id ?? tab.MissingProfileId) is Guid profileId && !vm.SnapshotAllowsRecovery(profileId))))
                    if (!await TryCloseTabAsync(tab)) return;
            }
            try { await vm.SaveSessionAsync(); }
            catch (Exception ex)
            {
                if (await Dialogs.ChooseAsync(this, T("sessionNotSaved"), DesktopOperationErrorMessages.Describe(ex) + "\n" + T("closeWithoutRecoveryPrompt"), T("closeWithoutRecovery"), T("cancel")) != T("closeWithoutRecovery")) return;
            }
            Program.RestartAfterExit = _restartAfterClose;
            _allowClose = true;
            vm.Dispose();
            Close();
        }
        finally
        {
            _closing = false;
            // A cancelled close must not turn a later ordinary exit into a restart.
            if (!_allowClose) _restartAfterClose = false;
        }
    }
}
