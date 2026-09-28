namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public sealed record ModelGenerationResult(string Text, int GeneratedTokens, TimeSpan Elapsed, string Provider, bool IsComplete = true, bool UsedCpuFallback = false)
{
    public TimeSpan? TimeToFirstToken { get; init; }
}
