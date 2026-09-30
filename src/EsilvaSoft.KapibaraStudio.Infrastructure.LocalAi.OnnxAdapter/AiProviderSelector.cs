using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

public static class AiProviderSelector
{
    private static readonly AiAccelerationMode[] AutomaticOrder = [AiAccelerationMode.Npu, AiAccelerationMode.Gpu, AiAccelerationMode.Cpu];

    /// <summary>
    /// Automatic: NPU, GPU and CPU among devices that are available and compatible with the model, with fallback.
    /// Explicit hardware or provider: exactly that backend; an unavailable backend fails instead of silently using CPU.
    /// </summary>
    public static AiExecutionPlan Plan(AutocompleteSettings settings, IReadOnlyList<AiHardwareDevice> hardware, LocalModelDefinition? model = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(hardware);
        var declared = model?.Metadata?.Hardware;
        bool Compatible(AiAccelerationMode kind) => declared is null || declared.Contains(kind);
        if (settings.ExecutionProvider != AiExecutionProvider.Auto)
        {
            var descriptor = OnnxExecutionProviders.Known.First(descriptor => descriptor.Setting == settings.ExecutionProvider);
            if (!Compatible(descriptor.Kind)) throw Incompatible(descriptor.Kind, declared!);
            var device = hardware.FirstOrDefault(device => device.IsAvailable && device.Provider == descriptor.DisplayName)
                ?? throw new AiProviderUnavailableException(descriptor.Kind, $"{descriptor.DisplayName} provider unavailable.");
            return new([Candidate(device, descriptor)], false);
        }
        if (settings.Acceleration != AiAccelerationMode.Auto)
        {
            var kind = settings.Acceleration;
            if (!Compatible(kind)) throw Incompatible(kind, declared!);
            var device = hardware.FirstOrDefault(device => device.Kind == kind && device.IsAvailable && OnnxExecutionProviders.ByDisplayName(device.Provider) is not null)
                ?? throw new AiProviderUnavailableException(kind, hardware.FirstOrDefault(device => device.Kind == kind)?.Reason
                    ?? $"Nenhum provider de {LocalAiStatusFormatter.HardwareLabel(kind)} disponível neste runtime.");
            return new([Candidate(device, OnnxExecutionProviders.ByDisplayName(device.Provider)!)], false);
        }
        var candidates = AutomaticOrder.Where(Compatible)
            .SelectMany(kind => hardware.Where(device => device.Kind == kind && device.IsAvailable))
            .Select(device => OnnxExecutionProviders.ByDisplayName(device.Provider) is { } descriptor ? Candidate(device, descriptor) : null)
            .OfType<AiProviderCandidate>().ToArray();
        if (candidates.Length == 0)
            throw new AiProviderUnavailableException(AiAccelerationMode.Auto, declared is null ? "Nenhum provider disponível neste runtime."
                : $"O modelo declara suporte apenas a {Labels(declared)}, e esse hardware não está disponível.");
        return new(candidates, true);
    }

    private static AiProviderCandidate Candidate(AiHardwareDevice device, OnnxExecutionProviderDescriptor descriptor) =>
        new(device.Kind, descriptor.DisplayName, descriptor.GenAiName, device.Name);

    private static AiProviderUnavailableException Incompatible(AiAccelerationMode kind, IReadOnlyList<AiAccelerationMode> declared) =>
        new(kind, $"O modelo declara suporte apenas a {Labels(declared)} em {LocalModelMetadata.FileName}.");

    private static string Labels(IReadOnlyList<AiAccelerationMode> modes) => modes.Count == 0 ? "nenhum hardware"
        : string.Join(", ", modes.Select(mode => LocalAiStatusFormatter.HardwareLabel(mode)));
}
