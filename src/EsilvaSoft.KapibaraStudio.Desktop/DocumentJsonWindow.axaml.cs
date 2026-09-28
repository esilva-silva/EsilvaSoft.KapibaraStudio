using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class DocumentJsonWindow : Window
{
    public DocumentJsonWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }, RoutingStrategies.Tunnel);
        Opened += (_, _) => JsonText.Focus();
    }

    /// <summary>Completes when the formatted JSON reached the clipboard or the failure is shown in the status line.</summary>
    public Task CopyTask { get; private set; } = Task.CompletedTask;

    private void CopyJson(object? sender, RoutedEventArgs e) => CopyTask = CopyAsync();

    private async Task CopyAsync()
    {
        if (DataContext is not DocumentJsonViewModel model) return;
        try
        {
            if (Clipboard is not { } clipboard) { model.Status = LocalizationViewModel.Current.Resolve("clipboardUnavailable"); return; }
            await clipboard.SetTextAsync(model.Json);
            model.Status = LocalizationViewModel.Current.Resolve("jsonCopied");
        }
        catch (Exception ex) { model.Status = LocalizationViewModel.Current.Format("copyFailed", ex.Message); }
    }

    private void CloseDialog(object? sender, RoutedEventArgs e) => Close();

}
