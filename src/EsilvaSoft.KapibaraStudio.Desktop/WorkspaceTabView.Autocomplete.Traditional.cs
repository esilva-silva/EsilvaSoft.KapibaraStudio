using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>Traditional (Ctrl+Space) completion list: presenter state, documentation preview and item acceptance.</summary>
public partial class WorkspaceTabView
{
    /// <summary>Status shown while a request is in flight; distinct from every other status text below.</summary>
    private static string TraditionalCompletionLoadingText => T("traditionalLoading");
    /// <summary>Status shown when the provider fails; distinct from "no suggestions" (which means it succeeded with zero matches).</summary>
    private static string TraditionalCompletionErrorText => T("traditionalSuggestionsError");
    /// <summary>Status shown when there is no completion provider for this tab at all (feature unavailable, not "no matches").</summary>
    private static string TraditionalCompletionUnavailableText => T("traditionalUnavailable");

    private void ShowTraditionalCompletion(IEnumerable<CompletionItem> items, bool isIncomplete = false, CompletionContext? context = null)
    {
        _traditionalPresenter.Show(items);
        _traditionalCompletionDocument = CodeEditor.Text ?? "";
        _traditionalCompletionIncomplete = isIncomplete;
        _traditionalCompletionContext = context;
        TraditionalCompletionPanel.IsVisible = true;
        RefreshTraditionalCompletionList();
        PositionTraditionalCompletionPanel();
    }

    /// <summary>Visible feedback while the request is running; the presenter itself stays closed so a text change
    /// during this window still flows through the normal invalidate/reopen path instead of being swallowed as a refilter.</summary>
    private void ShowTraditionalCompletionLoading()
    {
        TraditionalCompletionPanel.IsVisible = true;
        TraditionalCompletionList.ItemsSource = Array.Empty<CompletionItem>();
        TraditionalCompletionList.SelectedItem = null;
        TraditionalCompletionStatus.Text = TraditionalCompletionLoadingText;
        if (this.FindControl<TextBlock>("TraditionalCompletionDocumentation") is { } documentation) documentation.Text = "";
        PositionTraditionalCompletionPanel();
    }

    /// <summary>
    /// Motivo que abriu esta lista como fallback da IA explícita (<c>Ctrl+;</c>), quando foi o caso. Vive no mesmo
    /// ciclo da lista: é definido logo depois da invalidação que precede a consulta e apagado por
    /// <see cref="CloseTraditionalCompletion"/>. Fica em um campo, e não escrito uma vez na linha de estado, porque
    /// refiltrar a lista reescreve aquela linha — e o motivo precisa sobreviver ao refiltro.
    /// </summary>
    private string? _traditionalCompletionReason;

    private void ShowTraditionalCompletionMessage(string text)
    {
        _traditionalPresenter.Show([]);
        TraditionalCompletionPanel.IsVisible = true;
        TraditionalCompletionList.ItemsSource = Array.Empty<CompletionItem>();
        TraditionalCompletionList.SelectedItem = null;
        TraditionalCompletionStatus.Text = text;
        if (this.FindControl<TextBlock>("TraditionalCompletionDocumentation") is { } documentation) documentation.Text = "";
        PositionTraditionalCompletionPanel();
    }

    private void PositionTraditionalCompletionPanel()
    {
        var availableWidth = Math.Max(0, TraditionalCompletionLayer.Bounds.Width);
        var panelWidth = Math.Min(400, availableWidth);
        TraditionalCompletionPanel.Width = panelWidth > 0 ? panelWidth : 400;
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, CodeEditor.Document.TextLength);
        if (CodeEditor.TextArea.TextView.TranslatePoint(CodeEditor.PositionInTextView(caret), TraditionalCompletionLayer) is { } point)
        {
            var effectiveWidth = TraditionalCompletionPanel.Width > 0 ? TraditionalCompletionPanel.Width : 400;
            var maxLeft = Math.Max(0, TraditionalCompletionLayer.Bounds.Width - effectiveWidth);
            Canvas.SetLeft(TraditionalCompletionPanel, Math.Clamp(point.X, 0, maxLeft));
            const double panelHeight = 180;
            var below = point.Y + CodeEditor.LineHeight;
            var top = below + panelHeight <= TraditionalCompletionLayer.Bounds.Height
                ? below
                : Math.Max(0, point.Y - panelHeight);
            Canvas.SetTop(TraditionalCompletionPanel, top);
        }
    }

    private void RefreshTraditionalCompletionList()
    {
        // Captured before ItemsSource changes: assigning a new array that still contains the same selected item
        // instance makes the ListBox keep that old selection and raise SelectionChanged for it before we get to set
        // SelectedItem below. TraditionalCompletionSelectionChanged would otherwise feed that stale reselection back
        // into the presenter (undoing Move/Select), so the explicit SelectedItem assignment must use this captured
        // value instead of re-reading _traditionalPresenter.Selected after ItemsSource has already round-tripped it.
        var desired = _traditionalPresenter.Selected;
        TraditionalCompletionList.ItemsSource = _traditionalPresenter.Items.ToArray();
        TraditionalCompletionList.SelectedItem = desired;
        var summary = _traditionalPresenter.Items.Count == 0
            ? T("noSuggestionsMatch")
            : F("traditionalItemsStatus", _traditionalPresenter.Items.Count, _traditionalCompletionIncomplete ? T("traditionalDataLoading") : "", TraditionalCompletionShortcutsText());
        TraditionalCompletionStatus.Text = _traditionalCompletionReason is { Length: > 0 } reason ? $"{reason} · {summary}" : summary;
        ResolveTraditionalDocumentation(desired);
    }

    /// <summary>
    /// Formats the effective gestures for the list's own commands instead of a fixed "↑↓ mover · Enter/Tab aceita ·
    /// Esc fecha": a session that rebinds any of them would otherwise leave the status line describing a shortcut
    /// that no longer works.
    /// </summary>
    private string TraditionalCompletionShortcutsText()
    {
        if (DataContext is not WorkspaceTabViewModel tab) return "";
        var move = JoinGestures(tab.GestureText(EditorCommandIds.CompletionPrevious), tab.GestureText(EditorCommandIds.CompletionNext));
        var accept = JoinGestures(tab.GestureText(EditorCommandIds.CompletionAcceptEnter), tab.GestureText(EditorCommandIds.CompletionAccept));
        var close = JoinGestures(tab.GestureText(EditorCommandIds.CompletionClose));
        return $"{move} {T("moveVerb")} · {accept} {T("acceptVerb")} · {close} {T("closeVerb")}";
    }

    /// <summary>Distinct, non-empty gesture texts joined by "/"; "atalho não configurado" when every one is unbound.</summary>
    private static string JoinGestures(params string?[] gestures)
    {
        var distinct = gestures.Where(gesture => !string.IsNullOrEmpty(gesture)).Distinct().ToArray();
        return distinct.Length == 0 ? T("shortcutUnconfigured") : string.Join("/", distinct);
    }

    /// <summary>Immediately before the caret: the two characters this editor treats as automatic-open triggers.
    /// Reads the native, anchor-tracked CaretOffset instead of the cached CaretIndex wrapper, which a genuine
    /// keystroke can still be catching up to when the Text change notification for that same keystroke runs.</summary>
    private bool IsTraditionalTriggerCharacterAtCaret()
    {
        var text = CodeEditor.Text ?? "";
        var caret = Math.Clamp(CodeEditor.CaretOffset, 0, text.Length);
        return caret > 0 && text[caret - 1] is '.' or '$';
    }

    /// <summary>Re-evaluated one dispatcher tick later, once this keystroke's own property cascade has settled;
    /// see the call site in <c>WorkspaceTabView.Autocomplete.cs</c> for why it cannot run inline.</summary>
    private void ScheduleTraditionalTriggerCheck(WorkspaceTabViewModel tab) => Dispatcher.UIThread.Post(() =>
    {
        if (DataContext == tab && !_traditionalPresenter.IsOpen && CodeEditor.SelectionStart == CodeEditor.SelectionEnd
            && CodeEditor.IsKeyboardFocusWithin && IsTraditionalTriggerCharacterAtCaret())
            ShowTraditionalCompletionList(this, new RoutedEventArgs(), CompletionTrigger.TriggerCharacter);
    });

    private void CloseTraditionalCompletion()
    {
        _traditionalPresenter.Close();
        _traditionalDocumentationCancellation?.Cancel();
        _traditionalDocumentationCancellation = null;
        _traditionalDocumentationGeneration++;
        _traditionalCompletionDocument = null;
        _traditionalCompletionIncomplete = false;
        _traditionalCompletionContext = null;
        _traditionalCompletionReason = null;
        if (this.FindControl<TextBlock>("TraditionalCompletionDocumentation") is { } documentation) documentation.Text = "";
        if (this.FindControl<Border>("TraditionalCompletionPanel") is { } panel) panel.IsVisible = false;
    }

    /// <summary>
    /// Reads the native, anchor-tracked <c>CaretOffset</c> instead of the wrapped <c>CaretIndex</c> property, exactly
    /// like <see cref="IsTraditionalTriggerCharacterAtCaret"/>: while a Text change notification for a genuine
    /// keystroke is still running, <c>CaretIndex</c> can still hold the pre-keystroke value (it only catches up once
    /// its own, separately-raised property change fires). Using the stale wrapper here would filter the list one
    /// character behind whatever the user just typed.
    /// </summary>
    private string CurrentCompletionPrefix()
    {
        var text = CodeEditor.Text ?? "";
        var caret = Math.Clamp(CodeEditor.CaretOffset, 0, text.Length);
        var start = caret;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] is '_' or '$')) start--;
        return text[start..caret];
    }

    private bool AcceptTraditionalCompletion()
    {
        var item = _traditionalPresenter.Selected;
        if (item is null) return false;
        var text = item.Edit.NewText;
        IReadOnlyList<SnippetPlaceholder>? placeholders = null;
        if (item.Edit.IsSnippet && SnippetTemplate.TryParse(text, out var template, out _))
        {
            var expansion = template!.Expand(); text = expansion.Text; placeholders = expansion.Placeholders;
        }
        var original = CodeEditor.Text ?? "";
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, original.Length);
        var exactEdit = string.Equals(original, _traditionalCompletionDocument, StringComparison.Ordinal);
        var replacement = exactEdit ? item.Edit.ReplaceRange : new TextSpan(caret - CurrentCompletionPrefix().Length, CurrentCompletionPrefix().Length);
        if (replacement.Start < 0 || replacement.End > original.Length) { CloseTraditionalCompletion(); return false; }
        var start = replacement.Start;
        _acceptingCompletion = true;
        try
        {
            using (CodeEditor.Document.RunUpdate()) CodeEditor.Document.Replace(start, replacement.Length, text);
            CodeEditor.CaretIndex = start + text.Length;
            CodeEditor.SelectionStart = CodeEditor.SelectionEnd = CodeEditor.CaretIndex;
        }
        finally { _acceptingCompletion = false; }
        // List > Snippet precedence (ARB-05): accepting a list item opened from inside an active placeholder must
        // not silently end the snippet session. The edit above already ran under _acceptingCompletion, so
        // EditorCompletionChanged never touched _snippetSession; reanchor its placeholders by this edit's delta
        // instead, or end the session explicitly if the edit cannot be attributed safely to the active placeholder
        // (e.g. it crosses its boundary) — never leave stale offsets that a later Tab would navigate wrongly.
        if (placeholders is null && _snippetSession is { } activeSnippet
            && !activeSnippet.TryReanchorAfterEdit(replacement, text.Length, CodeEditor.Text ?? ""))
            _snippetSession = null;
        // Capturado antes de fechar a lista, que descarta o contexto: aceite e ranqueamento usam a mesma chave.
        if (DataContext is WorkspaceTabViewModel accepted)
        {
            accepted.RecordCompletionAccepted(_traditionalCompletionContext, item.SymbolId);
            _lastAcceptedCompletion = (_traditionalCompletionContext, item.SymbolId);
        }
        CloseTraditionalCompletion();
        if (placeholders is not null) BeginSnippet(start, placeholders);
        return true;
    }

    /// <summary>
    /// Último aceite desta aba, para que um desfazer imediato vire sinal negativo. Guarda apenas contexto e
    /// identificador de símbolo; a janela curta que caracteriza arrependimento é decidida pelo próprio rastreador.
    /// </summary>
    private (CompletionContext? Context, string SymbolId)? _lastAcceptedCompletion;

    private void RecordCompletionUndoneIfPending()
    {
        if (_lastAcceptedCompletion is not { } pending) return;
        _lastAcceptedCompletion = null;
        if (DataContext is WorkspaceTabViewModel tab) tab.RecordCompletionUndone(pending.Context, pending.SymbolId);
    }

    private void TraditionalCompletionSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TraditionalCompletionList.SelectedItem is CompletionItem item)
        {
            _traditionalPresenter.Select(item.SymbolId);
            ResolveTraditionalDocumentation(item);
        }
    }

    private async void ResolveTraditionalDocumentation(CompletionItem? item)
    {
        _traditionalDocumentationCancellation?.Cancel();
        if (item is null || !TraditionalCompletionPanel.IsVisible) return;
        var generation = ++_traditionalDocumentationGeneration;
        var document = _traditionalCompletionDocument;
        var cancellation = new CancellationTokenSource();
        _traditionalDocumentationCancellation = cancellation;
        TraditionalCompletionDocumentation.Text = T("loadingDetails");
        try
        {
            var resolved = await Task.Run(() => CompletionDocumentationResolver.Resolve(item, cancellation.Token), cancellation.Token);
            if (generation != _traditionalDocumentationGeneration || cancellation.IsCancellationRequested || !TraditionalCompletionPanel.IsVisible
                || !string.Equals(document, CodeEditor.Text ?? "", StringComparison.Ordinal) || _traditionalPresenter.Selected?.SymbolId != item.SymbolId) return;
            TraditionalCompletionDocumentation.Text = resolved.Text;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (ReferenceEquals(_traditionalDocumentationCancellation, cancellation)) _traditionalDocumentationCancellation = null;
            cancellation.Dispose();
        }
    }

    private void AcceptTraditionalCompletion(object? sender, RoutedEventArgs e)
    {
        if (AcceptTraditionalCompletion()) e.Handled = true;
    }

    /// <summary>XAML-bound entry point (Button.Click requires this exact delegate shape); always an explicit invocation.</summary>
    private void ShowTraditionalCompletionList(object? sender, RoutedEventArgs e) => ShowTraditionalCompletionList(sender, e, CompletionTrigger.Invoked);

    /// <param name="sender">Origem do evento; ignorado, a lista sempre pertence a esta view.</param>
    /// <param name="e">Evento roteado da origem; ignorado pelo mesmo motivo.</param>
    /// <param name="trigger">Como a lista foi pedida.</param>
    /// <param name="reason">
    /// Linha de estado a acompanhar a lista. Só a IA explícita a fornece, quando abre a lista como fallback: é ela
    /// que transforma "a lista abriu do nada" em "a lista abriu porque a IA não pôde atender, e por este motivo".
    /// </param>
    private async void ShowTraditionalCompletionList(object? sender, RoutedEventArgs e, CompletionTrigger trigger, string? reason = null)
    {
        if (DataContext is not WorkspaceTabViewModel tab) return;
        if (CodeEditor.SelectionStart != CodeEditor.SelectionEnd) return;
        var selectedSymbol = _traditionalPresenter.Selected?.SymbolId;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        InvalidateCompletion();
        // Depois da invalidação, que fecha a lista anterior e apagaria o motivo junto com ela.
        _traditionalCompletionReason = reason;
        if (tab.TraditionalCompletion is null)
        {
            ShowTraditionalCompletionMessage(reason is { Length: > 0 } explained
                ? $"{explained} · {TraditionalCompletionUnavailableText}" : TraditionalCompletionUnavailableText);
            return;
        }
        AutocompleteMetrics.CompletionRequested.Add(1,
            new KeyValuePair<string, object?>("modality", "list"),
            new KeyValuePair<string, object?>("trigger", trigger == CompletionTrigger.Invoked ? "invoked" : "trigger-character"));
        var version = _completionSession.Version;
        var original = tab.Text;
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, original.Length);
        var mode = tab.Mode;
        var profile = tab.Profile; var database = tab.Database; var collection = tab.Collection;
        bool IsCurrent() => _completionSession.Version == version && CodeEditor.CaretIndex == caret && tab.Text == original
            && DataContext == tab && tab.Profile == profile && tab.Database == database && tab.Collection == collection && tab.Mode == mode;
        ShowTraditionalCompletionLoading();
        try
        {
            AutocompleteMetrics.UiDispatcherTime.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("handler", "list"));
            // Owned per tab (EditorRequestScope), not a raw view-level CancellationTokenSource: superseding or
            // discarding this call never depends on which view instance happens to be bound right now.
            var traditional = await tab.GetTraditionalCompletionsAsync(new Language.Text.AvaloniaTextSnapshot(CodeEditor.Document), caret, CancellationToken.None, trigger);
            if (!IsCurrent()) return;
            ShowTraditionalCompletion(traditional.Items, traditional.IsIncomplete, traditional.Context);
            if (selectedSymbol is not null && _traditionalPresenter.Select(selectedSymbol) is not null) RefreshTraditionalCompletionList();
        }
        catch (OperationCanceledException) { if (IsCurrent()) CloseTraditionalCompletion(); }
        catch (Exception)
        {
            tab.Messages = T("traditionalUnavailable");
            if (IsCurrent()) ShowTraditionalCompletionMessage(TraditionalCompletionErrorText);
        }
    }
}
