using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using EsilvaSoft.KapibaraStudio.Application;
using Avalonia.VisualTree;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

namespace EsilvaSoft.KapibaraStudio.Desktop;

/// <summary>
/// IA explícita (<c>Ctrl+;</c>): indicador de geração, prévia inline progressiva multilinha, aceite em uma única
/// edição e fallback para a lista tradicional com o motivo.
/// </summary>
/// <remarks>
/// <para><strong>Arbitragem.</strong> <c>Ctrl+;</c> é do escopo <c>Global</c> e segue a precedência já documentada
/// (Lista &gt; Snippet &gt; Inline &gt; Global): ele só é alcançado quando nenhum estado ativo reivindicou a tecla.
/// Ao ser alcançado, faz o que a arquitetura já decidiu — fecha a lista, substitui o ghost e assume a superfície.
/// Enquanto a prévia está ativa, o ghost automático não é sequer pedido (<c>CaptureInlineState</c>), de modo que
/// nunca há duas apresentações disputando a mesma região.</para>
/// <para><strong>Nenhum autoexecute.</strong> Nada aqui executa consulta, comando ou script. O resultado é texto
/// esperando <c>Tab</c>; sem confirmação explícita, ele desaparece sem tocar no documento.</para>
/// </remarks>
public partial class WorkspaceTabView
{
    private readonly AiCompletionPreviewPresenter _aiPresenter = new();
    /// <summary>
    /// Cancelamento do pedido explícito desta view. Pertence a esta instância e a nenhuma outra (mesma disciplina do
    /// <c>_traditionalDocumentationCancellation</c>): nunca é compartilhado entre abas nem entre modalidades.
    /// </summary>
    private CancellationTokenSource? _aiCancellation;

    /// <summary>
    /// Temporizador do atraso do indicador. Vive nesta instância, como o cancelamento, e é descartado junto com o
    /// pedido que o criou.
    /// </summary>
    private ITimer? _aiIndicatorTimer;

    /// <summary>O atraso já venceu e o indicador pode aparecer.</summary>
    private bool _aiIndicatorDue;

    /// <summary>
    /// Relógio do atraso do indicador. Injetável (por teste) exatamente para que um segundo de espera seja avançado
    /// em vez de dormido: um teste que esperasse tempo de parede seria lento e instável.
    /// </summary>
    private TimeProvider _aiClock = TimeProvider.System;

    /// <summary>
    /// Atraso antes de mostrar qualquer coisa ("Tempos" de ai-autocomplete.md). Uma geração que termina antes disso
    /// não pisca indicador nenhum: o usuário vê a sugestão aparecer, e não um painel que se abre e se fecha.
    /// </summary>
    private static readonly TimeSpan AiCompletionIndicatorDelay = TimeSpan.FromSeconds(1);

    /// <summary>Texto do indicador enquanto o modelo ainda não entregou nada.</summary>
    private static string AiCompletionStartingText => T("aiCompletionStarting");
    /// <summary>Texto do indicador enquanto os pedaços chegam.</summary>
    private static string AiCompletionStreamingText => T("aiCompletionStreaming");
    /// <summary>
    /// Texto do indicador enquanto o que está acontecendo é a carga do modelo. Distinto de propósito: esperar por
    /// uma carga de segundos e esperar por tokens são situações diferentes, e o <c>Esc</c> aqui abandona a espera
    /// sem abortar a carga — ela é do serviço de modelo e serve ao próximo pedido.
    /// </summary>
    private static string AiCompletionLoadingText => T("aiCompletionLoading");

    /// <summary>
    /// Cancela a geração em andamento e apaga indicador e prévia. Cancelar o token abandona o
    /// <c>IAsyncEnumerable</c>, o que encerra a sessão nativa do runtime (DEC-R42-STREAMASYNC); o
    /// <see cref="AiCompletionPreviewPresenter.Reset"/> invalida a identidade do pedido, de modo que um provedor que
    /// ignore o cancelamento continue gerando para ninguém.
    /// </summary>
    private void CancelAiCompletion()
    {
        if (_aiCancellation is { } cancellation)
        {
            _aiCancellation = null;
            cancellation.Cancel();
        }
        _aiIndicatorTimer?.Dispose();
        _aiIndicatorTimer = null;
        _aiIndicatorDue = false;
        if (!_aiPresenter.IsActive) return;
        _aiPresenter.Reset();
        AiCompletionPanel.IsVisible = false;
        AiCompletionPreview.Text = "";
    }

    /// <summary>
    /// Vencido o atraso, o indicador pode aparecer — se o pedido que o agendou ainda for o corrente. Um pedido já
    /// substituído ou cancelado não reabre painel nenhum.
    /// </summary>
    private void OnAiCompletionIndicatorDue(long generation)
    {
        if (!_aiPresenter.IsCurrent(generation) || DataContext is not WorkspaceTabViewModel tab) return;
        _aiIndicatorDue = true;
        ShowAiCompletionPanel(tab);
    }

    /// <summary>
    /// Ponto de entrada do comando <see cref="EditorCommandIds.CompletionAi"/>. Captura texto e cursor antes de
    /// qualquer await e nunca deixa o atalho inserir caractere algum.
    /// </summary>
    private async void StartAiCompletion(WorkspaceTabViewModel tab)
    {
        // Ctrl+; fecha a lista e substitui o ghost: é a arbitragem já registrada em architecture.md. Também cancela
        // um pedido explícito anterior desta mesma view — um Ctrl+; substitui o outro, nunca convive com ele.
        InvalidateCompletion();
        if (CodeEditor.SelectionStart != CodeEditor.SelectionEnd) return;
        var settings = tab.Autocomplete.Settings;
        if (!settings.Enabled || settings.Mode == AutocompleteMode.Basic)
        {
            FallbackToTraditionalList(AiCompletionFallbackMessages.Disabled);
            return;
        }
        if (tab.AiCompletion is null)
        {
            FallbackToTraditionalList(AiCompletionFallbackMessages.NotConfigured);
            return;
        }
        var document = CodeEditor.Text ?? "";
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, document.Length);
        var cancellation = new CancellationTokenSource();
        var generation = _aiPresenter.Begin(document, caret);
        _aiCancellation = cancellation;
        _aiIndicatorDue = false;
        _aiIndicatorTimer = _aiClock.CreateTimer(
            _ => Dispatcher.UIThread.Post(() => OnAiCompletionIndicatorDue(generation)),
            null, AiCompletionIndicatorDelay, Timeout.InfiniteTimeSpan);
        ShowAiCompletionPanel(tab);
        try
        {
            // A captura do contexto é síncrona e acontece aqui, antes do primeiro MoveNextAsync; o fluxo é preguiçoso.
            var stream = tab.RequestAiCompletionAsync(document, caret, cancellation.Token)!;
            await foreach (var update in stream.WithCancellation(cancellation.Token))
            {
                // Sair do await foreach aqui é o que descarta um resultado obsoleto: a enumeração é abandonada, o
                // runtime é interrompido e nada do que já chegou chega à tela.
                if (!IsAiCompletionCurrent(tab, generation, document, caret)) return;
                if (update.IsLoading)
                {
                    if (_aiPresenter.MarkLoading(generation)) ShowAiCompletionPanel(tab);
                    continue;
                }
                if (!update.IsFinal)
                {
                    if (_aiPresenter.Update(generation, update.Text)) ShowAiCompletionPanel(tab);
                    continue;
                }
                if (update.Success && update.Candidate is { Text.Length: > 0 } candidate)
                {
                    if (_aiPresenter.Complete(generation, candidate.Text)) ShowAiCompletionPanel(tab);
                    return;
                }
                if (!_aiPresenter.IsCurrent(generation)) return;
                CancelAiCompletion();
                // Preempção por chat ou pelo teste de modelo é descarte silencioso: o indicador some como se o
                // usuário tivesse desistido, sem mensagem e sem lista. A distinção existe no motivo tipado, para
                // quem lê diagnóstico, e não na tela.
                if (update.Failure == AiCompletionFailure.Preempted) return;
                FallbackToTraditionalList(AiCompletionFallbackMessages.Describe(update, DateTimeOffset.UtcNow));
                return;
            }
            // Fluxo encerrado sem atualização final: nada a exibir e nada a inserir.
            if (_aiPresenter.IsCurrent(generation)) CancelAiCompletion();
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!_aiPresenter.IsCurrent(generation)) return;
            CancelAiCompletion();
            FallbackToTraditionalList(AiCompletionFallbackMessages.Failed);
        }
        finally
        {
            if (ReferenceEquals(_aiCancellation, cancellation)) _aiCancellation = null;
            cancellation.Dispose();
        }
    }

    /// <summary>
    /// O pedido ainda pertence a este editor, a esta aba e a este documento. Reusa a mesma disciplina de revalidação
    /// do ghost automático: qualquer edição, movimento de cursor, troca de aba ou perda de foco descarta o que
    /// estava a caminho.
    /// </summary>
    private bool IsAiCompletionCurrent(WorkspaceTabViewModel tab, long generation, string document, int caret) =>
        _aiPresenter.IsCurrent(generation) && _attached && DataContext == tab && CodeEditor.IsKeyboardFocusWithin
        && string.Equals(CodeEditor.Text ?? "", document, StringComparison.Ordinal) && CodeEditor.CaretIndex == caret
        && CodeEditor.SelectionStart == CodeEditor.SelectionEnd;

    /// <summary>Indicador e prévia, posicionados no cursor; nunca toca o documento.</summary>
    private void ShowAiCompletionPanel(WorkspaceTabViewModel tab)
    {
        // Nada é desenhado no primeiro segundo, a não ser que o resultado já esteja pronto: é assim que uma geração
        // rápida não mostra "gerando…" nenhum. Passado o atraso, o painel acompanha a geração até o fim.
        if (!_aiIndicatorDue && _aiPresenter.State != AiCompletionPreviewState.Preview)
        {
            AiCompletionPanel.IsVisible = false;
            return;
        }
        AiCompletionPanel.IsVisible = true;
        AiCompletionPreview.Text = _aiPresenter.Text;
        // Sem nenhum texto ainda, o indicador é uma linha só: um painel alto e vazio esperando o primeiro token é
        // exatamente o "ruído" que a seção de riscos da fase manda evitar.
        AiCompletionPreview.IsVisible = _aiPresenter.Text.Length > 0;
        AiCompletionIndicator.IsVisible = _aiPresenter.State is AiCompletionPreviewState.Generating or AiCompletionPreviewState.Loading;
        AiCompletionStatus.Text = _aiPresenter.State switch
        {
            AiCompletionPreviewState.Loading => AiCompletionLoadingText,
            AiCompletionPreviewState.Generating => _aiPresenter.Text.Length == 0 ? AiCompletionStartingText : AiCompletionStreamingText,
            _ => AiCompletionPreviewShortcutsText(tab)
        };
        PositionAiCompletionPanel();
    }

    /// <summary>
    /// Rótulo derivado dos gestos efetivos da aba, como já se faz na lista tradicional: um reatalho nunca pode deixar
    /// a prévia anunciando uma tecla que não aceita mais nada.
    /// </summary>
    private static string AiCompletionPreviewShortcutsText(WorkspaceTabViewModel tab)
    {
        var accept = JoinGestures(tab.GestureText(EditorCommandIds.InlineAccept));
        var dismiss = JoinGestures(tab.GestureText(EditorCommandIds.InlineDismiss));
        return LocalizationViewModel.Current.Format("aiSuggestionShortcutHint", accept, dismiss);
    }

    private void PositionAiCompletionPanel()
    {
        var availableWidth = Math.Max(0, AiCompletionLayer.Bounds.Width);
        var panelWidth = Math.Min(400, availableWidth);
        AiCompletionPanel.Width = panelWidth > 0 ? panelWidth : 400;
        var caret = Math.Clamp(CodeEditor.CaretIndex, 0, CodeEditor.Document.TextLength);
        if (CodeEditor.TextArea.TextView.TranslatePoint(CodeEditor.PositionInTextView(caret), AiCompletionLayer) is not { } point) return;
        var effectiveWidth = AiCompletionPanel.Width > 0 ? AiCompletionPanel.Width : 400;
        var maxLeft = Math.Max(0, AiCompletionLayer.Bounds.Width - effectiveWidth);
        Canvas.SetLeft(AiCompletionPanel, Math.Clamp(point.X, 0, maxLeft));
        // Altura real do painel quando já houve uma passada de layout, limitada ao MaxHeight do XAML: a prévia costuma
        // ser bem mais baixa do que o teto, e usar o teto a empurraria para cima da própria linha que está sendo
        // editada sem necessidade.
        var panelHeight = AiCompletionPanel.Bounds.Height is > 0 and var measured ? Math.Min(180, measured) : 180;
        var below = point.Y + CodeEditor.LineHeight;
        var top = below + panelHeight <= AiCompletionLayer.Bounds.Height ? below : Math.Max(0, point.Y - panelHeight);
        Canvas.SetTop(AiCompletionPanel, top);
    }

    /// <summary>
    /// Aceita a prévia inteira como <strong>uma única</strong> operação de desfazer, a mesma disciplina do aceite
    /// pela lista tradicional (HDL-08): toda a inserção acontece dentro de um só <c>RunUpdate</c>, então um
    /// <c>Ctrl+Z</c> devolve o documento exatamente ao estado anterior. Não há aceite incremental aqui: o candidato
    /// explícito é multilinha e recortá-lo por palavra deixaria um fragmento sintaticamente quebrado.
    /// </summary>
    private bool AcceptAiCompletion()
    {
        if (!_aiPresenter.HasPreview || DataContext is not WorkspaceTabViewModel) return false;
        var text = _aiPresenter.Text;
        var caret = _aiPresenter.Caret;
        // O documento mudou desde a captura: a prévia não pertence mais a este texto e é descartada sem inserir nada.
        if (!string.Equals(CodeEditor.Text ?? "", _aiPresenter.Document, StringComparison.Ordinal)
            || caret > CodeEditor.Document.TextLength)
        {
            CancelAiCompletion();
            return false;
        }
        CancelAiCompletion();
        _acceptingCompletion = true;
        try
        {
            using (CodeEditor.Document.RunUpdate()) CodeEditor.Document.Insert(caret, text);
            CodeEditor.CaretIndex = caret + text.Length;
            CodeEditor.SelectionStart = CodeEditor.SelectionEnd = CodeEditor.CaretIndex;
        }
        finally { _acceptingCompletion = false; }
        return true;
    }

    /// <summary>
    /// Critério de aceite 1 da Fase 4: a recusa abre a <em>lista tradicional</em> — nunca um popup, nunca um diálogo
    /// — com uma linha de estado curta explicando o motivo.
    /// </summary>
    private void FallbackToTraditionalList(string reason)
    {
        // A lista tradicional explícita é uma preferência do usuário como qualquer outra. Se ele a desligou, a falha
        // da IA informa a indisponibilidade e para por aí: o fallback não reabre — nem "só desta vez" — uma
        // apresentação desligada, e não altera preferência nenhuma para conseguir mostrá-la.
        if (DataContext is WorkspaceTabViewModel tab && !tab.Autocomplete.Settings.TraditionalEnabled)
        {
            InvalidateCompletion();
            ShowTraditionalCompletionMessage($"{reason} · {AiCompletionFallbackMessages.TraditionalDisabled}");
            return;
        }
        ShowTraditionalCompletionList(this, new RoutedEventArgs(), Autocomplete.Core.Context.CompletionTrigger.Invoked, reason);
    }
}
