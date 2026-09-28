using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class AutocompleteSettingsWindow : Window
{
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    public AutocompleteSettingsWindow()
    {
        InitializeComponent();
        _statusTimer.Tick += (_, _) => (DataContext as AutocompleteSettingsViewModel)?.RefreshStatus();
        Opened += async (_, _) =>
        {
            EnableAutocomplete.Focus();
            _statusTimer.Start();
            // Opening the AI preferences rescans the models directory and asks the runtime which hardware exists.
            if (DataContext is AutocompleteSettingsViewModel model) await Task.WhenAll(model.OpenedAsync(), model.LoadRemoteModelsIfNeededAsync());
        };
        Closed += (_, _) =>
        {
            _statusTimer.Stop();
            if (DataContext is not AutocompleteSettingsViewModel model) return;
            // A model download keeps running after the dialog closes; the status bar shows and cancels it.
            model.TestCommand.Cancel(); model.RefreshModelsCommand.Cancel(); model.DetectHardwareCommand.Cancel(); model.LoadRemoteModelsCommand.Cancel();
        };
    }

    private async void ChooseModelsDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AutocompleteSettingsViewModel model) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = LocalizationViewModel.Current.Resolve("modelDirectoryPicker"), AllowMultiple = false });
            if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path) return;
            model.ModelDirectory = path;
            await model.RefreshModelsCommand.ExecuteAsync(null);
        }
        catch (Exception) { model.OperationStatus = LocalizationViewModel.Current.Resolve("openFailed") + ": " + LocalizationViewModel.Current.Resolve("modelsDirectoryUndefined"); }
    }

    private async void ChooseExternalModel(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AutocompleteSettingsViewModel model) return;
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = LocalizationViewModel.Current.Resolve("onnxModelFolderPicker"), AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) await model.SelectExternalModelAsync(path);
        }
        catch (Exception) { model.OperationStatus = LocalizationViewModel.Current.Resolve("openFailed") + ": " + LocalizationViewModel.Current.Resolve("modelsDirectoryUndefined"); }
    }

    private async void OpenModelsDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AutocompleteSettingsViewModel model || model.EnsureModelsDirectory() is not { } directory) return;
        try
        {
            if (!await Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(directory))) model.OperationStatus = LocalizationViewModel.Current.Format("createDirectoryFailed", directory, "");
        }
        catch (Exception ex) { model.OperationStatus = LocalizationViewModel.Current.Format("createDirectoryFailed", directory, ex.Message); }
    }

    private void CloseDialog(object? sender, RoutedEventArgs e) => Close();

    private void TokenSuggestionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not AutocompleteSettingsViewModel model || sender is not ComboBox combo) return;
        // Use the item added by the selection event. SelectedItem can already point to the next item while
        // Avalonia is reconciling an editable ComboBox, which used to turn an exact 32 into a neighbouring value.
        var value = e.AddedItems.OfType<string>().LastOrDefault();
        if (value is null) return;
        combo.Text = value;
        if (ReferenceEquals(combo, ContextTokenBox)) model.ContextTokensText = value;
        else if (ReferenceEquals(combo, MaximumTokenBox)) model.MaximumTokensText = value;
    }

    private void TokenEditorLostFocus(object? sender, RoutedEventArgs e) => (DataContext as AutocompleteSettingsViewModel)?.CommitTokenEditors();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Close(); }
        base.OnKeyDown(e);
    }
}
