namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>Builds readable, structured documentation on demand from the item reference already captured for the request.</summary>
public static class CompletionDocumentationResolver
{
    public static CompletionDocumentation Resolve(CompletionItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        cancellationToken.ThrowIfCancellationRequested();
        var reference = item.Documentation;
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(item.LabelDetail)) lines.Add(item.LabelDetail!);
        if (!string.IsNullOrWhiteSpace(reference?.Category)) lines.Add("Categoria: " + reference.Category);
        if (!string.IsNullOrWhiteSpace(reference?.ValueShape)) lines.Add("Forma esperada: " + reference.ValueShape);
        if (reference?.Parameters is { Count: > 0 }) lines.Add("Parâmetros: " + string.Join(", ", reference.Parameters));
        if (!string.IsNullOrWhiteSpace(reference?.Returns)) lines.Add("Retorna: " + reference.Returns);
        if (!string.IsNullOrWhiteSpace(reference?.Since)) lines.Add("Disponível desde: " + reference.Since);
        if (reference?.Evidence is { } evidence && evidence != EvidenceSources.None) lines.Add("Evidência: " + EvidenceText(evidence));
        if (item.Tags.HasFlag(CompletionItemTags.Write)) lines.Add("Operação de escrita: inserir não executa; a confirmação do runtime continua obrigatória.");
        if (item.Tags.HasFlag(CompletionItemTags.Deprecated)) lines.Add("Obsoleto: prefira a alternativa recomendada pela sua versão do MongoDB.");
        if (item.Tags.HasFlag(CompletionItemTags.Stale)) lines.Add("Metadados em atualização: o item pode refletir um estado anterior.");
        cancellationToken.ThrowIfCancellationRequested();
        return new(item.Label, lines.Count == 0 ? "Sem detalhes adicionais." : string.Join(Environment.NewLine, lines), item.Kind, item.Tags);
    }

    private static string EvidenceText(EvidenceSources evidence)
    {
        var labels = new List<string>();
        if (evidence.HasFlag(EvidenceSources.Validator)) labels.Add("validador");
        if (evidence.HasFlag(EvidenceSources.Index)) labels.Add("índice");
        if (evidence.HasFlag(EvidenceSources.Results)) labels.Add("resultados locais");
        if (evidence.HasFlag(EvidenceSources.Sample)) labels.Add("amostra de schema");
        if (evidence.HasFlag(EvidenceSources.History)) labels.Add("histórico local");
        if (evidence.HasFlag(EvidenceSources.Pipeline)) labels.Add("estágios do pipeline");
        return string.Join(", ", labels);
    }
}
