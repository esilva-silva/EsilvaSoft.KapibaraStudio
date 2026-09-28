namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>
/// Gerador da sugestão automática (ghost) sem IA. Reusa <see cref="CompletionService"/>, o catálogo e o mesmo
/// ranqueamento da lista explícita; o que muda é apenas a política: acesso <see cref="MetadataAccess.Peek"/> sempre
/// (nenhuma rede, nenhum I/O, nenhuma amostragem disparada por digitação), conjunto pequeno de candidatos e portão de
/// confiança que prefere abster-se a propor um candidato falsamente único. Funciona integralmente sem modelo de IA e
/// sem conexão MongoDB: sem cache local, o catálogo simplesmente devolve o que já tem.
/// </summary>
public sealed class TraditionalPreemptiveCompletionProvider(CompletionService service, InlineCompletionConfidence? confidence = null)
    : ICompletionProvider
{
    private readonly CompletionService _service = service ?? throw new ArgumentNullException(nameof(service));
    private readonly InlineCompletionConfidence _confidence = confidence ?? InlineCompletionConfidence.Default;

    public CompletionProviderKind Kind => CompletionProviderKind.TraditionalPreemptive;
    public InlineCompletionConfidence Confidence => _confidence;

    private string _lastAbstentionReason = "none";

    /// <summary>
    /// Motivo da última abstenção, para diagnóstico; nunca contém texto do editor. É estado <em>compartilhado</em>:
    /// uma única instância deste provedor atende todas as abas, então o valor é o da última resposta produzida por
    /// qualquer uma delas (último a escrever vence) e não identifica aba nem pedido. Leitura e escrita são atômicas
    /// (<see cref="Volatile"/>), de modo que nunca se lê um valor rasgado, mas o dado não serve para decisão: nenhum
    /// caminho de produto o consulta, e correlacionar abstenção com pedido é papel das métricas de cada modalidade.
    /// </summary>
    public string LastAbstentionReason => Volatile.Read(ref _lastAbstentionReason);

    public async ValueTask<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var context = request.Context with
        {
            // Peek é imposto aqui, e não herdado do contexto: digitar nunca pode agendar carga de metadados.
            CatalogAccess = MetadataAccess.Peek,
            MaximumItems = Math.Min(request.Context.MaximumItems, _confidence.MaximumItems),
            MaximumCandidates = Math.Min(request.Context.MaximumCandidates, _confidence.MaximumCandidates)
        };
        var list = await _service.CompleteAsync(context, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var chosen = _confidence.Select(context.Prefix, list, out var reason);
        Volatile.Write(ref _lastAbstentionReason, chosen is null ? reason : "none");
        return new(request, chosen is null ? CompletionList.Empty(list.Version) : new(list.Version, [chosen], false));
    }
}
