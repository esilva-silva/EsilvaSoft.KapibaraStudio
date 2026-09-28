namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Resumo estatístico de uma métrica entre casos: média, desvio padrão amostral, mínimo, mediana, p95 e máximo.
/// </summary>
/// <remarks>
/// O desvio padrão é obrigatório e não opcional: uma média de latência sem dispersão não diz se a medição foi feita em
/// máquina estável, e relatar número assim é proibido pelas regras de performance do repositório. O desvio é amostral
/// (divisor <c>n - 1</c>) porque os casos são uma amostra do espaço de completions, não a população inteira.
/// </remarks>
public sealed record AiMetricSummary
{
    private AiMetricSummary(int count, double mean, double standardDeviation, double minimum, double median, double percentile95, double maximum)
    {
        Count = count;
        Mean = mean;
        StandardDeviation = standardDeviation;
        Minimum = minimum;
        Median = median;
        Percentile95 = percentile95;
        Maximum = maximum;
    }

    /// <summary>Resumo vazio; todas as estatísticas em zero.</summary>
    public static AiMetricSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public int Count { get; }
    public double Mean { get; }

    /// <summary>Desvio padrão amostral; zero quando há menos de dois valores.</summary>
    public double StandardDeviation { get; }

    public double Minimum { get; }
    public double Median { get; }

    /// <summary>Percentil 95 por interpolação linear entre posições vizinhas.</summary>
    public double Percentile95 { get; }

    public double Maximum { get; }

    /// <summary>Resume os valores de <paramref name="values"/>; a ordem de entrada é irrelevante.</summary>
    public static AiMetricSummary Of(IEnumerable<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var ordered = values.ToArray();
        if (ordered.Length == 0) return Empty;
        Array.Sort(ordered);
        var mean = ordered.Average();
        var deviation = ordered.Length < 2
            ? 0d
            : Math.Sqrt(ordered.Sum(value => (value - mean) * (value - mean)) / (ordered.Length - 1));
        return new(ordered.Length, mean, deviation, ordered[0], Percentile(ordered, 0.5), Percentile(ordered, 0.95), ordered[^1]);
    }

    /// <summary>Resume uma métrica inteira, convertida para ponto flutuante.</summary>
    public static AiMetricSummary Of(IEnumerable<int> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return Of(values.Select(value => (double)value));
    }

    private static double Percentile(double[] ordered, double fraction)
    {
        if (ordered.Length == 1) return ordered[0];
        var position = fraction * (ordered.Length - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        return lower == upper ? ordered[lower] : ordered[lower] + ((ordered[upper] - ordered[lower]) * (position - lower));
    }
}
