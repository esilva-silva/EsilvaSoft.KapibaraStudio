using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Pedido de reuso de KV por sessão (experimento de R43, desligado por padrão).
/// </summary>
/// <remarks>
/// O runtime mantém os tokens válidos; <see cref="Tokens"/> é legado e não concede reuso. O modo Reuse também
/// exige opt-in nas configurações e homologação declarada no pacote CPU. O laboratório pode avaliar pacotes ainda não aprovados.
/// </remarks>
public sealed record PrefixCacheState
{
    public string SessionKey { get; init; } = "";
    public PrefixCacheMode Mode { get; init; }
    public int? CapTokens { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public CancellationToken SessionLifetime { get; init; }
    /// <summary>Prompt cujo KV se pretende reaproveitar. Vazio significa "sem nada reaproveitável".</summary>
    public IReadOnlyList<int> Tokens { get; init; } = [];
}
