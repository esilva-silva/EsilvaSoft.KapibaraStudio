using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class DocumentMutationWindow : Window
{
    public DocumentMutationWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } }, RoutingStrategies.Tunnel);
    }
    private async void Apply(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DocumentMutationViewModel { IsRunning: false } model) return;
        var operation = model.Operation switch { "Inserir" => LocalizationViewModel.Current.Resolve("insert"), "Editar" => LocalizationViewModel.Current.Resolve("edit"), "Excluir" => LocalizationViewModel.Current.Resolve("remove"), _ => model.Operation };
        if (await Dialogs.ChooseAsync(this, LocalizationViewModel.Current.Format("documentOperationTitle", operation), model.Context + "\n" + model.Identity + "\n" + LocalizationViewModel.Current.Resolve("confirmMutation"), LocalizationViewModel.Current.Resolve("confirm"), LocalizationViewModel.Current.Resolve("cancel")) != LocalizationViewModel.Current.Resolve("confirm")) return;
        try { await model.ExecuteConfirmedAsync(); }
        catch (Exception ex) { model.Status = LocalizationViewModel.Current.Format("failurePrefix", ex.Message); }
    }
    private void CancelOperation(object? sender, RoutedEventArgs e) => (DataContext as DocumentMutationViewModel)?.Cancel();
    private void CloseDialog(object? sender, RoutedEventArgs e) => Close();
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is DocumentMutationViewModel { IsRunning: true } model) { e.Cancel = true; model.Status = LocalizationViewModel.Current.Resolve("cancelBeforeClose"); }
        base.OnClosing(e);
    }
}
