namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Por que o modelo não pôde atender. Permite decidir sem interpretar a mensagem, que é texto de interface.
/// Os valores são aditivos e a ordem existente é preservada: quem já tratava <see cref="NotLoaded"/> e
/// <see cref="DifferentConfiguration"/> continua valendo, e cada motivo novo corresponde a uma linha própria da
/// matriz de fallback da IA explícita — a mensagem ao usuário deriva do motivo, nunca o contrário.
/// </summary>
public enum LocalModelUnavailableReason : byte
{
    /// <summary>Motivo não classificado (cancelamento de carga, preempção).</summary>
    Unspecified,
    /// <summary>Nenhum modelo carregado e a política do pedido proíbe carregar.</summary>
    NotLoaded,
    /// <summary>Há modelo carregado, mas de outra chave (pasta, aceleração ou provider) e o pedido não pode trocá-lo.</summary>
    DifferentConfiguration,
    /// <summary>Nenhum modelo selecionado nas preferências: não há o que carregar nem o que validar.</summary>
    NoModelConfigured,
    /// <summary>
    /// O pacote existe mas não serve: arquivos ausentes, arquitetura não suportada, tokenizer incompatível ou
    /// contrato de contexto declarado e desconhecido por esta versão (DEC-A31C-CONTEXTCONTRACT).
    /// </summary>
    ModelInvalid,
    /// <summary>O modelo carregou, mas não declara a capacidade exigida pelo papel pedido (chat, autocomplete/fim).</summary>
    CapabilityMissing,
    /// <summary>O hardware ou execution provider exigido não pode executar este modelo nesta máquina.</summary>
    ProviderUnavailable,
    /// <summary>Recusa temporária após falha recente desta mesma chave; <see cref="LocalModelUnavailableException.RetryAfter"/> diz até quando.</summary>
    Cooldown,
    /// <summary>O pedido não cabe na janela do modelo. É erro do pedido, não do modelo: nada é descarregado e nada esfria.</summary>
    ContextOverflow,
    /// <summary>Falha de inicialização ou de inferência do runtime (erro nativo), já convertida em mensagem segura.</summary>
    RuntimeFailure
}

/// <summary>The local model cannot serve this request. Messages never contain editor text or raw native errors with prompts.</summary>
public class LocalModelUnavailableException : InvalidOperationException
{
    public LocalModelUnavailableException() { }
    public LocalModelUnavailableException(string message) : base(message) { }
    public LocalModelUnavailableException(string message, Exception? innerException) : base(message, innerException) { }

    /// <summary>
    /// Motivo tipado; <see cref="LocalModelUnavailableReason.Unspecified"/> quando não classificado. Nome distinto de
    /// <c>Reason</c> porque <c>AiProviderUnavailableException</c> já usa esse nome para o texto do provider.
    /// </summary>
    public LocalModelUnavailableReason UnavailableReason { get; init; }

    /// <summary>
    /// Instante em que a recusa temporária expira, quando <see cref="UnavailableReason"/> é
    /// <see cref="LocalModelUnavailableReason.Cooldown"/> (ou quando esta falha acabou de iniciar um cooldown).
    /// Em UTC, comparável ao <c>TimeProvider</c> de quem exibe o tempo restante; nulo nos demais motivos.
    /// </summary>
    public DateTimeOffset? RetryAfter { get; init; }
}
