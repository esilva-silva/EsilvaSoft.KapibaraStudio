using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <param name="Items">Sugestões já filtradas e ordenadas pelo ranqueamento determinístico.</param>
/// <param name="IsIncomplete">Verdadeiro quando metadados ainda carregavam e a lista pode crescer.</param>
/// <param name="Context">
/// Contexto que produziu esta lista. É devolvido junto para que o aceite registre o uso com exatamente a mesma chave
/// usada no ranqueamento; nulo quando a aba não tem provedor e nada foi analisado.
/// </param>
public sealed record TraditionalCompletionResult(IReadOnlyList<CompletionItem> Items, bool IsIncomplete,
    CompletionContext? Context = null);
