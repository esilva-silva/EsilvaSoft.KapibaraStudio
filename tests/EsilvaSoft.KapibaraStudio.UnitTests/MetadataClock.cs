namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>Deterministic <see cref="TimeProvider"/> used by metadata cache freshness/backoff tests.</summary>
internal sealed class MetadataClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Now;
}
