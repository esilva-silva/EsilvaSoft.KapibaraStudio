namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public sealed record ModelGenerationRequest(string Prefix, string Suffix, int ContextTokens, int MaximumTokens, bool RequireFullContext = false)
{
    /// <summary>Zero keeps greedy decoding.</summary>
    public double Temperature { get; init; }

    /// <summary>
    /// Prompt já tokenizado, no formato do modelo carregado (marcadores FIM inclusos). Quando presente, o runtime o
    /// usa como está e não tokeniza <see cref="Prefix"/>/<see cref="Suffix"/> de novo — quem montou o contexto na
    /// Fase 3 já pagou esse custo. O runtime continua validando se o prompt cabe na janela do modelo.
    /// </summary>
    public IReadOnlyList<int>? PromptTokens { get; init; }

    /// <summary>
    /// Sequências de parada por texto, além dos tokens de parada do próprio modelo. A geração para no primeiro passo
    /// em que o texto acumulado contém uma delas; o texto do pedaço em que a sequência apareceu é entregue.
    /// </summary>
    public IReadOnlyList<string>? StopSequences { get; init; }

    /// <summary>
    /// Estado de reuso de KV cache entre pedidos. Inerte em R42: o campo existe para que a assinatura não mude quando
    /// R43 implementar o experimento de prefix cache por provider; o runtime ignora qualquer valor aqui.
    /// </summary>
    public PrefixCacheState? PrefixCache { get; init; }
}
