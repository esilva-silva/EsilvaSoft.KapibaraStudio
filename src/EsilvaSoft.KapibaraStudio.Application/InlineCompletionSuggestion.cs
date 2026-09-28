using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>
/// Sugestão automática pronta para exibição: inserção pura no cursor capturado, nunca uma substituição do que já foi
/// digitado. O primeiro ghost não corrige texto anterior; quando a correção é necessária, a sugestão simplesmente não
/// existe e a lista explícita é que a oferece.
/// </summary>
/// <param name="Text">Texto a inserir exatamente no cursor.</param>
/// <param name="Description">Detalhe curto do símbolo, sem valores de documentos.</param>
/// <param name="SymbolId">Identificador do símbolo, para registrar aceite/desfazer com a chave do ranqueamento.</param>
/// <param name="Context">Contexto que produziu a sugestão, devolvido junto para o registro de uso.</param>
public sealed record InlineCompletionSuggestion(string Text, string Description, string SymbolId, CompletionContext? Context)
{
    /// <summary>Verdadeiro apenas quando a origem foi inferência local de IA (opt-in explícito), falso no determinístico.</summary>
    public bool IsAi { get; init; }

    /// <summary>
    /// Projeta um item ranqueado como inserção no cursor, ou devolve <c>null</c> quando isso não é possível sem
    /// reescrever o texto já digitado: o intervalo de substituição precisa terminar no cursor, o prefixo precisa ser
    /// exatamente o texto imediatamente anterior a ele e o rótulo precisa continuar esse prefixo.
    /// </summary>
    public static InlineCompletionSuggestion? TryCreate(CompletionContext context, CompletionItem item, string text, int caret)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(text);
        var prefix = context.Prefix;
        if (caret < prefix.Length || caret > text.Length) return null;
        return TryCreate(context, item, text.AsSpan(caret - prefix.Length, prefix.Length), caret);
    }

    /// <summary>
    /// Mesma projeção a partir do snapshot que originou a análise. Lê apenas o trecho imediatamente anterior ao
    /// cursor: o caminho automático não materializa o documento inteiro a cada tecla.
    /// </summary>
    public static InlineCompletionSuggestion? TryCreate(CompletionContext context, CompletionItem item, ITextSnapshot snapshot, int caret)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(snapshot);
        var prefix = context.Prefix;
        if (caret < prefix.Length || caret > snapshot.Length) return null;
        return TryCreate(context, item, prefix.Length == 0 ? [] : snapshot.GetText(caret - prefix.Length, prefix.Length).AsSpan(), caret);
    }

    private static InlineCompletionSuggestion? TryCreate(CompletionContext context, CompletionItem item, ReadOnlySpan<char> beforeCaret, int caret)
    {
        var prefix = context.Prefix;
        if (context.ReplaceSpan.End != caret) return null;
        if (!beforeCaret.SequenceEqual(prefix)) return null;
        if (!item.FilterText.StartsWith(prefix, StringComparison.Ordinal) || item.FilterText.Length <= prefix.Length) return null;
        return new(item.FilterText[prefix.Length..], item.LabelDetail ?? "", item.SymbolId, context);
    }
}
