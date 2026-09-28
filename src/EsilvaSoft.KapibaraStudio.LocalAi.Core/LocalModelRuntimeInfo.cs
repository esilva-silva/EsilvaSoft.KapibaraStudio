using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public sealed record LocalModelRuntimeInfo(AiAccelerationMode Backend, string Provider, string? Device, TimeSpan LoadTime)
{
    public long? ProcessMemoryBytes { get; init; }
    public bool UsedFallback { get; init; }
}
