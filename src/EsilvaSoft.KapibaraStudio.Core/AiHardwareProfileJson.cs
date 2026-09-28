namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Perfil salvo para estimativa; não representa hardware disponível ao runtime.</summary>
public sealed record AiHardwareProfileSettings(string Vendor, string Name, long MemoryBytes)
{
    public double MemoryGiB => MemoryBytes / 1024d / 1024d / 1024d;
    public string TierLabel => AiHardwareTiers.Label(AiHardwareTiers.For(MemoryBytes));
    public override string ToString() => $"{Vendor} {Name} — {MemoryGiB:0.#} GiB — {TierLabel}";
}
