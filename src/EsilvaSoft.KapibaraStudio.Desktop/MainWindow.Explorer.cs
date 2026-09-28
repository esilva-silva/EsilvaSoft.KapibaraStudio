using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class MainWindow
{
    private static ExplorerNodeViewModel? Node(object? sender) => (sender as Control)?.DataContext as ExplorerNodeViewModel;
    private void ExplorerContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (WorkspaceModel is { } workspace && Node(sender) is { } node) workspace.SelectedNode = node;
    }
    private async void ExplorerConnect(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel is not { } workspace || Node(sender) is not { } node) return;
        try { await workspace.OpenConnectionAsync(node.Profile); }
        catch (Exception ex) { workspace.ExplorerStatus = DesktopOperationErrorMessages.Describe(ex); }
    }
    private void ExplorerDisconnect(object? sender, RoutedEventArgs e) { if (Node(sender) is { } node) WorkspaceModel?.Disconnect(node); }
    private async void ExplorerRefresh(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel is not { } workspace || Node(sender) is not { } node) return;
        workspace.SelectedNode = node;
        await workspace.RefreshExplorerCommand.ExecuteAsync(null);
    }
    private void ExplorerOpenDocuments(object? sender, RoutedEventArgs e) { if (Node(sender) is { } node) WorkspaceModel?.OpenCollection(node); }
    private async void ExplorerViewIndexes(object? sender, RoutedEventArgs e)
    {
        if (Node(sender) is not { } node) return;
        var collection = node;
        while (collection is not null && collection.Kind != ExplorerNodeKind.Collection) collection = collection.Parent;
        if (collection is null) return;
        await collection.LoadAsync(); collection.IsExpanded = true;
        if (collection.Children.FirstOrDefault(c => c.Kind == ExplorerNodeKind.Indexes) is { } indexes)
        { await indexes.LoadAsync(); indexes.IsExpanded = true; if (WorkspaceModel is { } workspace) workspace.SelectedNode = indexes; }
    }
    private void ExplorerDetails(object? sender, RoutedEventArgs e) { if (Node(sender) is { } node && WorkspaceModel is { } workspace) { workspace.SelectedNode = node; _ = workspace.Details.SelectAsync(node); } }
    private void ExplorerServerInfo(object? sender, RoutedEventArgs e)
    {
        if (Node(sender) is { } node && WorkspaceModel is { } workspace) { workspace.SelectedNode = node; _ = workspace.Details.SelectServerAsync(node); }
    }
    private void ExplorerGenerate(object? sender, RoutedEventArgs e)
    {
        if (Node(sender) is { } node && sender is MenuItem { Tag: string action } && Enum.TryParse<ExplorerScriptOperation>(action, out var operation))
            WorkspaceModel?.OpenScript(node, operation);
    }
    private void ExplorerTools(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel is not { } workspace || Node(sender) is not { } node) return;
        if (!node.Root.IsConnected) { workspace.ExplorerStatus = T("connectBeforeTools"); return; }
        var model = new MainWindowViewModel(workspace.Workspace, autoLoadCollections: false)
        { SelectedProfile = node.Profile, SelectedDatabase = node.Database, SelectedCollection = node.Collection ?? "", IndexNameToDrop = node.Index?.Name ?? "" };
        var window = new WorkspaceToolsWindow { DataContext = model, Title = T("toolsTitle") + " · " + node.Context };
        window.Opened += (_, _) => window.SelectSection((sender as MenuItem)?.Tag as string ?? "Coleções");
        window.Closing += (_, args) => { if (model.IsOperationRunning) { args.Cancel = true; model.StatusMessage = T("cancelBeforeClose"); } };
        _ = window.ShowDialog(this);
    }
    private async void ExplorerDropIndex(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel is not { } workspace || Node(sender) is not { Index: not null } node) return;
        if (await Dialogs.ChooseAsync(this, T("removeIndex"), node.Context + "\n" + F("removeIndexPrompt", node.Index.Name), T("remove"), T("cancel")) != T("remove")) return;
        try { await workspace.DropExplorerIndexAsync(node); }
        catch (Exception ex) { workspace.ExplorerStatus = DesktopOperationErrorMessages.Describe(ex); }
    }
    private async void ExplorerCopyDefinition(object? sender, RoutedEventArgs e)
    {
        if (Node(sender)?.Index is not { } index) return;
        try { if (Clipboard is not null) await Clipboard.SetTextAsync(index.Definition); }
        catch (Exception ex) { if (WorkspaceModel is { } workspace) workspace.ExplorerStatus = DesktopOperationErrorMessages.Describe(ex); }
    }
    private async void SelectExplorerInstance(object? sender, RoutedEventArgs e)
    {
        if (WorkspaceModel is not { } workspace || workspace.Details.Node is not { } node) return;
        await workspace.Details.SelectAsync(node);
        if (!ReferenceEquals(workspace.Details.Node, node)) return;
        var instances = workspace.Details.Instances.ToArray();
        var dialog = new Window { Title = T("instancesTitle") + " · " + node.Profile.Name, Width = 620, Height = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = node.Profile.RoutingLabel, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        var selection = new ComboBox { ItemsSource = instances.Select(i => i.Host + " · " + i.Role).ToArray(), HorizontalAlignment = HorizontalAlignment.Stretch, SelectedIndex = instances.Length > 0 ? 0 : -1 };
        panel.Children.Add(selection);
        var status = new TextBlock { Text = instances.Length == 0 ? T("noSelectableMember") : instances[0].SelectionHint, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        panel.Children.Add(status);
        selection.SelectionChanged += (_, _) => { if (selection.SelectedIndex >= 0) status.Text = instances[selection.SelectedIndex].SelectionHint; };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var automatic = new Button { Content = T("autoSelection") };
        var direct = new Button { Content = T("useInstance"), IsEnabled = instances.Length > 0 };
        var close = new Button { Content = T("close") };
        async Task ApplyAsync(string? host)
        {
            automatic.IsEnabled = direct.IsEnabled = false;
            try { await workspace.SelectInstanceAsync(node, host); dialog.Close(); }
            catch (Exception ex) { status.Text = DesktopOperationErrorMessages.Describe(ex); automatic.IsEnabled = true; direct.IsEnabled = instances.Length > 0; }
        }
        automatic.Click += async (_, _) => await ApplyAsync(null);
        direct.Click += async (_, _) =>
        {
            if (selection.SelectedIndex < 0) return;
            var instance = instances[selection.SelectedIndex];
            if (!instance.CanSelect) { status.Text = instance.SelectionHint; return; }
            await ApplyAsync(instance.Host);
        };
        close.Click += (_, _) => dialog.Close();
        dialog.KeyDown += (_, args) => { if (args.Key == Key.Escape) { args.Handled = true; dialog.Close(); } };
        buttons.Children.Add(automatic); buttons.Children.Add(direct); buttons.Children.Add(close); panel.Children.Add(buttons);
        dialog.Content = panel; await dialog.ShowDialog(this);
    }
}
