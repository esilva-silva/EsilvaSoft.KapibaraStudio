using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>Reads the optional kapibarastudio-model.json. Unknown capability or hardware names are ignored for forward compatibility.</summary>
internal static class LocalModelMetadataReader
{
    private static readonly Dictionary<string, LocalModelCapabilities> CapabilityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["autocomplete"] = LocalModelCapabilities.Autocomplete, ["chat"] = LocalModelCapabilities.Chat,
        ["fim"] = LocalModelCapabilities.Fim, ["embeddings"] = LocalModelCapabilities.Embeddings, ["embedding"] = LocalModelCapabilities.Embeddings
    };

    private static readonly Dictionary<string, AiAccelerationMode> HardwareNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu"] = AiAccelerationMode.Cpu, ["gpu"] = AiAccelerationMode.Gpu, ["npu"] = AiAccelerationMode.Npu
    };

    public static LocalModelMetadata? Read(string root)
    {
        var path = Path.Combine(root, LocalModelMetadata.FileName);
        if (!File.Exists(path)) return null;
        using var document = LocalModelCatalog.ReadJson(path, 1024 * 1024);
        var json = document.RootElement;
        if (json.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        var generation = json.TryGetProperty("generation", out var value) && value.ValueKind != JsonValueKind.Null ? value : default;
        if (generation.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object)) throw new InvalidDataException();
        return new()
        {
            Name = Text(json, "name", 128),
            Version = Text(json, "version", 64),
            Architecture = Text(json, "architecture", 64),
            Parameters = Text(json, "parameters", 32),
            Domain = Texts(json, "domain")?.Where(item => item.Length is > 0 and <= 64).Take(32).ToArray() ?? [],
            Capabilities = Texts(json, "capabilities")?.Aggregate(LocalModelCapabilities.None,
                (current, item) => current | CapabilityNames.GetValueOrDefault(item, LocalModelCapabilities.None)),
            Hardware = Texts(json, "hardware")?.Where(HardwareNames.ContainsKey).Select(item => HardwareNames[item]).Distinct().ToArray(),
            ContextContract = Text(json, "contextContract", 64),
            SupportsRepositoryContext = Boolean(json, "supportsRepositoryContext"),
            RecommendedContextTokens = Integer(json, "recommendedContextTokens", 64, AutocompleteSettings.AbsoluteContextMaximum),
            RecommendedCompletionTokens = Integer(json, "recommendedCompletionTokens", 1, AutocompleteSettings.AbsoluteCompletionMaximum),
            Autocomplete = Generation(generation, "autocomplete", AutocompleteSettings.AbsoluteCompletionMaximum),
            Chat = Generation(generation, "chat", 1024)
        };
    }

    private static string? Text(JsonElement json, string name, int maximumLength)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException();
        var text = value.GetString()!.Trim();
        if (text.Length > maximumLength || text.Contains('\0')) throw new InvalidDataException();
        return text.Length == 0 ? null : text;
    }

    private static string[]? Texts(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw new InvalidDataException();
        return value.EnumerateArray().Take(64).Select(item => item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : throw new InvalidDataException()).ToArray();
    }

    private static bool? Boolean(JsonElement json, string name)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true, JsonValueKind.False => false, _ => throw new InvalidDataException()
        };
    }

    private static int? Integer(JsonElement json, string name, int minimum, int maximum)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) && number >= minimum && number <= maximum
            ? number : throw new InvalidDataException();
    }

    private static LocalModelGenerationDefaults Generation(JsonElement generation, string name, int maximumTokens)
    {
        if (generation.ValueKind != JsonValueKind.Object || !generation.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return new();
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        double? temperature = null;
        if (value.TryGetProperty("temperature", out var number) && number.ValueKind != JsonValueKind.Null)
            temperature = number.ValueKind == JsonValueKind.Number && number.TryGetDouble(out var parsed) && parsed is >= 0 and <= 2 ? parsed : throw new InvalidDataException();
        return new(Integer(value, "maxTokens", 1, maximumTokens), temperature);
    }
}
