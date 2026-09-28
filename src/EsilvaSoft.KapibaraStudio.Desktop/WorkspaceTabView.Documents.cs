using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class WorkspaceTabView
{
    private async void CopySelectedDocument(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel { SelectedDocument: not null } tab) return;
        try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(tab.SelectedDocument.DisplayJson); }
        catch (Exception ex) { tab.Messages = F("copyFailed", ex.Message); }
    }
    private void OpenSelectedDocument(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceTabViewModel { SelectedDocument: not null } tab && TopLevel.GetTopLevel(this) is MainWindow { WorkspaceModel: { } workspace })
            workspace.OpenDocumentInEditor(tab, tab.SelectedDocument.DisplayJson);
    }
    private async void MutateDocument(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel tab || TopLevel.GetTopLevel(this) is not Window owner || sender is not Button { Tag: string operation }) return;
        try
        {
            using var model = await tab.CreateDocumentMutationAsync(operation);
            var operationLabel = operation switch { "Inserir" => T("insert"), "Editar" => T("edit"), "Excluir" => T("remove"), _ => operation };
            var window = new DocumentMutationWindow { DataContext = model, Title = F("documentMutationTitle", operationLabel) };
            await window.ShowDialog(owner);
            if (model.Succeeded) tab.Messages = model.Status;
        }
        catch (Exception ex) { tab.Messages = DesktopOperationErrorMessages.Describe(ex); }
    }
}
