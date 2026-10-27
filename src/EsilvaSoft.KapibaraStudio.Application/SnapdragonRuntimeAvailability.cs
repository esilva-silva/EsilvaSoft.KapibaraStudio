using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Platform/version/plugin checks are supplied by composition; device evidence is never inferred from CPU branding.</summary>
public sealed class SnapdragonRuntimeAvailability(IAiHardwareProbe hardware, Func<bool> supportsBuild) : ISnapdragonRuntimeAvailability
{
    /// <summary>The official GenAI managed assembly has version 0.0.0.0; composition supplies the package version compiled into this app.</summary>
    public static bool SupportsBuild(bool isWindows, bool isArm64, string? compiledGenAiPackageVersion, bool packagedPlugin)
    {
        var versionText = compiledGenAiPackageVersion?.Split('+', 2)[0];
        return isWindows && isArm64 && packagedPlugin && Version.TryParse(versionText, out var version) && version >= new Version(0, 18);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!supportsBuild()) return false;
        return (await hardware.GetAvailableHardwareAsync(cancellationToken).ConfigureAwait(false)).Any(IsQualcommNpu);
    }

    public static bool IsQualcommNpu(AiHardwareDevice device)
    {
        if (!device.IsAvailable || device.Kind != AiAccelerationMode.Npu || device.Provider != "QNN" || string.IsNullOrWhiteSpace(device.Vendor)) return false;
        var vendor = device.Vendor.Trim();
        return vendor.Equals("Qualcomm", StringComparison.OrdinalIgnoreCase)
            || vendor.StartsWith("Qualcomm ", StringComparison.OrdinalIgnoreCase)
            || vendor.StartsWith("Qualcomm,", StringComparison.OrdinalIgnoreCase);
    }
}
