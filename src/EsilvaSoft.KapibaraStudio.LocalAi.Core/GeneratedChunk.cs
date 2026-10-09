namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Um pedaço de geração pronto para exibir. <paramref name="Text"/> é só o texto novo: concatenar todos os pedaços,
/// na ordem, reproduz exatamente o texto do modo não streaming.
/// </summary>
/// <param name="Text">Texto novo deste passo; pode ser vazio quando o token só fecha parte de um caractere.</param>
/// <param name="GeneratedTokens">Tokens de saída acumulados até aqui, sem contar o token de parada.</param>
/// <param name="IsFinal">Verdadeiro apenas no último pedaço, que sempre é emitido e carrega as medições.</param>
public sealed record GeneratedChunk(string Text, int GeneratedTokens, bool IsFinal)
{
    /// <summary>Generated token for this chunk when the runtime exposes it; null on aggregate/final chunks.</summary>
    public int? TokenId { get; init; }
    /// <summary>Tempo desde o início da geração; preenchido no pedaço final.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Tempo até o primeiro token; preenchido no pedaço final quando houve geração.</summary>
    public TimeSpan? TimeToFirstToken { get; init; }

    /// <summary>Provider efetivo da geração; preenchido no pedaço final.</summary>
    public string Provider { get; init; } = "";

    /// <summary>Se a geração terminou por conta própria em vez de esbarrar no limite de tokens.</summary>
    public bool IsComplete { get; init; } = true;

    /// <summary>Se a geração caiu para CPU depois de uma falha do provider acelerado.</summary>
    public bool UsedCpuFallback { get; init; }
}
