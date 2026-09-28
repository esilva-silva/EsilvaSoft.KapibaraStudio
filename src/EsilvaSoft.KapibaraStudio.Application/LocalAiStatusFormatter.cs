using System.Globalization;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>pt-BR text for model state, detected hardware and model tests. Metrics that could not be measured are omitted.</summary>
public static class LocalAiStatusFormatter
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    public static string HardwareLabel(AiAccelerationMode mode, Func<string, string>? localize = null) => mode switch
    {
        AiAccelerationMode.Cpu => "CPU",
        AiAccelerationMode.Gpu => "GPU",
        AiAccelerationMode.Npu => "NPU",
        _ => localize?.Invoke("hardwareAuto") ?? "Automático"
    };

    public static string StateLabel(LocalModelState state, Func<string, string>? localize = null) => state switch
    {
        LocalModelState.NotInstalled => localize?.Invoke("modelStateNotInstalled") ?? "Não instalado",
        LocalModelState.NotLoaded => localize?.Invoke("modelStateNotLoaded") ?? "Não carregado",
        LocalModelState.Available => localize?.Invoke("modelStateAvailable") ?? "Arquivos encontrados",
        LocalModelState.Loading => localize?.Invoke("modelStateLoading") ?? "Carregando",
        LocalModelState.Ready => localize?.Invoke("modelStateReady") ?? "Carregado",
        LocalModelState.Invalid => localize?.Invoke("modelStateInvalid") ?? "Inválido",
        LocalModelState.Unsupported => localize?.Invoke("modelStateUnsupported") ?? "Não suportado",
        LocalModelState.MissingFiles => localize?.Invoke("modelStateMissingFiles") ?? "Arquivos ausentes",
        _ => localize?.Invoke("modelStateFailed") ?? "Falha"
    };

    public static string ValidityLabel(LocalModelValidity validity, Func<string, string>? localize = null) => validity switch
    {
        LocalModelValidity.Valid => localize?.Invoke("modelValidityValid") ?? "válido",
        LocalModelValidity.Unsupported => localize?.Invoke("modelValidityUnsupported") ?? "não suportado",
        LocalModelValidity.MissingFiles => localize?.Invoke("modelValidityMissingFiles") ?? "arquivos ausentes",
        _ => localize?.Invoke("modelValidityInvalid") ?? "inválido"
    };

    /// <summary>Automatic considers NPU, GPU and CPU in this order among reported devices; model compatibility is checked at load.</summary>
    public static AiHardwareDevice? PredictDevice(AiAccelerationMode requested, IReadOnlyList<AiHardwareDevice> hardware)
    {
        ArgumentNullException.ThrowIfNull(hardware);
        AiAccelerationMode[] order = requested == AiAccelerationMode.Auto
            ? [AiAccelerationMode.Npu, AiAccelerationMode.Gpu, AiAccelerationMode.Cpu] : [requested];
        foreach (var kind in order)
            if (hardware.FirstOrDefault(device => device.Kind == kind && device.IsAvailable) is { } device) return device;
        return null;
    }

    public static string DeviceLine(AiHardwareDevice device, Func<string, string>? localize = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!device.IsAvailable)
        {
            var unavailable = HardwareLabel(device.Kind, localize) + " — " + (localize?.Invoke("unavailable") ?? "indisponível");
            return localize is null || string.IsNullOrWhiteSpace(device.Reason)
                ? unavailable
                : unavailable + ": " + LocalizeHardwareReason(device.Reason, localize);
        }
        var details = new[] { device.Kind == AiAccelerationMode.Cpu ? null : device.Provider, device.MemoryBytes is { } memory ? Bytes(memory) : null }
            .Where(value => !string.IsNullOrEmpty(value)).ToArray();
        return $"{HardwareLabel(device.Kind, localize)} — {device.Name}" + (details.Length == 0 ? "" : $" ({string.Join(", ", details)})");
    }

    private static string LocalizeHardwareReason(string reason, Func<string, string>? localize) => reason switch
    {
        "ONNX Runtime não foi carregado neste processo." => localize?.Invoke("hardwareRuntimeNotLoaded") ?? reason,
        "Nenhum provider de GPU (DirectML ou CUDA) disponível nesta distribuição ou máquina." => localize?.Invoke("hardwareGpuProviderUnavailable") ?? reason,
        "Nenhum provider de NPU (QNN, OpenVINO ou VitisAI) disponível nesta distribuição ou máquina." => localize?.Invoke("hardwareNpuProviderUnavailable") ?? reason,
        _ => reason
    };

    public static string Format(LocalModelStatus status, string? selectedModel, AiAccelerationMode requested, IReadOnlyList<AiHardwareDevice> hardware, Func<string, string>? localize = null)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(hardware);
        var lines = new List<string>
        {
            (localize?.Invoke("selectedModel") ?? "Modelo selecionado") + ": " + (string.IsNullOrWhiteSpace(selectedModel) ? localize?.Invoke("none") ?? "nenhum" : selectedModel),
            (localize?.Invoke("modelState") ?? "Estado") + ": " + StateLabel(status.State, localize),
            (localize?.Invoke("hardware") ?? "Hardware") + ": " + HardwareLabel(requested, localize)
        };
        // Preferences may show a new, unsaved selection while another model is still loaded.
        if (status.State is LocalModelState.Ready or LocalModelState.Loading && !string.IsNullOrEmpty(status.ModelName) && status.ModelName != selectedModel)
            lines.Insert(1, (localize?.Invoke("modelInUse") ?? "Modelo em uso") + ": " + status.ModelName);
        if (status.Backend is { } backend)
        {
            lines.Add((localize?.Invoke("backend") ?? "Backend") + ": " + HardwareLabel(backend, localize) + (status.UsedFallback ? " (" + (localize?.Invoke("preferredAccelerationUnavailable") ?? "aceleração preferida indisponível") + ")" : ""));
            if (!string.IsNullOrEmpty(status.Provider)) lines.Add((localize?.Invoke("provider") ?? "Provider") + ": " + status.Provider);
            if (!string.IsNullOrEmpty(status.Device)) lines.Add((localize?.Invoke("device") ?? "Dispositivo") + ": " + status.Device);
            if (status.LoadTime is { } load) lines.Add((localize?.Invoke("loadTime") ?? "Tempo de carregamento") + ": " + Milliseconds(load));
            if (status.ProcessMemoryBytes is { } memory) lines.Add((localize?.Invoke("processMemory") ?? "Memória do processo") + ": " + Bytes(memory));
            if (status.FirstToken is { } first) lines.Add((localize?.Invoke("firstToken") ?? "Primeiro token") + ": " + Milliseconds(first));
            if (status.TokensPerSecond is { } rate) lines.Add((localize?.Invoke("generation") ?? "Geração") + ": " + Rate(rate));
        }
        else if (hardware.Count > 0)
        {
            var predicted = PredictDevice(requested, hardware);
            lines.Add((localize?.Invoke("detectedProvider") ?? "Provider detectado") + ": " + (predicted?.Provider ?? localize?.Invoke("unavailable") ?? "indisponível"));
            if (predicted is not null) lines.Add((localize?.Invoke("device") ?? "Dispositivo") + ": " + predicted.Name);
        }
        if (!string.IsNullOrWhiteSpace(status.Message)) lines.Add(status.Message);
        return string.Join('\n', lines);
    }

    public static string FormatReport(LocalModelTestReport report, Func<string, string>? localize = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        var lines = new List<string> { report.Message };
        if (!string.IsNullOrEmpty(report.ModelName)) lines.Add((localize?.Invoke("model") ?? "Modelo") + ": " + report.ModelName);
        if (report.Backend is { } backend)
            lines.Add((localize?.Invoke("hardware") ?? "Hardware") + ": " + HardwareLabel(backend, localize) + (report.Hardware == AiAccelerationMode.Auto ? " (" + (localize?.Invoke("hardwareAuto") ?? "Automático") + ")" : "")
                + (report.UsedFallback ? " — " + (localize?.Invoke("preferredAccelerationUnavailable") ?? "aceleração preferida indisponível") : ""));
        else if (report.Hardware is { } hardware) lines.Add((localize?.Invoke("requestedHardware") ?? "Hardware solicitado") + ": " + HardwareLabel(hardware, localize));
        if (!string.IsNullOrEmpty(report.Provider)) lines.Add((localize?.Invoke("provider") ?? "Provider") + ": " + report.Provider);
        if (!string.IsNullOrEmpty(report.Device)) lines.Add((localize?.Invoke("device") ?? "Dispositivo") + ": " + report.Device);
        if (report.LoadTime is { } load) lines.Add((localize?.Invoke("loadingTime") ?? "Carregamento") + ": " + Milliseconds(load));
        if (report.FirstToken is { } first) lines.Add((localize?.Invoke("firstToken") ?? "Primeiro token") + ": " + Milliseconds(first));
        if (report.TokensPerSecond is { } rate) lines.Add((localize?.Invoke("generation") ?? "Geração") + ": " + Rate(rate));
        if (report.Steps.Count > 0)
            lines.Add((localize?.Invoke("steps") ?? "Etapas") + ": " + string.Join(" · ", report.Steps.Select(step => $"{step.Name}: {(step.Succeeded ? "OK" : localize?.Invoke("failed") ?? "falhou")}")));
        return string.Join('\n', lines);
    }

    private static string Milliseconds(TimeSpan value) => value.TotalMilliseconds.ToString("N0", Culture) + " ms";
    private static string Rate(double value) => value.ToString(value < 10 ? "N1" : "N0", Culture) + " tokens/s";
    private static string Bytes(long value) => value >= 1L << 30
        ? (value / (double)(1L << 30)).ToString("N1", Culture) + " GB"
        : (value / (double)(1L << 20)).ToString("N0", Culture) + " MB";
}
