using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using System.Globalization;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class WorkspaceToolsWindow : Window
{
    public WorkspaceToolsWindow()
    {
        InitializeComponent();
    }

    private async void ConfirmDestructive(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel model || sender is not Button { Tag: string command }) return;
        var localization = LocalizationViewModel.Current;
        var target = $"{model.SelectedProfile?.Name} › {model.SelectedDatabase} › {model.SelectedCollection} · {model.SelectedProfile?.RoutingLabel}";
        var detail = command == "DropIndexCommand"
            ? string.Format(CultureInfo.InvariantCulture, localization.Resolve("indexDetail"), model.IndexNameToDrop)
            : string.Format(CultureInfo.InvariantCulture, localization.Resolve("filterDetail"), model.MutationFilter) + (model.DeleteManyDocuments ? "\n" + localization.Resolve("allMatchesAffected") : "");
        var confirm = localization.Resolve("confirm");
        if (await Dialogs.ChooseAsync(this, localization.Resolve("confirmChange"), target + "\n" + detail + "\n" + (sender as Button)?.Content + "?", confirm, localization.Resolve("cancel")) != confirm) return;
        var action = command switch
        {
            "ReplaceDocumentCommand" => model.ReplaceDocumentCommand,
            "DeleteDocumentCommand" => model.DeleteDocumentCommand,
            _ => model.DropIndexCommand
        };
        try { await action.ExecuteAsync(null); }
        catch (Exception ex) { model.StatusMessage = DesktopOperationErrorMessages.Describe(ex); }
    }

    public void SelectSection(string header)
    {
        var item = this.GetLogicalDescendants().OfType<TabItem>().FirstOrDefault(tab => tab.Header as string == header);
        if (item is null) return;
        foreach (var ancestor in item.GetLogicalAncestors().OfType<TabItem>().Reverse())
            if (ancestor.Parent is TabControl owner) owner.SelectedItem = ancestor;
        if (item.Parent is TabControl tabs) tabs.SelectedItem = item;
    }

    private async void SelectScriptToOpen(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = LocalizationViewModel.Current.Resolve("openScriptTitle"),
            FileTypeFilter =
            [
                new FilePickerFileType(LocalizationViewModel.Current.Resolve("javascriptFileType")) { Patterns = ["*.js"] },
                new FilePickerFileType(LocalizationViewModel.Current.Resolve("allFiles")) { Patterns = ["*"] }
            ]
        });

        var path = files.Count == 0 ? null : files[0].Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path) && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ScriptFilePath = path;
        }
    }

    private async void SelectScriptToSave(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "consulta.js",
            Title = LocalizationViewModel.Current.Resolve("saveScriptTitle"),
            FileTypeChoices =
            [
                new FilePickerFileType(LocalizationViewModel.Current.Resolve("javascriptFileType")) { Patterns = ["*.js"] }
            ]
        });

        var path = file?.Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path) && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ScriptFilePath = path;
        }
    }

    private async void SelectImportDirectory(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = LocalizationViewModel.Current.Resolve("importMongoFolderTitle")
        });

        var path = folders.Count == 0 ? null : folders[0].Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path) && DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ImportSourceDirectory = path;
        }
    }
}
