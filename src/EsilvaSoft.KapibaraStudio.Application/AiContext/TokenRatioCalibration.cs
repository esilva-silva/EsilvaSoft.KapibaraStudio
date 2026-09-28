using System.Globalization;
using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application.AiContext;

/// <summary>
/// Relatório de uma calibração da razão <c>tokens/caractere</c> contra o tokenizer real.
/// </summary>
/// <param name="SampleCount">Amostras com pelo menos um caractere.</param>
/// <param name="TotalCharacters">Soma dos caracteres UTF-16 das amostras.</param>
/// <param name="TotalTokens">Soma das contagens exatas devolvidas pelo <see cref="ITokenCounter"/>.</param>
/// <param name="Mean">Razão agregada <c>TotalTokens / TotalCharacters</c>; descreve o corpus, não a pior amostra.</param>
/// <param name="Minimum">Menor razão observada em uma amostra.</param>
/// <param name="Maximum">Maior razão observada em uma amostra.</param>
/// <param name="Percentile">Razão do percentil escolhido, por posto mais próximo sobre as razões por amostra.</param>
/// <param name="PercentileRank">Percentil usado, entre 0 e 1.</param>
/// <param name="Recommended">Razão a injetar em <see cref="TokenRatioEstimator"/>; é <paramref name="Percentile"/>.</param>
public sealed record TokenRatioCalibrationReport(int SampleCount, long TotalCharacters, long TotalTokens,
    double Mean, double Minimum, double Maximum, double Percentile, double PercentileRank, double Recommended)
{
    /// <summary>Estimador pronto com a razão recomendada.</summary>
    public TokenRatioEstimator CreateEstimator() => new(Recommended);

    /// <summary>Relatório curto, em uma linha por métrica; invariante de cultura para poder ir a um arquivo de log.</summary>
    public string Format()
    {
        var report = new StringBuilder();
        report.Append(CultureInfo.InvariantCulture, $"amostras={SampleCount} caracteres={TotalCharacters} tokens={TotalTokens}").Append('\n');
        report.Append(CultureInfo.InvariantCulture, $"media={Mean:0.0000} minimo={Minimum:0.0000} maximo={Maximum:0.0000}").Append('\n');
        report.Append(CultureInfo.InvariantCulture, $"p{PercentileRank * 100:0}={Percentile:0.0000} recomendada={Recommended:0.0000}").Append('\n');
        return report.ToString();
    }
}

/// <summary>
/// Calibra a razão caracteres/token do <see cref="TokenRatioEstimator"/> contra o tokenizer real do modelo.
/// </summary>
/// <remarks>
/// <para><strong>Contra quem se calibra.</strong> Contra o <see cref="ITokenCounter"/>, nunca contra outro estimador:
/// calibrar estimativa com estimativa só propaga o erro. O valor de partida
/// <see cref="TokenRatioEstimator.DefaultTokensPerCharacter"/> é declaradamente um chute e existe apenas até haver
/// medição.</para>
/// <para><strong>Por que percentil e não média.</strong> A razão só é usada para <em>cortar</em> candidatos antes da
/// contagem exata. Subestimar é caro: o pipeline monta um candidato grande demais, a contagem exata reprova e ele
/// precisa cortar e recontar, pagando uma tokenização inteira por passe. Superestimar custa apenas um pouco de
/// contexto a menos. Por isso a razão recomendada é o percentil alto (padrão <see cref="DefaultPercentile"/>) das
/// razões por amostra, e não a média: com ele a estimativa cobre a esmagadora maioria dos textos do corpus. A média
/// continua no relatório porque é a melhor descrição do corpus como um todo e permite ver a dispersão.</para>
/// <para>Puro e offline: nenhuma amostra é guardada, nenhum texto sobrevive à chamada — só números entram no
/// relatório.</para>
/// </remarks>
public static class TokenRatioCalibration
{
    /// <summary>Percentil padrão das razões por amostra; alto de propósito, para o estimador não subestimar.</summary>
    public const double DefaultPercentile = 0.95;

    /// <summary>
    /// Mede as amostras com o contador exato e devolve o relatório.
    /// </summary>
    /// <param name="counter">Contador exato do modelo alvo.</param>
    /// <param name="samples">Textos representativos; amostras vazias são ignoradas.</param>
    /// <param name="percentile">Percentil das razões por amostra, entre 0 e 1.</param>
    /// <exception cref="ArgumentException">Nenhuma amostra tem conteúdo.</exception>
    public static TokenRatioCalibrationReport Calibrate(ITokenCounter counter, IEnumerable<string> samples, double percentile = DefaultPercentile)
    {
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(samples);
        if (!double.IsFinite(percentile) || percentile is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(percentile), percentile, "Um percentil fica entre 0 e 1.");

        var ratios = new List<double>();
        long characters = 0;
        long tokens = 0;
        foreach (var sample in samples)
        {
            ArgumentNullException.ThrowIfNull(sample);
            if (sample.Length == 0) continue;
            var count = counter.Count(sample);
            characters += sample.Length;
            tokens += count.Tokens;
            ratios.Add((double)count.Tokens / sample.Length);
        }

        if (ratios.Count == 0) throw new ArgumentException("Calibrar exige ao menos uma amostra com conteúdo.", nameof(samples));
        ratios.Sort();
        var chosen = ratios[NearestRank(ratios.Count, percentile)];
        return new(ratios.Count, characters, tokens, (double)tokens / characters, ratios[0], ratios[^1], chosen, percentile, chosen);
    }

    /// <summary>Atalho: calibra e devolve o estimador já configurado.</summary>
    /// <param name="counter">Contador exato do modelo alvo.</param>
    /// <param name="samples">Textos representativos.</param>
    /// <param name="percentile">Percentil das razões por amostra.</param>
    public static TokenRatioEstimator CreateEstimator(ITokenCounter counter, IEnumerable<string> samples, double percentile = DefaultPercentile)
        => Calibrate(counter, samples, percentile).CreateEstimator();

    /// <summary>Posto mais próximo, sem interpolação: índice do menor valor que cobre o percentil pedido.</summary>
    private static int NearestRank(int count, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * count);
        return Math.Clamp(rank - 1, 0, count - 1);
    }
}
