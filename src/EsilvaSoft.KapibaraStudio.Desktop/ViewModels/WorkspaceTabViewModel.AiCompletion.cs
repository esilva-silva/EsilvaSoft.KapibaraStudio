using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    /// <summary>
    /// Provider da IA explícita (<c>Ctrl+;</c>), atribuído pela composição do workspace. Nulo em abas isoladas
    /// (design-time ou teste), caso em que o atalho cai direto no fallback da lista tradicional.
    /// </summary>
    /// <remarks>
    /// A camada Desktop nunca constrói o pipeline, o serviço de modelo nem o runtime: recebe a interface de
    /// <c>Application</c> e a consome. Prioridade, fila, cooldown e política de carga continuam sendo decisão de quem
    /// a implementa.
    /// </remarks>
    public IAiCompletionProvider? AiCompletion { get; set; }

    /// <summary>
    /// Pedido explícito à IA local a partir do estado capturado do editor. Devolve <see langword="null"/> quando esta
    /// aba não tem provider — a view mostra o fallback em vez de um fluxo vazio.
    /// </summary>
    /// <remarks>
    /// Todo o contexto (texto, cursor, destino, preferências, campos observados) é capturado aqui, de forma síncrona,
    /// antes de qualquer await: o fluxo devolvido é preguiçoso e só começa a gerar quando a view o enumera. Abandonar
    /// a enumeração (ou cancelar o token) interrompe a geração — é o que o <c>Esc</c> e uma nova edição fazem.
    /// </remarks>
    /// <param name="text">Texto completo do editor no instante do atalho.</param>
    /// <param name="caret">Cursor no instante do atalho.</param>
    /// <param name="token">Cancelamento cooperativo pertencente a esta view/aba.</param>
    public IAsyncEnumerable<AiCompletionUpdate>? RequestAiCompletionAsync(string text, int caret, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (AiCompletion is not { } provider) return null;
        var settings = Autocomplete.Settings;
        var request = new AiGenerationRequest(CaptureAiContextSnapshot(text, caret), settings);
        return provider.RequestAsync(request, token);
    }

    /// <summary>
    /// Mesma captura de contexto do autocomplete tradicional (<see cref="CaptureAutocompleteRequest"/>): destino,
    /// campos observados nos resultados, nomes conhecidos e comandos recentes desta conexão/banco. A diferença é o
    /// produto — aqui o pipeline de contexto da Fase 3 recebe o snapshot cru e decide o recorte sob orçamento.
    /// </summary>
    /// <param name="text">Texto completo do editor.</param>
    /// <param name="caret">Cursor.</param>
    public AutocompleteContextSnapshot CaptureAiContextSnapshot(string text, int caret)
    {
        ArgumentNullException.ThrowIfNull(text);
        var clamped = Math.Clamp(caret, 0, text.Length);
        var fields = GetObservedCompletionFields(text[..clamped]);
        var names = KnownAutocompleteNames().Concat([Profile?.Name ?? "", Database, Collection]).ToArray();
        var history = ConsoleHistory.Where(entry => entry.ProfileId == Profile?.Id && entry.Database == Database)
            .OrderByDescending(entry => entry.ExecutedAt).Take(3).Select(entry => entry.Script).ToArray();
        var language = Mode switch { "Agregação" => "json", "Script" => "JavaScript (mongosh)", _ => "Mongo Console JavaScript" };
        return new AutocompleteContextSnapshot(text, clamped, language, InputJson, fields, names, history, FilePath);
    }
}
