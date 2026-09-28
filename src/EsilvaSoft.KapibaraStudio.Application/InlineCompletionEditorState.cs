namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>
/// Estado do editor capturado na própria thread de UI, antes de qualquer await, para decidir se uma sugestão
/// automática pode sequer ser pedida. É um valor imutável: o coordenador nunca volta a consultar o editor.
/// </summary>
public readonly record struct InlineCompletionEditorState
{
    /// <summary>Editor anexado à árvore visual.</summary>
    public bool Attached { get; init; }
    /// <summary>Foco de teclado dentro do editor.</summary>
    public bool Focused { get; init; }
    /// <summary>Seleção colapsada (cursor simples); qualquer seleção suspende o automático.</summary>
    public bool CollapsedSelection { get; init; }
    /// <summary>Lista explícita aberta: o automático se abstém em vez de disputar a âncora.</summary>
    public bool ListOpen { get; init; }
    /// <summary>Sessão de snippet ativa, com posições a navegar por Tab.</summary>
    public bool SnippetActive { get; init; }
    /// <summary>Composição de IME em andamento: o texto ainda não é o que o usuário digitou.</summary>
    public bool Composing { get; init; }
    /// <summary>Pedido explícito (lista ou IA) já iniciado e ainda em voo.</summary>
    public bool ExplicitRequestPending { get; init; }

    /// <summary>Estado neutro de um editor pronto para receber uma sugestão automática.</summary>
    public static InlineCompletionEditorState Ready { get; } = new()
    {
        Attached = true,
        Focused = true,
        CollapsedSelection = true
    };

    public bool AllowsAutomaticSuggestion =>
        Attached && Focused && CollapsedSelection && !ListOpen && !SnippetActive && !Composing && !ExplicitRequestPending;
}
