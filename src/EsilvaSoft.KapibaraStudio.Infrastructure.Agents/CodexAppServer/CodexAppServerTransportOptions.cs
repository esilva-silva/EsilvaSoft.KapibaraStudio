namespace EsilvaSoft.KapibaraStudio.Infrastructure.Agents.CodexAppServer;

/// <summary>Limits for the local App Server transport. No response or diagnostic body is retained in logs.</summary>
internal sealed record CodexAppServerTransportOptions
{
    public int MaxFrameBytes { get; init; } = 1024 * 1024;

    public long MaxStdoutBytes { get; init; } = 64L * 1024 * 1024;

    public int MaxStderrBytes { get; init; } = 1024 * 1024;

    public int MaxQueuedMessages { get; init; } = 256;

    public TimeSpan DefaultRequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
