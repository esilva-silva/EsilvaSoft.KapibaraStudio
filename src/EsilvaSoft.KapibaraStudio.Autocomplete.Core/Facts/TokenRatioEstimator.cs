namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;

/// <summary>
/// Estimativa por <c>caracteres × razão</c>. A razão é injetada, nunca fixada no código: ela varia por modelo e por
/// tipo de texto (JSON de pipeline é mais denso em tokens que prosa) e é calibrada contra o tokenizer real por quem
/// tem acesso a ele. O resultado é um corte preliminar, não a palavra final sobre o orçamento.
/// </summary>
public sealed class TokenRatioEstimator : ITokenEstimator
{
    /// <summary>Ponto de partida conservador até haver calibração medida; não é um valor verificado.</summary>
    public const double DefaultTokensPerCharacter = 0.35;

    public TokenRatioEstimator(double tokensPerCharacter = DefaultTokensPerCharacter)
    {
        if (!double.IsFinite(tokensPerCharacter) || tokensPerCharacter <= 0)
            throw new ArgumentOutOfRangeException(nameof(tokensPerCharacter), tokensPerCharacter, "A razão precisa ser finita e positiva.");
        TokensPerCharacter = tokensPerCharacter;
    }

    /// <summary>Razão calibrada em uso.</summary>
    public double TokensPerCharacter { get; }

    public int Estimate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0) return 0;
        var estimate = Math.Ceiling(text.Length * TokensPerCharacter);
        return estimate >= int.MaxValue ? int.MaxValue : (int)estimate;
    }
}
