namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Espaço reservado para o reuso de KV cache entre pedidos (experimento de R43, desligado por padrão).
/// </summary>
/// <remarks>
/// Nesta entrega o tipo é inerte: nenhum runtime lê ou escreve nele. Existe para que <c>ModelGenerationRequest</c>
/// já tenha o campo e a assinatura não mude quando o experimento for implementado. Os tokens do prefixo reaproveitado
/// ficam aqui porque a invalidação do KV é decidida comparando prefixos, não textos.
/// </remarks>
public sealed record PrefixCacheState
{
    /// <summary>Prompt cujo KV se pretende reaproveitar. Vazio significa "sem nada reaproveitável".</summary>
    public IReadOnlyList<int> Tokens { get; init; } = [];
}
