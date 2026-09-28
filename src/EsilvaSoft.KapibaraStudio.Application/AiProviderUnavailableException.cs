using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>The requested hardware cannot run the model. Outside Automatic mode there is no silent CPU fallback.</summary>
public sealed class AiProviderUnavailableException : LocalModelUnavailableException
{
    // Todo construtor fixa o motivo tipado: o provider indisponível é uma linha própria da matriz de fallback e quem
    // a trata não pode depender de qual sobrecarga criou a exceção.
    public AiProviderUnavailableException() => UnavailableReason = LocalModelUnavailableReason.ProviderUnavailable;
    public AiProviderUnavailableException(string message) : base(message) => UnavailableReason = LocalModelUnavailableReason.ProviderUnavailable;
    public AiProviderUnavailableException(string message, Exception? innerException) : base(message, innerException) =>
        UnavailableReason = LocalModelUnavailableReason.ProviderUnavailable;
    public AiProviderUnavailableException(AiAccelerationMode hardware, string reason, Exception? innerException = null)
        : base(Format(hardware, reason), innerException)
    {
        Hardware = hardware;
        Reason = reason;
        UnavailableReason = LocalModelUnavailableReason.ProviderUnavailable;
    }

    public AiAccelerationMode Hardware { get; }
    public string Reason { get; } = "";

    internal AiProviderUnavailableException Localize(Func<string, string>? localize)
    {
        if (localize is null) return this;
        return new AiProviderUnavailableException(Hardware, Reason, this, localize);
    }

    private AiProviderUnavailableException(AiAccelerationMode hardware, string reason, Exception innerException, Func<string, string> localize)
        : base(Format(hardware, reason, localize), innerException)
    {
        Hardware = hardware;
        Reason = reason;
        UnavailableReason = LocalModelUnavailableReason.ProviderUnavailable;
    }

    private static string Format(AiAccelerationMode hardware, string reason, Func<string, string>? localize = null) => hardware == AiAccelerationMode.Auto
        ? (localize?.Invoke("aiNoHardware") ?? "Nenhum hardware disponível pode executar este modelo.") + "\n" + (localize?.Invoke("aiReasonPrefix") ?? "Motivo:") + " " + reason
        : (localize?.Invoke("aiProviderUnavailableHardware") ?? $"Não foi possível executar este modelo utilizando {LocalAiStatusFormatter.HardwareLabel(hardware)}.").Replace("{0}", LocalAiStatusFormatter.HardwareLabel(hardware), StringComparison.Ordinal)
          + "\n" + (localize?.Invoke("aiReasonPrefix") ?? "Motivo:") + " " + reason + "\n"
          + (hardware == AiAccelerationMode.Cpu ? (localize?.Invoke("aiSelectAutomatic") ?? "Você pode selecionar: Automático.") : (localize?.Invoke("aiSelectAutomaticCpu") ?? "Você pode selecionar: Automático ou CPU."));
}
