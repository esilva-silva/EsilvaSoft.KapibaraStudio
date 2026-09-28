namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>
/// Portão de confiança da sugestão automática determinística. O ranqueamento devolve um score ponderado, que
/// <em>não é probabilidade</em>: por isso a decisão de exibir não compara o score com um limiar absoluto, e sim exige
/// evidência estrutural de que existe uma única continuação possível — prefixo estrito, catálogo não truncado,
/// continuação única no conjunto elegível e margem mínima sobre o segundo colocado. Na dúvida, abstém-se.
/// </summary>
public sealed record InlineCompletionConfidence
{
    public static InlineCompletionConfidence Default { get; } = new();

    /// <summary>Sem prefixo digitado não há continuação inequívoca: a lista explícita é que oferece o conjunto amplo.</summary>
    public int MinimumPrefixLength { get; init; } = 1;

    /// <summary>Top-K inspecionado. Pequeno de propósito: o automático decide entre poucos, ou não decide.</summary>
    public int MaximumItems { get; init; } = 8;

    /// <summary>Teto de candidatos analisados antes do ranqueamento, conforme a política do modo automático.</summary>
    public int MaximumCandidates { get; init; } = 200;

    /// <summary>Margem relativa mínima entre top-1 e top-2; empate significa ambiguidade, e ambiguidade não vira ghost.</summary>
    public double MinimumMargin { get; init; } = .20;

    /// <summary>
    /// Escolhe o único candidato seguro para uma sugestão automática, ou <c>null</c> para abster-se. Toda abstenção é
    /// deliberada e tem motivo nomeado em <paramref name="reason"/>, que as métricas registram sem texto do editor.
    /// </summary>
    public CompletionItem? Select(string prefix, CompletionList list, out string reason)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(list);
        reason = "none";
        if (prefix.Length < MinimumPrefixLength) { reason = "short-prefix"; return null; }
        // Catálogo truncado/carregando não prova candidato único: a evidência que faltou pode ser justamente a que
        // desempataria. IsIncomplete cobre as três formas de corte — fonte incompleta, teto de candidatos analisados
        // e teto de itens ranqueados —, e qualquer uma delas basta para a abstenção.
        if (list.IsIncomplete) { reason = "incomplete-catalog"; return null; }
        if (list.Items.Count == 0) { reason = "empty"; return null; }

        var items = list.Items;
        CompletionItem? continuation = null;
        foreach (var item in items)
        {
            // Termo já completo: não há o que inserir, e insinuar uma continuação mudaria o símbolo digitado.
            if (string.Equals(item.FilterText, prefix, StringComparison.Ordinal)) { reason = "already-complete"; return null; }
            if (!IsStrictContinuation(item, prefix)) continue;
            if (continuation is not null) { reason = "ambiguous"; return null; }
            continuation = item;
        }
        if (continuation is null) { reason = "no-continuation"; return null; }
        // A única continuação estrita precisa ser também a mais bem ranqueada; se algo melhor ranqueado não é
        // continuação (outro casamento, outra caixa), o que o automático mostraria não é o melhor candidato.
        if (!ReferenceEquals(continuation, items[0])) { reason = "not-top"; return null; }
        if (items.Count > 1 && !HasMargin(items[0].Score, items[1].Score)) { reason = "margin"; return null; }
        return continuation;
    }

    public CompletionItem? Select(string prefix, CompletionList list) => Select(prefix, list, out _);

    private bool HasMargin(double first, double second)
    {
        var scale = Math.Max(Math.Abs(first), 1);
        return (first - second) / scale >= MinimumMargin;
    }

    /// <summary>
    /// Continuação pura no cursor: mesmo texto já digitado, caixa incluída, mais pelo menos um caractere. Snippets com
    /// posições a preencher pertencem à lista explícita, e um item cujo texto de edição difere do rótulo não pode ser
    /// projetado como inserção simples.
    /// </summary>
    private static bool IsStrictContinuation(CompletionItem item, string prefix) =>
        !item.Edit.IsSnippet && item.Kind != CompletionItemKind.Snippet
        && string.Equals(item.Edit.NewText, item.FilterText, StringComparison.Ordinal)
        && item.FilterText.Length > prefix.Length
        && item.FilterText.StartsWith(prefix, StringComparison.Ordinal);
}
