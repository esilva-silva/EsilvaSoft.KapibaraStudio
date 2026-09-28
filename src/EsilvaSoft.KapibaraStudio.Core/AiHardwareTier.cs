namespace EsilvaSoft.KapibaraStudio.Core;

public enum AiHardwareTier
{
    NoAccelerator,
    Basic,
    Intermediate,
    Advanced,
    High,
    VeryHigh,
    Workstation
}

public sealed record AiHardwareProfile(string Vendor, string Name, long MemoryBytes)
{
    public AiHardwareTier Tier => AiHardwareTiers.For(MemoryBytes);
    public double MemoryGiB => MemoryBytes / 1024d / 1024d / 1024d;
    public string TierLabel => AiHardwareTiers.Label(Tier);
    public override string ToString() => $"{Vendor} {Name} — {MemoryGiB:0.#} GiB — {TierLabel}";
}

public static class AiHardwareTiers
{
    private const long GiB = 1024L * 1024 * 1024;

    public static IReadOnlyList<AiHardwareProfileSettings> Presets { get; } =
    [
        new("Tier", "Sem acelerador (0–2 GiB)", 2 * GiB),
        new("Tier", "Básico (>2–4 GiB)", 4 * GiB),
        new("Tier", "Intermediário (>4–8 GiB)", 8 * GiB),
        new("Tier", "Avançado (>8–12 GiB)", 12 * GiB),
        new("Tier", "Alto (>12–16 GiB)", 16 * GiB),
        new("Tier", "Muito alto (>16–24 GiB)", 24 * GiB),
        new("Tier", "Workstation (>24 GiB)", 32 * GiB)
    ];

    public static (int ContextTokens, int CompletionTokens) Recommendation(AiHardwareTier tier) => tier switch
    {
        AiHardwareTier.NoAccelerator => (512, 32),
        AiHardwareTier.Basic => (1024, 32),
        AiHardwareTier.Intermediate => (2048, 64),
        AiHardwareTier.Advanced => (4096, 128),
        AiHardwareTier.High => (4096, 128),
        AiHardwareTier.VeryHigh => (8192, 256),
        AiHardwareTier.Workstation => (8192, 256),
        _ => (2048, 32)
    };

    public static AiHardwareTier For(long memoryBytes)
    {
        var gib = memoryBytes / (double)GiB;
        return gib <= 2 ? AiHardwareTier.NoAccelerator
            : gib <= 4 ? AiHardwareTier.Basic
            : gib <= 8 ? AiHardwareTier.Intermediate
            : gib <= 12 ? AiHardwareTier.Advanced
            : gib <= 16 ? AiHardwareTier.High
            : gib <= 24 ? AiHardwareTier.VeryHigh
            : AiHardwareTier.Workstation;
    }

    public static string Label(AiHardwareTier tier) => tier switch
    {
        AiHardwareTier.NoAccelerator => "Sem acelerador",
        AiHardwareTier.Basic => "Básico",
        AiHardwareTier.Intermediate => "Intermediário",
        AiHardwareTier.Advanced => "Avançado",
        AiHardwareTier.High => "Alto",
        AiHardwareTier.VeryHigh => "Muito alto",
        AiHardwareTier.Workstation => "Workstation",
        _ => "Desconhecido"
    };
}
