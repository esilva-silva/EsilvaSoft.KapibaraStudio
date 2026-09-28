using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

public partial class WorkspaceTabView
{
    private WorkspaceTabViewModel? _resultsTab;
    private string? _syncedResultText;
    private bool _movingResultCaret;

    /// <summary>Document menu currently open in the results panel (tree or JSON view).</summary>
    public ContextMenu? ResultDocumentMenu { get; private set; }

    /// <summary>Completes when the last results action (copy, view or edit) finished; failures are reported in Mensagens.</summary>
    public Task ResultActionTask { get; private set; } = Task.CompletedTask;

    private void InitializeResults()
    {
        ResultTree.AddHandler(ContextRequestedEvent, ResultTreeContextRequested, RoutingStrategies.Tunnel);
        ResultTree.AddHandler(KeyDownEvent, ResultTreeKeyDown, RoutingStrategies.Tunnel);
        ResultJson.AddHandler(ContextRequestedEvent, ResultJsonContextRequested, RoutingStrategies.Tunnel);
        ResultJson.AddHandler(KeyDownEvent, ResultJsonKeyDown, RoutingStrategies.Tunnel);
        ResultJson.AddHandler(PointerPressedEvent, ResultJsonPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        ResultJson.PropertyChanged += ResultJsonPropertyChanged;
        DataContextChanged += (_, _) => BindResultsTab();
        AttachedToVisualTree += (_, _) => BindResultsTab();
        DetachedFromVisualTree += (_, _) => { ResultDocumentMenu?.Close(); UnbindResultsTab(); };
    }

    private void BindResultsTab()
    {
        UnbindResultsTab();
        if (DataContext is not WorkspaceTabViewModel tab) return;
        _resultsTab = tab;
        tab.PropertyChanged += ResultsTabChanged;
    }

    private void UnbindResultsTab()
    {
        if (_resultsTab is { } tab) tab.PropertyChanged -= ResultsTabChanged;
        _resultsTab = null;
    }

    private void ResultsTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WorkspaceTabViewModel.SelectedDocument) or nameof(WorkspaceTabViewModel.ResultView) or nameof(WorkspaceTabViewModel.ResultTabIndex))
            Dispatcher.UIThread.Post(RevealSelectedResult);
    }

    /// <summary>Keeps the selected document visible when it changes or the panel switches between JSON and tree.</summary>
    private void RevealSelectedResult()
    {
        if (DataContext is not WorkspaceTabViewModel tab) return;
        if (tab.IsTreeResultView)
        {
            if (tab.SelectedResultNode is { } node) ResultTree.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(item => ReferenceEquals(item.DataContext, node))?.BringIntoView();
            return;
        }
        if (tab.SelectedDocument is not { } document || ReferenceEquals(DocumentAt(tab, ResultJson.CaretIndex), document)) return;
        if (tab.ResultSegments.FirstOrDefault(segment => ReferenceEquals(segment.Document, document)) is not { } target) return;
        _movingResultCaret = true;
        try { ResultJson.CaretIndex = Math.Min(target.Start, ResultJson.Text?.Length ?? 0); }
        finally { _movingResultCaret = false; }
    }

    private void ResultJsonPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == MongoTextEditor.TextProperty)
        {
            // Caret coercion caused by new text must not select another document; the selection is revealed afterwards.
            _syncedResultText = ResultJson.Text;
            _movingResultCaret = true;
            Dispatcher.UIThread.Post(() => { _movingResultCaret = false; RevealSelectedResult(); });
            return;
        }
        if (e.Property != MongoTextEditor.CaretIndexProperty || _movingResultCaret || !ReferenceEquals(ResultJson.Text, _syncedResultText)
            || DataContext is not WorkspaceTabViewModel tab) return;
        if (DocumentAt(tab, ResultJson.CaretIndex) is { } document) tab.SelectResultDocument(document);
    }

    private static ResultDocumentViewModel? DocumentAt(WorkspaceTabViewModel tab, int index) =>
        tab.ResultSegments.FirstOrDefault(segment => index >= segment.Start && index <= segment.Start + segment.Length)?.Document;

    private void ResultJsonPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(ResultJson).Properties.IsRightButtonPressed) return;
        // A right click targets the document under the pointer; the caret moves there unless it lands inside the current selection.
        if (ResultJson.GetPositionFromPoint(e.GetPosition(ResultJson)) is not { } position) return;
        var index = ResultJson.Document.GetOffset(position.Location);
        var start = Math.Min(ResultJson.SelectionStart, ResultJson.SelectionEnd);
        var end = Math.Max(ResultJson.SelectionStart, ResultJson.SelectionEnd);
        if (start != end && index >= start && index <= end) return;
        ResultJson.CaretIndex = index;
    }

    private void ResultTreeContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel tab) return;
        var item = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<TreeViewItem>().FirstOrDefault();
        if (item?.DataContext is not ResultNodeViewModel { Document: { } document } node) return;
        e.Handled = true;
        if (ResultDocumentMenu?.IsOpen == true) return;
        tab.SelectedResultNode = node;
        OpenResultDocumentMenu(tab, document, item, includeTextCopy: false);
    }

    private void ResultTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsContextMenuKey(e) || DataContext is not WorkspaceTabViewModel { SelectedResultNode: { Document: { } document } node } tab) return;
        e.Handled = true;
        Control target = ResultTree.GetVisualDescendants().OfType<TreeViewItem>().FirstOrDefault(item => ReferenceEquals(item.DataContext, node)) ?? (Control)ResultTree;
        OpenResultDocumentMenu(tab, document, target, includeTextCopy: false);
    }

    private void ResultJsonContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (ResultDocumentMenu?.IsOpen == true || DataContext is not WorkspaceTabViewModel tab) return;
        OpenResultDocumentMenu(tab, DocumentAt(tab, ResultJson.CaretIndex), ResultJson, includeTextCopy: true);
    }

    private void ResultJsonKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsContextMenuKey(e) || DataContext is not WorkspaceTabViewModel tab) return;
        e.Handled = true;
        OpenResultDocumentMenu(tab, DocumentAt(tab, ResultJson.CaretIndex), ResultJson, includeTextCopy: true);
    }

    private static bool IsContextMenuKey(KeyEventArgs e) => e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift));

    private void OpenResultDocumentMenu(WorkspaceTabViewModel tab, ResultDocumentViewModel? document, Control target, bool includeTextCopy)
    {
        ResultDocumentMenu?.Close();
        var menu = new ContextMenu();
        MenuItem? first = null;
        if (document is not null)
        {
            menu.Items.Add(Caption(document.Summary + " · " + document.IdentityText));
            var view = new MenuItem { Header = T("viewDocumentJson"), Tag = "view" };
            view.Click += (_, _) => ResultActionTask = ShowResultDocumentJsonAsync(tab, document);
            menu.Items.Add(first = view);
            var availability = WorkspaceTabViewModel.GetEditAvailability(document);
            var edit = new MenuItem { Header = T("editDocument"), Tag = "edit", IsEnabled = availability.CanOpen };
            edit.Click += (_, _) => ResultActionTask = OpenResultDocumentEditorAsync(tab, document);
            menu.Items.Add(edit);
            if (!availability.CanOpen) menu.Items.Add(Caption(F("editUnavailable", availability.Reason)));
            menu.Items.Add(new Separator());
            var copy = new MenuItem { Header = T("copyJson"), Tag = "copy" };
            copy.Click += (_, _) => ResultActionTask = CopyResultTextAsync(tab, document.FormattedJson);
            menu.Items.Add(copy);
            // Identifier copies keep the stored BSON type; the UUID alternative of an ObjectId is offered only in UUID v4 mode.
            AddCopy(menu, tab, T("copyId"), "copy-id", document.IdentityValueText);
            AddCopy(menu, tab, T("copyIdQuery"), "copy-id-script", document.IdentityScript);
            AddCopy(menu, tab, T("copyUuidId"), "copy-id-uuid", document.IdentityUuidEquivalent);
        }
        else menu.Items.Add(Caption(T("noDocumentCursor")));
        if (includeTextCopy)
        {
            var selection = ResultJson.SelectedText;
            var copyText = new MenuItem { Header = T("copySelectedText"), Tag = "copy-selection", IsEnabled = !string.IsNullOrEmpty(selection) };
            copyText.Click += (_, _) => ResultActionTask = CopyResultTextAsync(tab, selection);
            if (document is null) menu.Items.Add(new Separator());
            menu.Items.Add(copyText);
            first ??= copyText.IsEnabled ? copyText : null;
        }
        ResultDocumentMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (!ReferenceEquals(ResultDocumentMenu, menu)) return;
            ResultDocumentMenu = null;
            if (target.IsAttachedToVisualTree()) target.Focus();
        };
        menu.Open(target);
        if (first is not null) Dispatcher.UIThread.Post(() => { if (menu.IsOpen) first.Focus(); });
    }

    private void AddCopy(ContextMenu menu, WorkspaceTabViewModel tab, string header, string tag, string? text)
    {
        if (text is null) return;
        var item = new MenuItem { Header = header, Tag = tag };
        item.Click += (_, _) => ResultActionTask = CopyResultTextAsync(tab, text);
        menu.Items.Add(item);
    }

    private static MenuItem Caption(string text) => new()
    {
        IsEnabled = false,
        Header = new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 380, Classes = { "metadata", "menu-caption" } }
    };

    private void CopySelectedResultJson(object? sender, RoutedEventArgs e)
    {
        if (DataContext is WorkspaceTabViewModel { SelectedDocument: { } document } tab) ResultActionTask = CopyResultTextAsync(tab, document.FormattedJson);
    }

    private async Task CopyResultTextAsync(WorkspaceTabViewModel tab, string text)
    {
        try { if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard) await clipboard.SetTextAsync(text); }
        catch (Exception ex) { tab.Messages = F("copyFailed", ex.Message); }
    }

    private async Task ShowResultDocumentJsonAsync(WorkspaceTabViewModel tab, ResultDocumentViewModel document)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        try { await new DocumentJsonWindow { DataContext = new DocumentJsonViewModel(document, tab.CodeFontSize) }.ShowDialog(owner); }
        catch (Exception ex) { tab.Messages = F("openDocumentFailed", ex.Message); }
    }

    private async Task OpenResultDocumentEditorAsync(WorkspaceTabViewModel tab, ResultDocumentViewModel document)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            using var model = tab.CreateResultDocumentEditor(document);
            await new DocumentMutationWindow { DataContext = model, Title = F("editDocumentTitle", document.Summary) }.ShowDialog(owner);
            if (model.Succeeded) tab.Messages = model.Status;
        }
        catch (Exception ex) { tab.Messages = DesktopOperationErrorMessages.Describe(ex); }
    }
}
