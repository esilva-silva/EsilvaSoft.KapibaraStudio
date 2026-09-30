using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Globalization;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using Microsoft.ML.OnnxRuntime;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>Reports what the ONNX Runtime of this build exposes on this machine, so the UI never promises an absent backend.</summary>
public sealed class OnnxHardwareProbe : IAiHardwareProbe
{
    private readonly Lazy<IReadOnlyList<AiHardwareDevice>> _devices = new(Detect, LazyThreadSafetyMode.ExecutionAndPublication);

    public Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default) =>
        _devices.IsValueCreated ? Task.FromResult(_devices.Value) : Task.Run(() => _devices.Value, cancellationToken);

    public static IReadOnlyList<AiHardwareDevice> Detect()
    {
        try
        {
            var environment = OrtEnv.Instance();
            environment.DisableTelemetryEvents();
            var providers = environment.GetAvailableProviders();
            IReadOnlyList<OnnxEpDevice> devices;
            // Device enumeration needs a recent native runtime; provider names alone remain a valid, less detailed answer.
            try { devices = environment.GetEpDevices().Select(ToDevice).ToArray(); }
            catch (Exception ex) when (ex is OnnxRuntimeException or EntryPointNotFoundException or NotSupportedException) { devices = []; }
            return Describe(providers, devices);
        }
        catch (Exception ex) when (ex is OnnxRuntimeException or DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            return Describe([], []);
        }
    }

    /// <summary>Available entries only for providers both this build can configure and the runtime reports. GPU/NPU absence never affects CPU.</summary>
    public static IReadOnlyList<AiHardwareDevice> Describe(IReadOnlyCollection<string> providers, IReadOnlyList<OnnxEpDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(devices);
        var result = new List<AiHardwareDevice>();
        foreach (var kind in new[] { AiAccelerationMode.Cpu, AiAccelerationMode.Gpu, AiAccelerationMode.Npu })
        {
            var added = false;
            foreach (var descriptor in OnnxExecutionProviders.Known.Where(descriptor => descriptor.Kind == kind && providers.Contains(descriptor.OrtName)))
            {
                var reported = devices.Where(device => device.ExecutionProvider == descriptor.OrtName).ToArray();
                // OpenVINO, for example, also exposes CPU and GPU devices: only a device of this kind counts.
                var device = reported.Where(device => device.Kind == kind).OrderBy(device => device.AdapterIndex ?? int.MaxValue).FirstOrDefault();
                if (device is null && (reported.Length > 0 || descriptor.OrtName == "OpenVINOExecutionProvider")) continue;
                result.Add(new(kind, descriptor.DisplayName, device?.Description ?? LocalAiStatusFormatter.HardwareLabel(kind), true) { MemoryBytes = device?.MemoryBytes });
                added = true;
            }
            if (!added)
                result.Add(new(kind, "", LocalAiStatusFormatter.HardwareLabel(kind), false)
                {
                    Reason = kind switch
                    {
                        AiAccelerationMode.Cpu => "ONNX Runtime não foi carregado neste processo.",
                        AiAccelerationMode.Gpu => "Nenhum provider de GPU (DirectML ou CUDA) disponível nesta distribuição ou máquina.",
                        _ => "Nenhum provider de NPU (QNN, OpenVINO ou VitisAI) disponível nesta distribuição ou máquina."
                    }
                });
        }
        return result;
    }

    private static OnnxEpDevice ToDevice(OrtEpDevice device)
    {
        var hardware = device.HardwareDevice;
        var metadata = hardware.Metadata.Entries;
        var kind = hardware.Type switch
        {
            OrtHardwareDeviceType.GPU => AiAccelerationMode.Gpu,
            OrtHardwareDeviceType.NPU => AiAccelerationMode.Npu,
            _ => AiAccelerationMode.Cpu
        };
        var description = metadata.TryGetValue("Description", out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : hardware.Vendor?.Trim();
        long? memory = metadata.TryGetValue("DxgiVideoMemory", out var video)
            && long.TryParse(video.Split(' ', 2)[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var megabytes) ? megabytes * 1024 * 1024 : null;
        int? adapter = metadata.TryGetValue("DxgiAdapterNumber", out var number)
            && int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : null;
        return new(device.EpName, kind, description, memory, adapter);
    }
}
