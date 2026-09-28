using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public sealed record LocalModelStatus(LocalModelState State, string Message, string? Provider = null)
{
    public string? ModelName { get; init; }
    public AiAccelerationMode? RequestedHardware { get; init; }
    public AiAccelerationMode? Backend { get; init; }
    public string? Device { get; init; }
    public TimeSpan? LoadTime { get; init; }
    public long? ProcessMemoryBytes { get; init; }
    public TimeSpan? FirstToken { get; init; }
    public double? TokensPerSecond { get; init; }
    public bool UsedFallback { get; init; }
}
