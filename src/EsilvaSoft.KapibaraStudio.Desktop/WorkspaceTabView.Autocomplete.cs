using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>
/// Ciclo de vida da sugestão em linha (ghost text) e o despacho de teclado compartilhado com a lista tradicional
/// (ver <c>WorkspaceTabView.Autocomplete.Traditional.cs</c>) e com as posições de snippet
/// (ver <c>WorkspaceTabView.Autocomplete.Snippets.cs</c>).
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Avalonia controls release editor sessions on DetachedFromVisualTree and recreate them on attachment.")]
public partial class WorkspaceTabView
{
    private CompletionSession _completionSession = new();
    /// <summary>
    /// Dono da sugestão automática deste editor: uma pendência substituível, atraso por relógio injetado e descarte de
    /// resposta obsoleta. O caminho legado (<see cref="CompletionSession"/>) permanece apenas para a IA automática,
    /// que é opt-in desligado por padrão.
    /// </summary>
    private InlineCompletionCoordinator _inlineCoordinator = new();
    private InlineCompletionSuggestion? _inlineSuggestion;
    /// <summary>Composição de IME publicada pelo cliente de texto do editor.</summary>
    internal bool ImeComposing { get; private set; }
    private WorkspaceTabViewModel? _completionTab;
    private AutocompleteResult? _completion;
    private string? _completionOriginal;
    private int _completionCaret;
    private bool _attached;
    private bool _acceptingCompletion;
    private SnippetSession? _snippetSession;
    private readonly CompletionWindowPresenter _traditionalPresenter = new();
    private CancellationTokenSource? _traditionalDocumentationCancellation;
    private long _traditionalDocumentationGeneration;
    private string? _traditionalCompletionDocument;
    private bool _traditionalCompletionIncomplete;
    /// <summary>Contexto que produziu a lista exibida; usado para registrar o uso com a mesma chave do ranqueamento.</summary>
    private CompletionContext? _traditionalCompletionContext;

    private void InitializeAutocomplete()
    {
        CodeEditor.AddHandler(InputElement.KeyDownEvent, EditorKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        CodeEditor.PropertyChanged += EditorCompletionChanged;
        CodeEditor.ImeCompositionChanged += EditorImeCompositionChanged;
        CodeEditor.LayoutUpdated += (_, _) => PositionGhostText();
        CodeEditor.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => PositionGhostText());
        // A sessão de snippet sobrevive de propósito tanto a InvalidateCompletion quanto à perda de foco: aceitar um
        // item da lista move o foco enquanto as posições estão sendo instaladas, e abrir a lista dentro de um snippet
        // não pode encerrá-lo. O que ela jamais pode fazer é reivindicar Escape de um editor que o cursor já deixou —
        // DismissCompletion impõe isso com uma checagem explícita de foco, e UnbindCompletionTab descarta a sessão
        // quando a aba se vai.
        CodeEditor.LostFocus += (_, _) =>
        {
            if (ImeComposing) EditorImeCompositionChanged(CodeEditor, false);
            InvalidateCompletion();
        };
        DataContextChanged += (_, _) => BindCompletionTab();
        AttachedToVisualTree += (_, _) =>
        {
            _completionSession.Dispose(); _completionSession = new();
            _inlineCoordinator.Dispose(); _inlineCoordinator = new();
            _attached = true; BindCompletionTab();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _attached = false;
            if (ImeComposing) EditorImeCompositionChanged(CodeEditor, false);
            InvalidateCompletion(InlineCompletionCancelReason.Closed);
            _completionSession.Dispose(); _inlineCoordinator.Dispose(); UnbindCompletionTab();
        };
    }

    private void EditorImeCompositionChanged(object? sender, bool composing)
    {
        ImeComposing = composing;
        if (composing)
        {
            // Uma composição pode atualizar o preedit sem alterar o documento. Invalide imediatamente para que um
            // ghost anterior não cubra os candidatos do IME; a confirmação do texto será tratada como edição normal.
            InvalidateCompletion(InlineCompletionCancelReason.Typing);
        }
    }

    private void BindCompletionTab()
    {
        InvalidateCompletion(); UnbindCompletionTab();
        if (!_attached || DataContext is not WorkspaceTabViewModel tab) return;
        _completionTab = tab;
        tab.PropertyChanged += CompletionContextChanged;
        tab.TraditionalCompletionRefreshRequested += TraditionalCompletionRefreshRequested;
        tab.Autocomplete.SettingsChanged += CompletionSettingsChanged;
        UpdateCompletionShortcutTexts(tab);
    }

    /// <summary>
    /// Deriva o texto da interface dos gestos efetivos da aba, e não de um padrão fixo: um reatalho (ou uma sessão que
    /// desvincula o comando por completo) nunca pode deixar um rótulo descrevendo um atalho que não faz mais nada.
    /// </summary>
    private void UpdateCompletionShortcutTexts(WorkspaceTabViewModel tab)
    {
        var show = tab.GestureText(EditorCommandIds.CompletionShow);
        ShowTraditionalCompletionButton.Content = show.Length > 0
            ? LocalizationViewModel.Current.Format("suggestionsWithShortcut", show)
            : LocalizationViewModel.Current.Resolve("suggestionsEllipsis");
        var accept = tab.GestureText(EditorCommandIds.InlineAccept);
        var dismiss = tab.GestureText(EditorCommandIds.InlineDismiss);
        Avalonia.Automation.AutomationProperties.SetName(CompletionText,
            accept.Length > 0 && dismiss.Length > 0
                ? LocalizationViewModel.Current.Format("suggestionShortcutHint", accept, dismiss)
                : LocalizationViewModel.Current.Resolve("suggestionAccessible"));
    }

    private void UnbindCompletionTab()
    {
        _snippetSession = null;
        if (_completionTab is not { } tab) return;
        tab.PropertyChanged -= CompletionContextChanged;
        tab.TraditionalCompletionRefreshRequested -= TraditionalCompletionRefreshRequested;
        tab.Autocomplete.SettingsChanged -= CompletionSettingsChanged;
        _completionTab = null;
    }

    private void CompletionSettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => InvalidateCompletion(InlineCompletionCancelReason.Disabled));
    private void TraditionalCompletionRefreshRequested(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_traditionalPresenter.IsOpen && DataContext == sender && CodeEditor.SelectionStart == CodeEditor.SelectionEnd)
            ShowTraditionalCompletionList(this, new Avalonia.Interactivity.RoutedEventArgs());
    });
    private void CompletionContextChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_acceptingCompletion) return;
        // Text tem vínculo bidirecional com CodeEditor.Text, então uma tecla real chega a este ouvinte (pelo vínculo)
        // e ao EditorCompletionChanged do próprio CodeEditor para a mesma edição, em qualquer ordem, conforme a ordem
        // de inscrição. Com a lista aberta e a seleção vazia, quem já é dono dessa decisão é o EditorCompletionChanged
        // (refiltrar, não invalidar tudo) — ceder a ele aqui, em vez de disputar com um InvalidateCompletion
        // incondicional, é o que faz o digitar-para-filtrar (HDL-01) sobreviver independentemente de qual ouvinte
        // rodar primeiro para a mesma mudança de propriedade.
        if (e.PropertyName == nameof(WorkspaceTabViewModel.Text)
            && _traditionalPresenter.IsOpen && CodeEditor.SelectionStart == CodeEditor.SelectionEnd)
            return;
        if (e.PropertyName is nameof(WorkspaceTabViewModel.Text) or nameof(WorkspaceTabViewModel.Profile)
            or nameof(WorkspaceTabViewModel.Database) or nameof(WorkspaceTabViewModel.Collection) or nameof(WorkspaceTabViewModel.Mode)
            or nameof(WorkspaceTabViewModel.InputJson) or nameof(WorkspaceTabViewModel.Results))
            InvalidateCompletion(InlineCompletionCancelReason.DocumentChanged);
    }

    private void InvalidateCompletion() => InvalidateCompletion(InlineCompletionCancelReason.Superseded);

    private void InvalidateCompletion(InlineCompletionCancelReason reason)
    {
        _completionSession.Invalidate();
        _inlineCoordinator.Cancel(reason);
        // Pertence à própria aba (EditorRequestScope), nunca um CTS cru compartilhado entre visões ou abas.
        _completionTab?.CancelTraditionalCompletion();
        // Mesma disciplina para a IA explícita: qualquer edição, movimento de cursor, troca de aba ou perda de foco
        // que invalide o ghost também interrompe a geração em andamento e descarta a prévia — um resultado obsoleto
        // nunca chega à tela.
        CancelAiCompletion();
        _completion = null; _completionOriginal = null; _inlineSuggestion = null;
        CompletionPanel.IsVisible = false;
        CloseTraditionalCompletion();
    }

    /// <summary>
    /// Marcado imediatamente antes de retornar do ramo "lista aberta, refiltrar" abaixo, e consumido pela próxima
    /// notificação de cursor/seleção. Uma tecla real que filtra a lista sempre levanta Text antes de CaretIndex
    /// (verificado: o AvaloniaEdit atualiza o documento e só então a mudança de propriedade do cursor, levantada
    /// separadamente, o alcança) — logo aquela notificação seguinte é o avanço de cursor da própria edição, não uma
    /// navegação de verdade. Sem isso, a lista recém-produzida por este refiltro seria invalidada (e fechada) na hora,
    /// derrotando em silêncio o digitar-para-filtrar (HDL-01) a cada tecla.
    /// </summary>
    private bool _suppressNextCaretInvalidateAfterRefilter;

    private void EditorCompletionChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (_acceptingCompletion) return;
        var isCaretOrSelection = e.Property == MongoTextEditor.CaretIndexProperty
            || e.Property == MongoTextEditor.SelectionStartProperty || e.Property == MongoTextEditor.SelectionEndProperty;
        if (e.Property != MongoTextEditor.TextProperty && !isCaretOrSelection) return;
        _snippetSession = null;
        if (isCaretOrSelection)
        {
            if (_suppressNextCaretInvalidateAfterRefilter) { _suppressNextCaretInvalidateAfterRefilter = false; return; }
            // Mover o cursor ou mudar a seleção nunca agenda sugestão nova; apenas invalida o que estava à mostra
            // (ghost, lista ou snippet). Só uma edição de texto, abaixo, pode abrir ou refiltrar alguma coisa.
            InvalidateCompletion(e.Property == MongoTextEditor.CaretIndexProperty
                ? InlineCompletionCancelReason.CaretMoved : InlineCompletionCancelReason.SelectionChanged);
            return;
        }
        if (_traditionalPresenter.IsOpen && CodeEditor.SelectionStart == CodeEditor.SelectionEnd)
        {
            _traditionalPresenter.SetFilter(CurrentCompletionPrefix());
            RefreshTraditionalCompletionList();
            _suppressNextCaretInvalidateAfterRefilter = true;
            return;
        }
        // Aditivo e opt-in: só um caractere de gatilho (nunca toda tecla) pode abrir a lista sozinho, e apenas com
        // CompletionTrigger.TriggerCharacter, que o motor mantém em MetadataAccess.Peek — nenhuma consulta ao digitar.
        // Conferido no próximo ciclo do despachante (ScheduleTraditionalTriggerCheck), não aqui: a cascata de mudanças
        // de propriedade desta mesma tecla (CaretIndex/CaretOffset, o tab.Text de vínculo bidirecional) não
        // necessariamente assentou enquanto esta notificação de Text roda, e o caminho normal do ghost, abaixo, ainda
        // precisa rodar intacto para todo caractere comum.
        if (!_traditionalPresenter.IsOpen && CodeEditor.SelectionStart == CodeEditor.SelectionEnd
            && _attached && CodeEditor.IsKeyboardFocusWithin && DataContext is WorkspaceTabViewModel triggerTab
            && triggerTab.Autocomplete.Settings.CompletionAutoOpenOnTrigger)
            ScheduleTraditionalTriggerCheck(triggerTab);
        InvalidateCompletion(InlineCompletionCancelReason.Typing);
        // Adiado um ciclo do despachante, igual ao ScheduleTraditionalTriggerCheck acima e pelo mesmo motivo: a
        // cascata de mudanças de CaretIndex/Selection desta mesma tecla não necessariamente assentou enquanto esta
        // notificação de Text ainda roda. Ler CodeEditor.CaretIndex só depois que essa cascata escoou garante que o
        // cursor capturado corresponde à edição que produziu este texto, nunca a um valor que a mesma tecla ainda está
        // corrigindo (digitação real já mantém os dois coerentes, então não há latência observável aqui; isso só
        // importa quando Text e CaretIndex são atribuídos em passos separados).
        Dispatcher.UIThread.Post(RequestInlineCompletion);
    }

    /// <summary>
    /// Estado do editor capturado na thread de UI, antes de qualquer await. É o que decide se o automático pode
    /// sequer ser pedido: lista explícita aberta, snippet ativo, composição de IME, seleção ou foco perdido suspendem.
    /// </summary>
    private InlineCompletionEditorState CaptureInlineState() => new()
    {
        Attached = _attached,
        Focused = CodeEditor.IsKeyboardFocusWithin,
        CollapsedSelection = CodeEditor.SelectionStart == CodeEditor.SelectionEnd,
        ListOpen = _traditionalPresenter.IsOpen,
        SnippetActive = _snippetSession is not null,
        Composing = ImeComposing,
        // O painel da lista fica visível durante o "Carregando sugestões…", antes de o presenter abrir: um pedido
        // explícito já iniciado tem precedência e o automático não disputa com ele. A prévia da IA explícita entra
        // pela mesma porta — enquanto ela está na tela, o ghost sequer é pedido, e há sempre um só presenter ativo.
        ExplicitRequestPending = (TraditionalCompletionPanel.IsVisible && !_traditionalPresenter.IsOpen) || _aiPresenter.IsActive
    };

    /// <summary>Latest inline request, including publication or rejection of its captured result.</summary>
    public Task InlineCompletionTask { get; private set; } = Task.CompletedTask;

    private void RequestInlineCompletion() => InlineCompletionTask = RequestInlineCompletionAsync();

    private async Task RequestInlineCompletionAsync()
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        if (DataContext is not WorkspaceTabViewModel tab) return;
        var settings = tab.Autocomplete.Settings;
        var state = CaptureInlineState();
        if (!InlineCompletionPolicy.Allows(settings, state)) { _inlineCoordinator.Cancel(InlineCompletionCancelReason.Disabled); return; }
        var original = CodeEditor.Text ?? "";
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, original.Length);
        // Snapshot imutável do próprio documento do editor, capturado no mesmo instante que o texto: é ele que carrega
        // a linhagem de versões e permite ao cache de tokens reaproveitar a lexificação da tecla anterior. Um
        // StringTextSnapshot criado por tecla nunca casaria com a entrada anterior do cache.
        var snapshot = new Language.Text.AvaloniaTextSnapshot(CodeEditor.Document);
        var profile = tab.Profile; var database = tab.Database; var collection = tab.Collection; var mode = tab.Mode;
        try
        {
            var pending = _inlineCoordinator.RequestAsync(settings, state,
                InlineCompletionPolicy.TraditionalInline(settings)
                    ? token => ComputeTraditionalInlineCompletionAsync(tab, snapshot, caret, token)
                    : null,
                token => ComputeInlineCompletionAsync(tab, settings, original, caret, token));
            // O único trabalho sem debounce é o lookup determinístico local. Fallback lexical/IA continua atrás do
            // atraso configurado; todas as fontes seguem canceláveis e uma nova edição substitui a pendência.
            AutocompleteMetrics.UiDispatcherTime.Record(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                new KeyValuePair<string, object?>("handler", "inline"));
            var result = await pending;
            if (result is null || result.Text.Length == 0 || !_attached || !CodeEditor.IsKeyboardFocusWithin || DataContext != tab
                || CodeEditor.Text != original || CodeEditor.CaretIndex != caret || CodeEditor.SelectionStart != CodeEditor.SelectionEnd
                || _traditionalPresenter.IsOpen || _snippetSession is not null
                // Collection entra na revalidação porque compõe o CatalogScope: mudar de coleção durante o await muda
                // o conjunto de campos, e o resultado do escopo anterior não pertence mais a esta aba.
                || tab.Profile != profile || tab.Database != database || tab.Collection != collection || tab.Mode != mode) return;
            _completion = new AutocompleteResult(result.Text, result.IsAi, result.Description);
            _inlineSuggestion = result;
            _completionOriginal = original; _completionCaret = caret;
            CompletionPanel.IsVisible = true;
            PositionGhostText();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            var gesture = tab.GestureText(EditorCommandIds.CompletionShow);
            tab.Messages = gesture.Length > 0
                ? F("suggestionsUnavailableShortcut", gesture)
                : T("suggestionsUnavailable");
        }
    }

    /// <summary>
    /// Geração da sugestão automática, já depois do atraso e ainda na thread de UI (a captura de contexto acontece
    /// aqui; o trabalho pesado vai para o pool dentro de cada origem). Ordem fixa: determinístico contextual primeiro;
    /// se ele se abstém, as origens restantes permitidas pela política (dicionário lexical local e, só com opt-in
    /// explícito, inferência de IA) — sempre sob LoadedOnly, nunca carregando, trocando ou inicializando modelo.
    /// </summary>
    private async Task<InlineCompletionSuggestion?> ComputeInlineCompletionAsync(WorkspaceTabViewModel tab,
        AutocompleteSettings settings, string original, int caret, CancellationToken token)
    {
        var traditional = InlineCompletionPolicy.TraditionalInline(settings);
        token.ThrowIfCancellationRequested();
        // Origens do pedido decididas de uma vez pela política: o dicionário lexical só participa quando a origem
        // determinística está ligada (com InlineUseTraditional desligado ele não reaparece por atalho interno), e
        // MayLoadModel falso é a política LoadedOnly — quem a impõe é o dono do modelo, não este chamador, de modo que
        // digitar não carrega, não descarrega e não troca o modelo de nenhuma chave.
        var sources = new CompletionSourcePolicy(traditional && settings.UseDictionary,
            InlineCompletionPolicy.AiInline(settings), MayLoadModel: false);
        if (sources.None) return null;
        var request = tab.CaptureAutocompleteRequest(original, caret);
        // O atraso já foi aplicado pelo coordenador; o caminho legado não deve aplicá-lo de novo.
        var result = await _completionSession.RequestAsync(tab.Autocomplete, request, immediate: true, sources);
        return result is null ? null : new InlineCompletionSuggestion(result.Text, result.Description, "", null) { IsAi = result.IsAi };
    }

    private static Task<InlineCompletionSuggestion?> ComputeTraditionalInlineCompletionAsync(WorkspaceTabViewModel tab,
        Autocomplete.Core.Text.ITextSnapshot snapshot, int caret, CancellationToken token) =>
        tab.GetInlineCompletionAsync(snapshot, caret, token);

    private bool AcceptCompletion()
    {
        if (_completion is not { } completion || _completionOriginal is not { } original || DataContext is not WorkspaceTabViewModel tab
            || CodeEditor.Text != original || CodeEditor.CaretIndex != _completionCaret || !tab.Autocomplete.Settings.Enabled) return false;
        var caret = _completionCaret;
        var count = tab.Autocomplete.Settings.IncrementalTab ? IncrementalCompletion.NextLength(completion.Text) : completion.Text.Length;
        // Capturado antes de invalidar, que descarta a sugestão: o sinal de uso precisa da mesma chave do ranqueamento.
        var suggestion = _inlineSuggestion;
        InvalidateCompletion();
        _acceptingCompletion = true;
        try
        {
            CodeEditor.SelectionStart = CodeEditor.SelectionEnd = caret;
            CodeEditor.SelectedText = completion.Text[..count];
            CodeEditor.CaretIndex = caret + count;
            CodeEditor.SelectionStart = CodeEditor.SelectionEnd = CodeEditor.CaretIndex;
        }
        finally { _acceptingCompletion = false; }
        if (count < completion.Text.Length)
        {
            _completion = completion with { Text = completion.Text[count..] };
            _inlineSuggestion = suggestion is null ? null : suggestion with { Text = suggestion.Text[count..] };
            _completionOriginal = CodeEditor.Text; _completionCaret = caret + count;
            CompletionPanel.IsVisible = true;
            PositionGhostText();
            return true;
        }
        // Sugestão inteiramente aceita: registra o uso do símbolo determinístico (nunca valores, nunca texto de IA) e
        // deixa o desfazer imediato virar sinal negativo, exatamente como no aceite pela lista.
        if (suggestion is { Context: not null, SymbolId.Length: > 0 } accepted)
        {
            tab.RecordCompletionAccepted(accepted.Context, accepted.SymbolId);
            _lastAcceptedCompletion = (accepted.Context, accepted.SymbolId);
        }
        return true;
    }

    private void PositionGhostText()
    {
        if (!CompletionPanel.IsVisible || _completion is null || _completionOriginal is not { } original) return;
        var textView = CodeEditor.TextArea.TextView;
        CompletionPanel.Background = CodeEditor.Background;
        if (textView.TranslatePoint(default, GhostLayer) is { } viewportOrigin)
            GhostLayer.Clip = new RectangleGeometry(new Rect(viewportOrigin, textView.Bounds.Size));
        var caret = _completionCaret;
        var lineRange = CodeEditor.VisibleLineRange(caret);
        var lineStart = lineRange.Start;
        var suffixEnd = CodeEditor.Document.GetLineByOffset(caret).Length > SyntaxHighlightingOptions.LongLineThreshold
            ? lineRange.End : Math.Min(original.Length, caret + 32768);
        if (textView.TranslatePoint(CodeEditor.PositionInTextView(lineStart), GhostLayer) is not { } point) return;
        Canvas.SetLeft(CompletionPanel, point.X); Canvas.SetTop(CompletionPanel, point.Y);
        CompletionPanel.Width = Math.Max(0, GhostLayer.Bounds.Width - point.X - 16);
        CompletionPanel.Height = Math.Max(0, GhostLayer.Bounds.Height - point.Y - 16);
        if (textView.TranslatePoint(CodeEditor.PositionInTextView(caret), GhostLayer) is { } caretPoint)
        {
            Canvas.SetLeft(GhostCaret, caretPoint.X); Canvas.SetTop(GhostCaret, caretPoint.Y);
        }
        CompletionText.Show(original[lineStart..caret], _completion.Text, original[caret..suffixEnd],
            SyntaxHighlighting.SyntaxStyles.Brush(CodeEditor, SyntaxTokenType.Default),
            SyntaxHighlighting.SyntaxStyles.Brush(CompletionText, SyntaxTokenType.GhostText),
            CodeEditor.Snapshot is { } snapshot && snapshot.Text == original ? snapshot : null, lineStart);
    }

    /// <summary>
    /// Resolve o Escape pela mesma precedência Lista &gt; Snippet &gt; Inline de <see cref="EditorKeyDown"/>, mas
    /// pode ser chamado direto de <c>MainWindow.OnWorkspaceKeyDown</c>: aquele manipulador roda no registro Tunnel da
    /// própria janela, que dispara antes de o <c>EditorKeyDown</c> desta visão (registrado direto no <c>CodeEditor</c>)
    /// sequer ver o evento. Sem isto, o Escape global da janela (cancelar execução) ou passaria à frente da arbitragem
    /// do editor ou teria de fixar "fecha lista e ghost juntos", ignorando um reatalho e a distinção entre
    /// <see cref="EditorCommandIds.CompletionClose"/>, <see cref="EditorCommandIds.SnippetCancel"/> e
    /// <see cref="EditorCommandIds.InlineDismiss"/>. Devolve falso quando nenhum dos três estados reivindica o Escape
    /// (inclusive quando uma sessão o desvincula), de modo que o atalho global de cancelar execução continua valendo.
    /// </summary>
    public bool DismissCompletion()
    {
        if (DataContext is not WorkspaceTabViewModel tab) return false;
        var keyEvent = new EditorKeyEvent(EditorKeyModifiers.None, null, EditorKey.Escape);
        var dispatcher = tab.Commands;
        if (_traditionalPresenter.IsOpen && dispatcher.Match(keyEvent, EditorCommandScope.List) == EditorCommandIds.CompletionClose)
        {
            CloseTraditionalCompletion();
            return true;
        }
        // Exige foco: DismissCompletion é chamado pelo manipulador de túnel da janela, que não confere onde está o
        // cursor. Sem isto, uma sessão de snippet de um editor sem foco consumiria o Escape global.
        if (_snippetSession is not null && CodeEditor.IsKeyboardFocusWithin
            && dispatcher.Match(keyEvent, EditorCommandScope.Snippet) == EditorCommandIds.SnippetCancel)
        {
            _snippetSession = null;
            return true;
        }
        // A geração explícita é cancelada pelo Escape antes do ghost, pelo mesmo motivo de ordem do bloco acima: as
        // duas ocupam o escopo Inline e nunca estão ativas juntas.
        if (_aiPresenter.IsActive && dispatcher.Match(keyEvent, EditorCommandScope.Inline) == EditorCommandIds.InlineDismiss)
        {
            CancelAiCompletion();
            return true;
        }
        if (CompletionPanel.IsVisible && dispatcher.Match(keyEvent, EditorCommandScope.Inline) == EditorCommandIds.InlineDismiss)
        {
            InvalidateCompletion();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Um modificador pressionado sozinho nunca carrega comando: sair aqui, antes de <see cref="ToEditorKeyEvent"/> e
    /// de qualquer <see cref="EditorCommandDispatcher.Match"/>, é a garantia de custo quase zero que o desenho exige —
    /// o próprio <c>EditorKeyEvent.HasTrigger</c> do Core já recusaria o casamento, mas construir o evento custa.
    /// </summary>
    private static readonly HashSet<Key> ModifierOnlyKeys =
    [
        Key.LeftCtrl, Key.RightCtrl, Key.LeftShift, Key.RightShift, Key.LeftAlt, Key.RightAlt, Key.LWin, Key.RWin
    ];

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (ModifierOnlyKeys.Contains(e.Key)) return;
        // Desfazer logo após aceitar é arrependimento, e o aceite é um único passo de desfazer porque a inserção roda
        // dentro de um só RunUpdate. O rastreador é quem decide se veio dentro da janela; fora dela a chamada é inerte.
        if (e.Key == Key.Z && e.KeyModifiers == KeyModifiers.Control) RecordCompletionUndoneIfPending();
        if (DataContext is not WorkspaceTabViewModel tab) return;
        var keyEvent = ToEditorKeyEvent(e);
        if (!keyEvent.HasTrigger) return;
        var dispatcher = tab.Commands;

        // Precedência obrigatória: lista aberta > sessão de snippet > ghost visível > global. Cada bloco só é
        // consultado quando o respectivo estado está ativo; um comando que não casa dentro do bloco simplesmente
        // deixa a tecla cair para o próximo estado, exatamente como o Ctrl+Espaço reabre/recalcula a lista mesmo com
        // ela já aberta hoje.
        if (_traditionalPresenter.IsOpen)
        {
            var command = dispatcher.Match(keyEvent, EditorCommandScope.List);
            if (command == EditorCommandIds.CompletionNext) { _traditionalPresenter.Move(1); RefreshTraditionalCompletionList(); e.Handled = true; return; }
            if (command == EditorCommandIds.CompletionPrevious) { _traditionalPresenter.Move(-1); RefreshTraditionalCompletionList(); e.Handled = true; return; }
            if (command == EditorCommandIds.CompletionAccept && AcceptTraditionalCompletion()) { e.Handled = true; return; }
            if (command == EditorCommandIds.CompletionAcceptEnter)
            {
                if (tab.Autocomplete.Settings.CompletionEnterAccepts != false && AcceptTraditionalCompletion()) e.Handled = true;
                else CloseTraditionalCompletion();
                return;
            }
            if (command == EditorCommandIds.CompletionClose) { CloseTraditionalCompletion(); e.Handled = true; return; }
        }
        if (_snippetSession is not null)
        {
            var command = dispatcher.Match(keyEvent, EditorCommandScope.Snippet);
            if (command == EditorCommandIds.SnippetNext && MoveSnippetPlaceholder(reverse: false)) { e.Handled = true; return; }
            if (command == EditorCommandIds.SnippetPrevious && MoveSnippetPlaceholder(reverse: true)) { e.Handled = true; return; }
            if (command == EditorCommandIds.SnippetCancel) { _snippetSession = null; e.Handled = true; return; }
        }
        // A prévia da IA explícita ocupa o mesmo escopo Inline do ghost (AC-18: Ctrl+; é uma prévia inline) e as duas
        // nunca estão ativas ao mesmo tempo, então não há ambiguidade: Tab insere a prévia, Esc cancela a geração.
        if (_aiPresenter.IsActive)
        {
            var command = dispatcher.Match(keyEvent, EditorCommandScope.Inline);
            if (command == EditorCommandIds.InlineAccept && AcceptAiCompletion()) { e.Handled = true; return; }
            if (command == EditorCommandIds.InlineDismiss) { CancelAiCompletion(); e.Handled = true; return; }
        }
        if (CompletionPanel.IsVisible)
        {
            var command = dispatcher.Match(keyEvent, EditorCommandScope.Inline);
            if (command == EditorCommandIds.InlineAccept && AcceptCompletion()) { e.Handled = true; return; }
            if (command == EditorCommandIds.InlineDismiss) { InvalidateCompletion(); e.Handled = true; return; }
        }
        var global = dispatcher.Match(keyEvent, EditorCommandScope.Global);
        if (global == EditorCommandIds.CompletionShow) { e.Handled = true; ShowTraditionalCompletionList(sender, e); return; }
        if (global == EditorCommandIds.CompletionAi)
        {
            // Nunca insere o caractere do gesto e nunca executa consulta alguma: pede a geração e espera confirmação.
            e.Handled = true;
            StartAiCompletion(tab);
        }
    }

    private static EditorKeyEvent ToEditorKeyEvent(KeyEventArgs e)
    {
        var modifiers = (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? EditorKeyModifiers.Control : EditorKeyModifiers.None)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? EditorKeyModifiers.Shift : EditorKeyModifiers.None)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? EditorKeyModifiers.Alt : EditorKeyModifiers.None)
            | (e.KeyModifiers.HasFlag(KeyModifiers.Meta) ? EditorKeyModifiers.Meta : EditorKeyModifiers.None);
        // "Nenhum símbolo produzido" e "símbolo produzido que nenhum gesto poderia vincular" são fatos diferentes e
        // precisam continuar diferentes: só o primeiro pode recorrer ao símbolo físico QWERTY. Com Ctrl pressionado,
        // várias plataformas entregam um caractere de controle no lugar de um real, e é esse o caso que significa
        // "nada foi produzido". Um caractere imprimível que o layout realmente produziu passa adiante mesmo que nenhum
        // gesto pudesse vinculá-lo, porque aí o despachante precisa recusar o casamento em vez de buscar a tecla
        // norte-americana embaixo: no ABNT2 a tecla de cedilha fica onde o US tem ";", então descartar o caractere que
        // ela produz faria Ctrl+Ç invocar o comando de Ctrl+;.
        char? symbol = e.KeySymbol is { Length: 1 } text && !char.IsControl(text[0]) ? text[0] : null;
        var qwertyKey = e.PhysicalKey.ToQwertyKey();
        // O enum Key do Avalonia declara "Return" e "Enter" como apelidos do mesmo valor; ToString() sempre devolve
        // "Return", que o EditorKey (chamado "Enter") nunca interpreta por nome. Sem esta alternativa, nenhum gesto
        // vinculado à tecla física Enter (o padrão CompletionAcceptEnter, por exemplo) casaria por PhysicalKey.
        EditorKey? physical = Enum.TryParse<EditorKey>(qwertyKey.ToString(), ignoreCase: true, out var parsed) ? parsed
            : qwertyKey == Key.Enter ? EditorKey.Enter : null;
        var qwertySymbol = e.PhysicalKey.ToQwertyKeySymbol(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        char? physicalSymbol = qwertySymbol is { Length: 1 } physicalText && !char.IsControl(physicalText[0]) ? physicalText[0] : null;
        return new EditorKeyEvent(modifiers, symbol, physical, physicalSymbol);
    }
}
