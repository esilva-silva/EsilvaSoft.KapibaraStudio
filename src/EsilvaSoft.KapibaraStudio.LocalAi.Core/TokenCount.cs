namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// Resultado de uma contagem de tokens. <see cref="IsExact"/> distingue a contagem obtida por
/// <c>ITokenizer.Encode(text).Count</c> sobre o texto inteiro (exata) de uma contagem derivada da soma de blocos
/// contados separadamente cuja fronteira não foi aprovada por um <see cref="ITokenBoundaryOracle"/> (estimativa).
/// </summary>
/// <remarks>
/// Uma contagem com <see cref="IsExact"/> falso só pode ser usada para <em>cortar</em> candidatos de contexto;
/// nunca para autorizar o prompt final. O prompt final é sempre tokenizado inteiro, uma única vez.
/// </remarks>
public readonly record struct TokenCount
{
    /// <summary>Cria uma contagem com o número de tokens e o grau de confiança informados.</summary>
    /// <param name="tokens">Quantidade de tokens; nunca negativa.</param>
    /// <param name="isExact">Verdadeiro apenas quando o valor veio de uma tokenização completa ou de somas cujas fronteiras foram todas aprovadas.</param>
    public TokenCount(int tokens, bool isExact)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tokens);
        Tokens = tokens;
        IsExact = isExact;
    }

    /// <summary>Quantidade de tokens contada ou estimada.</summary>
    public int Tokens { get; }

    /// <summary>Verdadeiro quando a contagem é comprovadamente igual a <c>Encode(texto).Count</c>.</summary>
    public bool IsExact { get; }

    /// <summary>Contagem comprovada por tokenização completa do texto.</summary>
    public static TokenCount Exact(int tokens) => new(tokens, isExact: true);

    /// <summary>Contagem derivada de soma de blocos sem prova de estabilidade da fronteira.</summary>
    public static TokenCount Estimated(int tokens) => new(tokens, isExact: false);

    /// <summary>
    /// Soma duas contagens. O resultado só permanece exato quando ambas as parcelas são exatas e a fronteira entre
    /// elas foi aprovada, porque BPE não é composicional: <c>Encode(A) + Encode(B) ≠ Encode(A + B)</c> em geral.
    /// </summary>
    /// <param name="other">Contagem do bloco seguinte.</param>
    /// <param name="boundaryApproved">Resultado do <see cref="ITokenBoundaryOracle"/> para a fronteira entre os dois blocos.</param>
    public TokenCount Add(TokenCount other, bool boundaryApproved) =>
        new(Tokens + other.Tokens, IsExact && other.IsExact && boundaryApproved);
}
