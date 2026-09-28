namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;

/// <summary>Ranking is data: changing this profile never changes context interpretation.</summary>
public sealed record RankingProfile
{
    public double ExactMatch { get; init; } = 1000;
    public double PrefixMatch { get; init; } = 500;
    public double CamelHumpMatch { get; init; } = 350;
    public double SubstringMatch { get; init; } = 100;
    public double SourceCatalog { get; init; } = 20;
    public double SourceSchema { get; init; } = 40;
    public double SourceSnippet { get; init; } = 10;
    public double DeprecatedPenalty { get; init; } = 80;
    public double StalePenalty { get; init; } = 15;
    public double TypeMismatchPenalty { get; init; } = 50;
    /// <summary>Prioriza consultas de leitura sobre métodos que alteram o estado quando o prefixo está vazio.</summary>
    public double WritePenalty { get; init; } = 120;
    public double UsageWeight { get; init; } = 5;
}
