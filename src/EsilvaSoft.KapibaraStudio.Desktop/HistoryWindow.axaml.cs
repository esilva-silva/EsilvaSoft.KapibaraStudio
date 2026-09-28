using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class HistoryWindow : Window
{
    public HistoryWindow() => InitializeComponent();
    private void OpenConsoleHistory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceTabViewModel { SelectedConsoleHistory: { } entry } && Owner is MainWindow { WorkspaceModel: { } workspace })
        { workspace.OpenConsoleHistory(entry); Close(); }
    }
    private async Task<bool> CanReplaceAsync(WorkspaceTabViewModel tab)
    {
        if (tab.IsRunning || !tab.IsDirty) return !tab.IsRunning;
        var localization = LocalizationViewModel.Current;
        var replace = localization.Resolve("replace");
        return await Dialogs.ChooseAsync(this, localization.Resolve("replaceContentTitle"), localization.Resolve("replaceContentPrompt"), replace, localization.Resolve("cancel")) == replace;
    }
    private async void ApplyHistory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceTabViewModel { SelectedHistory: not null } tab && await CanReplaceAsync(tab)) { tab.SelectedSavedQuery = null; tab.ApplyHistoryCommand.Execute(null); Close(); }
    }
    private async void ApplySavedQuery(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceTabViewModel { SelectedSavedQuery: not null } tab && await CanReplaceAsync(tab)) { tab.SelectedHistory = null; tab.ApplyHistoryCommand.Execute(null); Close(); }
    }
    private async void OpenRecent(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel { SelectedScriptHistory: not null } tab || Owner is not MainWindow { WorkspaceModel: { } vm }) return;
        var entry = tab.SelectedScriptHistory;
        vm.NewTabCommand.Execute(null);
        try { await vm.ActiveTab!.OpenAsync(entry.Path); if (entry.InputJson is not null) vm.ActiveTab.InputJson = entry.InputJson; Close(); }
        catch (Exception ex) { await Dialogs.ChooseAsync(this, LocalizationViewModel.Current.Resolve("fileUnavailable"), DesktopOperationErrorMessages.Describe(ex), LocalizationViewModel.Current.Resolve("close")); }
    }
    private async void DeleteSaved(object? sender, RoutedEventArgs e)
    {
        var localization = LocalizationViewModel.Current;
        var remove = localization.Resolve("remove");
        if (DataContext is WorkspaceTabViewModel { SelectedSavedQuery: not null } tab
            && await Dialogs.ChooseAsync(this, localization.Resolve("removeSavedQuery"), localization.Resolve("removeSavedQueryPrompt"), remove, localization.Resolve("cancel")) == remove)
            await tab.DeleteSavedQueryCommand.ExecuteAsync(null);
    }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Close(); } base.OnKeyDown(e); }
}
