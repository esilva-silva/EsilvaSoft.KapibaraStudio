using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>
/// User-facing model names built from the publishing convention: variant folders <c>int4</c>, <c>int8</c>, <c>dml-fp16</c>…,
/// installed folders <c>&lt;family&gt;-ONNX-&lt;variant&gt;</c> and metadata names such as <c>SlopCoder-Mongo-0.5B ONNX DML-FP16</c>.
/// </summary>
public static class ModelDisplayNames
{
    private static readonly string[] Precisions = ["INT4", "INT8", "FP16", "FP32", "BF16", "W4A16", "W8A16"];
    private static readonly (string Prefix, AiAccelerationMode Hardware, string Provider)[] Providers =
    [
        ("DML", AiAccelerationMode.Gpu, "DirectML"),
        ("CUDA", AiAccelerationMode.Gpu, "CUDA"),
        ("QNN", AiAccelerationMode.Npu, "QNN"),
        ("OPENVINO", AiAccelerationMode.Gpu, "OpenVINO")
    ];

    /// <summary><c>dml-fp16</c> → GPU DirectML FP16; <c>int4</c> → CPU INT4; any other name → null.</summary>
    public static ModelFlavor? ParseVariant(string variant)
    {
        ArgumentNullException.ThrowIfNull(variant);
        var parts = variant.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2) return null;
        var precision = parts[^1].ToUpperInvariant();
        if (!Precisions.Contains(precision)) return null;
        if (parts.Length == 1) return new(AiAccelerationMode.Cpu, null, precision);
        var provider = Providers.FirstOrDefault(entry => string.Equals(entry.Prefix, parts[0], StringComparison.OrdinalIgnoreCase));
        return provider.Provider is null ? null : new(provider.Hardware, provider.Provider, precision);
    }

    public static string HardwareText(ModelFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(flavor);
        return flavor.Hardware switch
        {
            AiAccelerationMode.Gpu => flavor.Provider is null ? "GPU" : "GPU " + flavor.Provider,
            AiAccelerationMode.Npu => flavor.Provider is null ? "NPU" : "NPU " + flavor.Provider,
            _ => "CPU"
        };
    }

    /// <summary>For example "SlopCoder-Mongo-1.5B-full — GPU DirectML FP16".</summary>
    public static string Title(string family, ModelFlavor flavor) => $"{family} — {HardwareText(flavor)} {flavor.Precision}";

    /// <summary>Friendly title of an installed model, or null when neither the folder nor the metadata follows the convention.</summary>
    public static string? ForInstalled(string folderName, LocalModelMetadata? metadata)
    {
        ArgumentNullException.ThrowIfNull(folderName);
        const string marker = "-ONNX-";
        var index = folderName.LastIndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index > 0 && ParseVariant(folderName[(index + marker.Length)..]) is { } flavor) return Title(folderName[..index], flavor);
        return metadata?.Name is { } name ? FromMetadataName(name, metadata.Hardware) : null;
    }

    private static string? FromMetadataName(string name, IReadOnlyList<AiAccelerationMode>? hardware)
    {
        string? precision = null, provider = null;
        var family = new List<string>();
        foreach (var token in name.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("ONNX", StringComparison.OrdinalIgnoreCase)) continue;
            if (ParseVariant(token) is { } parsed)
            {
                precision = parsed.Precision;
                provider ??= parsed.Provider;
                continue;
            }
            family.Add(token);
        }
        if (precision is null || family.Count == 0) return null;
        var mode = provider is not null || hardware?.Contains(AiAccelerationMode.Gpu) == true ? AiAccelerationMode.Gpu
            : hardware?.Contains(AiAccelerationMode.Npu) == true ? AiAccelerationMode.Npu : AiAccelerationMode.Cpu;
        return Title(string.Join(' ', family), new ModelFlavor(mode, provider, precision));
    }
}
