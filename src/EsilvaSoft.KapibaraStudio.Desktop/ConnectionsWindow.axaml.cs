using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class ConnectionsWindow : Window
{
    private readonly WorkspaceViewModel? _workspace;
    public ConnectionsWindow() { InitializeComponent(); Opened += (_, _) => SearchBox.Focus(); }
    public ConnectionsWindow(WorkspaceViewModel workspace) : this()
    {
        _workspace = workspace;
        var model = new ConnectionsViewModel(workspace.Workspace, workspace);
        DataContext = model;
        ProfileUuidPanel.DataContext = model.Uuid;
        ProfileUuidPanel.IsVisible = true;
    }
    private async void OpenSelected(object? sender, RoutedEventArgs e)
    {
        if (_workspace is null || DataContext is not ConnectionsViewModel { SelectedChoice: not null, IsOpening: false } vm) return;
        vm.IsOpening = true; vm.Error = "";
        try { await _workspace.OpenConnectionAsync(vm.SelectedChoice.Profile); vm.IsOpening = false; Close(true); }
        catch (Exception ex) { vm.Error = DesktopOperationErrorMessages.Describe(ex); }
        finally { vm.IsOpening = false; }
    }
    private void CloseDialog(object? sender, RoutedEventArgs e) { if (DataContext is not ConnectionsViewModel { IsOpening: true }) Close(false); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; CloseDialog(this, e); }
        base.OnKeyDown(e);
    }
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is ConnectionsViewModel { IsOpening: true }) e.Cancel = true;
        base.OnClosing(e);
    }
    private async void DeleteProfile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConnectionsViewModel { SelectedChoice: not null } vm) return;
        var localization = LocalizationViewModel.Current;
        var remove = localization.Resolve("remove");
        var answer = await Dialogs.ChooseAsync(
            this,
            localization.Resolve("removeLocalProfile"),
            string.Format(CultureInfo.InvariantCulture, localization.Resolve("removeProfilePrompt"), vm.SelectedChoice.Profile.Name),
            remove,
            localization.Resolve("cancel"));
        if (answer == remove)
            await vm.Editor.DeleteSelectedProfileCommand.ExecuteAsync(null);
    }
}
