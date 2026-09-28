using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public sealed record LocalModelTestReport(bool Succeeded, string Message, IReadOnlyList<LocalModelTestStep> Steps)
{
    public string? ModelName { get; init; }
    public AiAccelerationMode? Hardware { get; init; }
    public AiAccelerationMode? Backend { get; init; }
    public string? Provider { get; init; }
    public string? Device { get; init; }
    public TimeSpan? LoadTime { get; init; }
    public TimeSpan? FirstToken { get; init; }
    public double? TokensPerSecond { get; init; }
    public int GeneratedTokens { get; init; }
    public bool UsedFallback { get; init; }
}
