using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal sealed record NpuInventoryReport(string Schema, string Source, IReadOnlyList<NpuInventoryDevice> Devices,
    bool NpuAvailable, string? NpuStatus)
{
    public static NpuInventoryReport Create(IReadOnlyList<AiHardwareDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        var npu = devices.Where(device => device.Kind == AiAccelerationMode.Npu).ToArray();
        var available = npu.Any(device => device.IsAvailable);
        var inventory = devices.Select(device => new NpuInventoryDevice(device.Kind.ToString(), device.Provider,
            device.Name, device.IsAvailable, device.MemoryBytes, device.Reason)).ToArray();
        return new("kapilab-npu-inventory-v1", "onnxruntime-ep-device-enumeration", inventory, available,
            available ? null : npu.FirstOrDefault()?.Reason ?? "O runtime não reportou providers de NPU.");
    }
}

internal sealed record NpuInventoryDevice(string Kind, string Provider, string Name, bool IsAvailable,
    long? MemoryBytes, string? Reason);
