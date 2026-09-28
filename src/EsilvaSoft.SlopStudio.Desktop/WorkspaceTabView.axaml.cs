using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Document;
using EsilvaSoft.SlopStudio.Core;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Autocomplete.Core.Completion;
using EsilvaSoft.SlopStudio.Desktop.ViewModels;
using EsilvaSoft.SlopStudio.Desktop.Agents;

namespace EsilvaSoft.SlopStudio.Desktop;

public partial class WorkspaceTabView : UserControl
{
    private AgentBufferEditor? _agentBufferEditor;
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    public string? SelectedCode => CodeEditor.SelectedText;
    public string? ExecutionCode(bool partial)
    {
        if (!partial) return null;
        if (!string.IsNullOrEmpty(SelectedCode)) return SelectedCode;
        if (DataContext is WorkspaceTabViewModel { IsConsole: true } tab)
        {
            var statement = tab.GetConsoleStatement(CodeEditor.CaretIndex);
            return statement.Length == 0 ? T("noStatementAtCursor") : tab.Text.Substring(statement.Start, statement.Length);
        }
        return null;
    }
    public WorkspaceTabView()
    {
        InitializeComponent();
        InitializeAutocomplete();
        InitializeResults();
        DataContextChanged += (_, _) =>
        {
            // The agent chat reads the selection synchronously when the user reviews a send; the guard keeps a tab
            // whose view now shows another tab from reading someone else's selection.
            if (DataContext is WorkspaceTabViewModel shown)
            {
                _agentBufferEditor = new AgentBufferEditor(this, shown);
                shown.EditorSelectionProvider = () => ReferenceEquals(DataContext, shown) ? CodeEditor.SelectedText : null;
                shown.EditorBufferProvider = () => ReferenceEquals(DataContext, shown) ? _agentBufferEditor : null;
            }
            else _agentBufferEditor = null;
            if (DataContext is WorkspaceTabViewModel tab) tab.ConfirmConsoleWrite = async (request, token) =>
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (TopLevel.GetTopLevel(this) is not Window owner) return false;
                    return await Dialogs.ChooseCancelableAsync(owner, T("confirmConsoleWrite"), request.Context + "\n\n" + T("confirmSendOperation"), token, T("execute"), T("cancel")) == T("execute");
                });
        };
        DetachedFromVisualTree += (_, _) => { _completionTab?.CancelTraditionalCompletion(); _formatCancellation?.Cancel(); };
        AttachedToVisualTree += (_, _) =>
        {
            if (TopLevel.GetTopLevel(this) is MainWindow { WorkspaceModel: { } vm })
            {
                SplitGrid.RowDefinitions[0].Height = new GridLength(vm.EditorRatio, GridUnitType.Star);
                SplitGrid.RowDefinitions[2].Height = new GridLength(1 - vm.EditorRatio, GridUnitType.Star);
            }
        };
    }

    private sealed class AgentBufferEditor(WorkspaceTabView view, WorkspaceTabViewModel tab) : IAgentBufferEditor
    {
        private readonly Dictionary<(Guid ProposalId, int HunkIndex), (TextAnchor Start, TextAnchor End)> _hunkAnchors = [];

        public string Text => ReferenceEquals(view.DataContext, tab) ? view.CodeEditor.Document.Text : "";

        public int? GetHunkLineHint(Guid proposalId, int hunkIndex)
        {
            if (!ReferenceEquals(view.DataContext, tab) ||
                !_hunkAnchors.TryGetValue((proposalId, hunkIndex), out var anchors) || anchors.Start.IsDeleted)
                return null;

            var prefix = view.CodeEditor.Document.Text.AsSpan(0, anchors.Start.Offset);
            var line = 0;
            for (var index = 0; index < prefix.Length; index++)
            {
                if (prefix[index] == '\n') line++;
                else if (prefix[index] == '\r' && (index + 1 == prefix.Length || prefix[index + 1] != '\n')) line++;
            }
            return line;
        }

        public bool TryApplyHunk(Guid proposalId, int hunkIndex, bool reverting,
            EsilvaSoft.SlopStudio.Application.Agents.Editing.LineDiffTextEdit edit)
        {
            return TryApplyHunks([new AgentHunkTextEdit(proposalId, hunkIndex, edit)], reverting);
        }

        public bool TryApplyHunks(IReadOnlyList<AgentHunkTextEdit> edits, bool reverting)
        {
            if (!ReferenceEquals(view.DataContext, tab) || tab.IsRunning || edits is null) return false;
            var document = view.CodeEditor.Document;
            var original = document.Text;
            var simulated = original;
            var keys = new HashSet<(Guid ProposalId, int HunkIndex)>();
            var tracked = new Dictionary<(Guid ProposalId, int HunkIndex), (int Start, int End)>();
            var deletedAnchors = new HashSet<(Guid ProposalId, int HunkIndex)>();
            if (reverting)
            {
                foreach (var (key, anchors) in _hunkAnchors)
                {
                    if (anchors.Start.IsDeleted || anchors.End.IsDeleted)
                    {
                        deletedAnchors.Add(key);
                        continue;
                    }
                    tracked[key] = (anchors.Start.Offset, anchors.End.Offset);
                }
            }

            // Validate the whole sequential batch, including every live hunk anchor, before changing the document.
            foreach (var item in edits)
            {
                if (item is null || item.Edit is null || item.ProposalId == Guid.Empty || item.HunkIndex < 0 ||
                    !keys.Add((item.ProposalId, item.HunkIndex))) return false;
                var edit = item.Edit;
                if (edit.Offset < 0 || edit.Length < 0 || edit.Offset + edit.Length > simulated.Length) return false;
                var key = (item.ProposalId, item.HunkIndex);
                if (reverting)
                {
                    if (deletedAnchors.Contains(key) || tracked.TryGetValue(key, out var anchor) &&
                        (anchor.Start != edit.Offset || anchor.End - anchor.Start != edit.Length)) return false;
                    tracked.Remove(key);
                }

                var editEnd = edit.Offset + edit.Length;
                var delta = edit.Replacement.Length - edit.Length;
                foreach (var anchorKey in tracked.Keys.ToArray())
                {
                    var anchor = tracked[anchorKey];
                    if (Overlaps(anchor.Start, edit.Offset, editEnd) || Overlaps(anchor.End, edit.Offset, editEnd)) return false;
                    tracked[anchorKey] = (Shift(anchor.Start, edit.Offset, editEnd, delta),
                        Shift(anchor.End, edit.Offset, editEnd, delta));
                }

                simulated = simulated.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Replacement);
            }

            if (!ReferenceEquals(view.DataContext, tab) || tab.IsRunning || document.Text != original) return false;
            using (document.RunUpdate())
            {
                foreach (var item in edits)
                {
                    var edit = item.Edit;
                    document.Replace(edit.Offset, edit.Length, edit.Replacement);
                    var key = (item.ProposalId, item.HunkIndex);
                    if (reverting)
                    {
                        _hunkAnchors.Remove(key);
                    }
                    else
                    {
                        var start = document.CreateAnchor(edit.Offset);
                        start.MovementType = AnchorMovementType.AfterInsertion;
                        var end = document.CreateAnchor(edit.Offset + edit.Replacement.Length);
                        end.MovementType = AnchorMovementType.BeforeInsertion;
                        _hunkAnchors[key] = (start, end);
                    }
                }
            }
            return true;
        }

        private static bool Overlaps(int position, int editStart, int editEnd) =>
            editEnd > editStart && position > editStart && position < editEnd;

        private static int Shift(int position, int editStart, int editEnd, int delta) =>
            position >= editEnd ? position + delta : position;

        public bool TryApply(IReadOnlyList<EsilvaSoft.SlopStudio.Application.Agents.Editing.LineDiffTextEdit> edits)
        {
            if (!ReferenceEquals(view.DataContext, tab) || tab.IsRunning || edits is null) return false;
            var document = view.CodeEditor.Document;
            var original = document.Text;
            var simulated = original;
            foreach (var edit in edits)
            {
                if (edit is null || edit.Offset < 0 || edit.Length < 0 || edit.Offset + edit.Length > simulated.Length) return false;
                simulated = simulated.Remove(edit.Offset, edit.Length).Insert(edit.Offset, edit.Replacement);
            }

            // Preflight the complete batch before opening an undo group, so a bad later edit cannot leave a partial patch.
            if (!ReferenceEquals(view.DataContext, tab) || tab.IsRunning || document.Text != original) return false;
            using (document.RunUpdate())
            {
                foreach (var edit in edits)
                {
                    document.Replace(edit.Offset, edit.Length, edit.Replacement);
                }
            }
            return true;
        }
    }
    private void SplitResized(object? sender, VectorEventArgs e)
    {
        var top = SplitGrid.RowDefinitions[0].ActualHeight;
        var bottom = SplitGrid.RowDefinitions[2].ActualHeight;
        if (top + bottom > 0 && TopLevel.GetTopLevel(this) is MainWindow { WorkspaceModel: { } vm }) vm.EditorRatio = top / (top + bottom);
    }
    private CancellationTokenSource? _formatCancellation;
    private CancellationTokenSource? _validationCancellation;
    public Task ValidationTask { get; private set; } = Task.CompletedTask;
    private async void ValidateCode(object? sender, RoutedEventArgs e) { ValidationTask = ValidateCoreAsync(); await ValidationTask; }
    private async Task ValidateCoreAsync()
    {
        if (DataContext is not WorkspaceTabViewModel { IsRunning: false } tab) return;
        _validationCancellation?.Cancel();
        using var operation = tab.Operations.Begin(T("validatingSyntaxOperation"), ApplicationOperationPriority.Normal);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        _validationCancellation = cancellation;
        var document = CodeEditor.Document;
        var original = document.Text;
        var mode = tab.Mode;
        var profile = tab.Profile; var database = tab.Database; var collection = tab.Collection;
        var start = CodeEditor.SelectionLength > 0 ? CodeEditor.SelectionStart : 0;
        var length = CodeEditor.SelectionLength > 0 ? CodeEditor.SelectionLength : original.Length;
        try
        {
            var result = await tab.ValidateCodeAsync(original.Substring(start, length), mode == "Agregação", cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (DataContext != tab || CodeEditor.Document != document || document.Text != original || tab.IsRunning || tab.Mode != mode ||
                tab.Profile != profile || tab.Database != database || tab.Collection != collection)
            { operation.Complete(ApplicationOperationStatus.Warning, T("textContextChangedDiscarded")); return; }
            var message = (length == original.Length ? T("editorPrefix") : T("selectionPrefix")) + result.Message;
            if (result.IsValid) { tab.Messages = message; tab.Errors = ""; tab.ResultTabIndex = 1; }
            else
            {
                tab.Errors = message; tab.ResultTabIndex = 2;
                CodeEditor.SelectionStart = start + Math.Clamp(result.Offset, 0, length);
                CodeEditor.SelectionEnd = start + Math.Clamp(result.Offset + result.Length, 0, length);
                CodeEditor.CaretIndex = CodeEditor.SelectionStart;
                CodeEditor.Focus();
            }
            operation.Complete(result.IsValid ? ApplicationOperationStatus.Success : ApplicationOperationStatus.Warning,
                result.IsValid ? T("validSyntax") : T("errorsAvailable"));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { operation.Complete(ApplicationOperationStatus.Cancelled, T("validationCancelled")); }
        catch (Exception ex)
            { tab.Errors = F("validationIncomplete", DesktopOperationErrorMessages.Describe(ex)); tab.ResultTabIndex = 2; operation.Complete(ApplicationOperationStatus.Error); }
        finally { if (ReferenceEquals(_validationCancellation, cancellation)) _validationCancellation = null; }
    }
    public Task FormattingTask { get; private set; } = Task.CompletedTask;
    private async void FormatCode(object? sender, RoutedEventArgs e) { FormattingTask = FormatCoreAsync(); await FormattingTask; }
    private async Task FormatCoreAsync()
    {
        if (DataContext is not WorkspaceTabViewModel tab) return;
        _formatCancellation?.Cancel();
        using var operation = tab.Operations.Begin(T("formattingQuery"), ApplicationOperationPriority.High);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.Token);
        _formatCancellation = cancellation;
        var document = CodeEditor.Document;
        var original = document.Text;
        var start = CodeEditor.SelectionLength > 0 ? CodeEditor.SelectionStart : 0;
        var length = CodeEditor.SelectionLength > 0 ? CodeEditor.SelectionLength : original.Length;
        try
        {
            var formatted = await tab.FormatCodeAsync(original.Substring(start, length), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (DataContext != tab || CodeEditor.Document != document || document.Text != original)
            { operation.Complete(ApplicationOperationStatus.Warning, T("formatChangedDiscarded")); return; }
            using (document.RunUpdate()) document.Replace(start, length, formatted);
            operation.Complete(ApplicationOperationStatus.Success, T("formatCompleted"));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { operation.Complete(ApplicationOperationStatus.Cancelled, T("formatCancelled")); }
        catch (Exception ex)
        { tab.Errors = F("formatFailed", ex.Message); tab.ResultTabIndex = 2; operation.Complete(ApplicationOperationStatus.Error, T("formatIncomplete")); }
        finally { if (ReferenceEquals(_formatCancellation, cancellation)) _formatCancellation = null; }
    }

    private async void ExportResults(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel tab || TopLevel.GetTopLevel(this) is not Window owner) return;
        var documents = tab.Documents.ToArray();
        try
        {
            var selection = await owner.StorageProvider.SaveFilePickerWithResultAsync(new FilePickerSaveOptions {
                Title = T("exportLoadedPage"), SuggestedFileName = "resultados.json", DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("Extended JSON") { Patterns = ["*.json"] }, new FilePickerFileType("CSV · proteção de fórmulas") { Patterns = ["*.csv"] }] });
            if (selection.File is not { } file) return;
            // O nome digitado pelo usuário deve prevalecer sobre o filtro inicial
            // do diálogo quando a extensão indicar explicitamente CSV.
            var csv = string.Equals(Path.GetExtension(file.Path.LocalPath), ".csv", StringComparison.OrdinalIgnoreCase)
                || (selection.SelectedFileType is { } selectedType && selectedType.Patterns?.Contains("*.csv") == true);
            using var operation = tab.Operations.Begin(F("exportingPage", documents.Length), ApplicationOperationPriority.High);
            try
            {
                await tab.ExportResultPageAsync(file.Path.LocalPath, documents, csv,
                    (done, total) => operation.Report(done, total, F("exportingPageProgress", done, total)), operation.Token);
                tab.Messages = F("pageExported", documents.Length) + (csv ? T("csvExportNote") : string.Empty);
                operation.Complete(ApplicationOperationStatus.Success, tab.Messages);
            }
            catch (OperationCanceledException) when (operation.Token.IsCancellationRequested)
            { tab.Messages = T("exportCancelled"); operation.Complete(ApplicationOperationStatus.Cancelled, tab.Messages); }
            catch
            { operation.Complete(ApplicationOperationStatus.Error, T("exportIncomplete")); throw; }
        }
        catch (Exception ex) { await Dialogs.ChooseAsync(owner, T("exportFailed"), DesktopOperationErrorMessages.Describe(ex, export: true), T("close")); }
    }

    private async void ChooseTarget(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel { IsRunning: false } tab || TopLevel.GetTopLevel(this) is not MainWindow { WorkspaceModel: { } vm } owner) return;
        var window = new Window { Title = T("targetWindowTitle"), Width = 480, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Spacing = 8, Margin = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = T("openConnectionLabel") });
        var connections = new ComboBox { ItemsSource = vm.Roots.Where(r => r.IsConnected).ToArray(), SelectedItem = vm.Roots.FirstOrDefault(r => r.Profile.Id == tab.Profile?.Id),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<ExplorerNodeViewModel>((node, _) => new TextBlock { Text = node?.Profile.Name }) };
        stack.Children.Add(connections);
        stack.Children.Add(new TextBlock { Text = T("databaseLabel") });
        var database = new ComboBox(); stack.Children.Add(database);
        stack.Children.Add(new TextBlock { Text = T("legacyAggregationCollection"), IsVisible = !tab.IsConsole });
        var collection = new TextBox { Text = tab.Collection, IsVisible = !tab.IsConsole }; stack.Children.Add(collection);
        var info = new TextBlock { Text = T("openConnectionTarget") + " " + T("notExecuted"), TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "muted" } }; stack.Children.Add(info);
        void UpdateBanks()
        {
            var root = connections.SelectedItem as ExplorerNodeViewModel;
            database.ItemsSource = root?.Children.Select(c => c.Database).ToArray() ?? [];
            database.SelectedItem = root?.Children.FirstOrDefault(c => c.Database == tab.Database)?.Database ?? root?.Children.FirstOrDefault()?.Database;
        }
        connections.SelectionChanged += (_, _) => UpdateBanks(); UpdateBanks();
        var apply = new Button { Content = T("applyTarget"), HorizontalAlignment = HorizontalAlignment.Right, Classes = { "primary" } };
        apply.Click += (_, _) =>
        {
            var profile = (connections.SelectedItem as ExplorerNodeViewModel)?.Profile;
            if (profile is null || database.SelectedItem is not string bank) { info.Text = T("chooseOpenConnectionDatabase"); return; }
            vm.BindActiveTab(profile, bank, collection.Text?.Trim() ?? "");
            window.Close();
        };
        stack.Children.Add(apply); window.Content = stack;
        window.KeyDown += (_, args) => { if (args.Key == Avalonia.Input.Key.Escape) { args.Handled = true; window.Close(); } };
        await window.ShowDialog(owner);
    }

    private async void OpenHistory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorkspaceTabViewModel tab || TopLevel.GetTopLevel(this) is not Window owner) return;
        await tab.LoadHistoryCommand.ExecuteAsync(null);
        var window = new HistoryWindow { DataContext = tab };
        await window.ShowDialog(owner);
    }
}
