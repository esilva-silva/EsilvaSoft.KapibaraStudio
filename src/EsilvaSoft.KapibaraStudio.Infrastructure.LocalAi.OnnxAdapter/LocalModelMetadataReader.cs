using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using System.Security.Cryptography;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>Reads the optional kapibarastudio-model.json. Unknown capability or hardware names are ignored for forward compatibility.</summary>
internal static class LocalModelMetadataReader
{
    private static readonly Dictionary<string, LocalModelCapabilities> CapabilityNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["autocomplete"] = LocalModelCapabilities.Autocomplete, ["chat"] = LocalModelCapabilities.Chat,
        ["fim"] = LocalModelCapabilities.Fim, ["embeddings"] = LocalModelCapabilities.Embeddings, ["embedding"] = LocalModelCapabilities.Embeddings,
        ["agent"] = LocalModelCapabilities.Agent, ["tools"] = LocalModelCapabilities.Tools,
        ["reasoning"] = LocalModelCapabilities.Reasoning
    };

    private static readonly Dictionary<string, AiAccelerationMode> HardwareNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu"] = AiAccelerationMode.Cpu, ["gpu"] = AiAccelerationMode.Gpu, ["npu"] = AiAccelerationMode.Npu
    };

    public static LocalModelMetadata? Read(string root, ILocalModelFileAccess fileAccess)
    {
        var path = Path.Combine(root, LocalModelMetadata.FileName);
        if (!fileAccess.FileExists(path)) return null;
        using var document = LocalModelCatalog.ReadJson(path, fileAccess, 1024 * 1024);
        var json = document.RootElement;
        if (json.ValueKind != JsonValueKind.Object) throw new InvalidDataException();
        var generation = json.TryGetProperty("generation", out var value) && value.ValueKind != JsonValueKind.Null ? value : default;
        if (generation.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Object)) throw new InvalidDataException();
        var capabilities = Texts(json, "capabilities")?.Aggregate(LocalModelCapabilities.None,
            (current, item) => current | CapabilityNames.GetValueOrDefault(item, LocalModelCapabilities.None));
        var agent = Agent(json);
        var reasoning = Reasoning(json);
        var agentGeneration = AgentGeneration(generation);
        var minimumGenAi = RuntimeMinimum(json);
        if (capabilities.HasValue)
        {
            var flags = capabilities.Value;
            if (flags.HasFlag(LocalModelCapabilities.Tools) && !flags.HasFlag(LocalModelCapabilities.Agent)
                || flags.HasFlag(LocalModelCapabilities.Agent) && !flags.HasFlag(LocalModelCapabilities.Tools)
                || flags.HasFlag(LocalModelCapabilities.Reasoning) && !flags.HasFlag(LocalModelCapabilities.Agent)
                || flags.HasFlag(LocalModelCapabilities.Agent) &&
                    (agent is null || agentGeneration is null || !flags.HasFlag(LocalModelCapabilities.Chat))
                || flags.HasFlag(LocalModelCapabilities.Reasoning) && reasoning is not { Supported: true }
                || agent is not null && !flags.HasFlag(LocalModelCapabilities.Agent)
                || reasoning is { Supported: true } && !flags.HasFlag(LocalModelCapabilities.Reasoning))
                throw new InvalidDataException();
        }
        else if (agent is not null || reasoning is not null || agentGeneration is not null) throw new InvalidDataException();
        if (agentGeneration is not null && agent is null) throw new InvalidDataException();
        if (reasoning is { Supported: true } && (agent is null || agentGeneration is null || reasoning.BudgetTokens is null))
            throw new InvalidDataException();
        if (reasoning is { Supported: true }) VerifyTemplate(root, fileAccess, agent!, reasoning);
        return new()
        {
            Name = Text(json, "name", 128),
            Version = Text(json, "version", 64),
            Architecture = Text(json, "architecture", 64),
            Parameters = Text(json, "parameters", 32),
            Domain = Texts(json, "domain")?.Where(item => item.Length is > 0 and <= 64).Take(32).ToArray() ?? [],
            Capabilities = capabilities,
            Hardware = Texts(json, "hardware")?.Where(HardwareNames.ContainsKey).Select(item => HardwareNames[item]).Distinct().ToArray(),
            ContextContract = Text(json, "contextContract", 64),
            SupportsRepositoryContext = Boolean(json, "supportsRepositoryContext"),
            RecommendedContextTokens = Integer(json, "recommendedContextTokens", 64, AutocompleteSettings.AbsoluteContextMaximum),
            RecommendedCompletionTokens = Integer(json, "recommendedCompletionTokens", 1, AutocompleteSettings.AbsoluteCompletionMaximum),
            Autocomplete = Generation(generation, "autocomplete", AutocompleteSettings.AbsoluteCompletionMaximum),
            Chat = Generation(generation, "chat", 1024),
            Agent = agent, Reasoning = reasoning, AgentGeneration = agentGeneration,
            MinimumGenAi = minimumGenAi
        };
    }

    private static LocalModelAgentMetadata? Agent(JsonElement json)
    {
        var value = Object(json, "agent");
        if (value.ValueKind == JsonValueKind.Undefined) return null;
        var prompt = RequiredText(value, "promptFormat", 64);
        var template = RequiredText(value, "chatTemplateFile", 128);
        var call = RequiredText(value, "toolCallFormat", 64);
        var schema = RequiredText(value, "toolSchemaVersion", 64);
        var decoding = Text(value, "constrainedDecoding", 64);
        if (prompt != LocalModelPromptFormats.Qwen3ChatMlTools || template != "chat_template.jinja"
            || call != "qwen-hermes-json" || schema != "kapibara-tools-2026-10"
            || decoding is not (null or "lark_grammar") || Path.GetFileName(template) != template)
            throw new InvalidDataException();
        return new(prompt, template, call, schema,
            Integer(value, "maxToolCallsPerTurn", 1, 12) ?? 12,
            Integer(value, "maxToolResultBytes", 1, 6144) ?? 6144,
            decoding);
    }

    private static LocalModelReasoningMetadata? Reasoning(JsonElement json)
    {
        var value = Object(json, "reasoning");
        if (value.ValueKind == JsonValueKind.Undefined) return null;
        var supported = Boolean(value, "supported") ?? throw new InvalidDataException();
        var budget = Object(value, "budgetTokens");
        LocalModelReasoningBudget? limits = null;
        if (budget.ValueKind != JsonValueKind.Undefined)
        {
            var minimum = Integer(budget, "minimum", 1, 8192) ?? throw new InvalidDataException();
            var maximum = Integer(budget, "maximum", minimum, 8192) ?? throw new InvalidDataException();
            var @default = Integer(budget, "default", minimum, maximum) ?? throw new InvalidDataException();
            limits = new(@default, minimum, maximum);
        }
        var turnBudget = Integer(value, "turnBudgetTokens", 1, 65_536);
        if (supported && (limits is null || turnBudget is null || turnBudget < limits.Default)) throw new InvalidDataException();
        var format = RequiredText(value, "format", 64);
        var toggle = RequiredText(value, "toggle", 32);
        var history = Text(value, "history", 64);
        var exceeded = Text(value, "onBudgetExceeded", 32);
        if (format != "qwen3-think" || toggle is not ("hard" or "none")
            || history is not (null or "strip-previous-turns") || exceeded is not (null or "force-close"))
            throw new InvalidDataException();
        // These are protocol bytes. Trimming would change the Qwen3 prefix/forced closure semantics.
        var suffix = RequiredRawText(value, "disabledGenerationSuffix", 128);
        var close = RawText(value, "forceCloseText", 128);
        if (supported && (close is null || !close.Contains("</think>", StringComparison.Ordinal)
            || !suffix.Contains("<think>", StringComparison.Ordinal)
            || !suffix.Contains("</think>", StringComparison.Ordinal))) throw new InvalidDataException();
        var sha = RequiredText(value, "templateSha256", 64);
        if (sha.Length != 64 || !sha.All(char.IsAsciiHexDigit)) throw new InvalidDataException();
        var start = Integer(value, "startTokenId", 1, int.MaxValue) ?? throw new InvalidDataException();
        var end = Integer(value, "endTokenId", 1, int.MaxValue) ?? throw new InvalidDataException();
        var defaultEnabled = Boolean(value, "defaultEnabled") ?? false;
        if (start == end || toggle == "none" && !defaultEnabled) throw new InvalidDataException();
        return new(supported, format, start, end, defaultEnabled, toggle, suffix, limits, turnBudget,
            exceeded, close, history, sha);
    }

    private static LocalModelAgentGeneration? AgentGeneration(JsonElement generation)
    {
        var value = Object(generation, "agent");
        if (value.ValueKind == JsonValueKind.Undefined) return null;
        var sampling = Object(value, "sampling");
        if (sampling.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException();
        return new(Integer(value, "maxAnswerTokensPerStep", 1, 2048) ?? throw new InvalidDataException(),
            Sampling(Object(sampling, "thinking")), Sampling(Object(sampling, "nonThinking")));
    }

    private static LocalModelAgentSampling Sampling(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined) throw new InvalidDataException();
        return new(Number(value, "temperature", 0, 2), Number(value, "topP", double.Epsilon, 1),
            Integer(value, "topK", 1, 1000) ?? throw new InvalidDataException());
    }

    private static Version? RuntimeMinimum(JsonElement json)
    {
        var runtime = Object(json, "runtime");
        if (runtime.ValueKind == JsonValueKind.Undefined) return null;
        var text = Text(runtime, "minimumGenAi", 32);
        if (text is null) return null;
        if (!Version.TryParse(text, out var version) || version.Major < 0 || version.Minor < 0 || version.Build < 0)
            throw new InvalidDataException();
        return version;
    }

    private static void VerifyTemplate(string root, ILocalModelFileAccess fileAccess, LocalModelAgentMetadata agent,
        LocalModelReasoningMetadata reasoning)
    {
        var path = Path.Combine(root, agent.ChatTemplateFile);
        if (!fileAccess.FileExists(path) || fileAccess.GetFileLength(path) is <= 0 or > 1024 * 1024)
            throw new InvalidDataException();
        using var stream = fileAccess.OpenRead(path);
        var hash = SHA256.HashData(stream);
        if (!Convert.ToHexString(hash).Equals(reasoning.TemplateSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException();
    }

    private static JsonElement Object(JsonElement json, string name)
    {
        if (json.ValueKind == JsonValueKind.Undefined || !json.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null) return default;
        return value.ValueKind == JsonValueKind.Object ? value : throw new InvalidDataException();
    }

    private static string RequiredText(JsonElement json, string name, int maximumLength) =>
        Text(json, name, maximumLength) ?? throw new InvalidDataException();

    private static string RequiredRawText(JsonElement json, string name, int maximumLength) =>
        RawText(json, name, maximumLength) ?? throw new InvalidDataException();

    private static string? RawText(JsonElement json, string name, int maximumLength)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String) throw new InvalidDataException();
        var text = value.GetString()!;
        if (text.Length is 0 || text.Length > maximumLength || text.Contains('\0')) throw new InvalidDataException();
        return text;
    }

    private static double Number(JsonElement json, string name, double minimum, double maximum)
    {
        if (!json.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number
            || !value.TryGetDouble(out var number) || !double.IsFinite(number) || number < minimum || number > maximum)
            throw new InvalidDataException();
        return number;
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
