using System.Security.Cryptography;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>
/// A deliberately local contract. It does not claim to implement an unpublished exporter's schema.
/// Unknown versions and the not-yet-specified NPU block fail closed in strict mode.
/// </summary>
internal static class StrictKapiModelValidation
{
    internal const string MetadataSchema = "kapilab-kapi-model-v1";
    internal const string ManifestSchema = "kapilab-kapicoder-manifest-v1";
    private const int MaximumJsonBytes = 1024 * 1024;
    private const int MaximumManifestFiles = 10_000;

    internal sealed record Result(bool Valid, IReadOnlyList<string> Issues, int? VerifiedFiles = null);

    internal static Result ValidateMetadata(string package, KapiLabModelFileAccess files)
    {
        var issues = new List<string>();
        try
        {
            var path = Path.Combine(package, LocalModelMetadata.FileName);
            using var document = ReadJson(path, files);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !UniqueProperties(root))
                return Invalid("metadata.object_or_duplicate_property");
            if (!String(root, "schema", 64, out var schema) || schema != MetadataSchema)
                issues.Add("metadata.schema_unsupported");
            if (!String(root, "contextContract", 64, out var context) || context != LocalModelContextContracts.EditorContextV1)
                issues.Add("metadata.context_contract_unsupported");
            if (root.TryGetProperty("npu", out _)) issues.Add("metadata.npu_schema_unavailable");

            var capabilities = ReadCapabilities(root, issues);
            var agent = Object(root, "agent", issues);
            var reasoning = Object(root, "reasoning", issues);
            var generation = Object(root, "generation", issues);
            var agentGeneration = generation is { } generationObject ? Object(generationObject, "agent", issues) : null;
            var runtime = Object(root, "runtime", issues);

            if (runtime is { } runtimeObject)
            {
                if (!String(runtimeObject, "minimumGenAi", 32, out var minimum) ||
                    !Version.TryParse(minimum, out var version) || version.Build < 0 || version.Revision >= 0)
                    issues.Add("metadata.runtime.minimum_genai_invalid");
            }
            else issues.Add("metadata.runtime.minimum_genai_missing");
            if (agent is { } agentObject)
            {
                if (!capabilities.Contains("chat") || !capabilities.Contains("agent") || !capabilities.Contains("tools"))
                    issues.Add("metadata.agent.capabilities_missing");
                CheckLiteral(agentObject, "promptFormat", LocalModelPromptFormats.Qwen3ChatMlTools, issues);
                CheckLiteral(agentObject, "chatTemplateFile", "chat_template.jinja", issues);
                CheckLiteral(agentObject, "toolCallFormat", "qwen-hermes-json", issues);
                CheckLiteral(agentObject, "toolSchemaVersion", "kapibara-tools-2026-10", issues);
                CheckInt(agentObject, "maxToolCallsPerTurn", 1, 12, issues);
                CheckInt(agentObject, "maxToolResultBytes", 1, 6144, issues);
                var templatePath = Path.Combine(package, "chat_template.jinja");
                if (!files.FileExists(templatePath) || files.GetFileLength(templatePath) is < 1 or > MaximumJsonBytes)
                    issues.Add("metadata.agent.template_missing_or_oversized");
                if (agentObject.TryGetProperty("constrainedDecoding", out var decoding) &&
                    (decoding.ValueKind != JsonValueKind.String || decoding.GetString() != "lark_grammar"))
                    issues.Add("metadata.agent.constrained_decoding_invalid");
                if (agentGeneration is null) issues.Add("metadata.generation.agent_missing");
            }
            else if (capabilities.Contains("agent") || capabilities.Contains("tools") || agentGeneration is not null)
                issues.Add("metadata.agent.block_missing");

            if (agentGeneration is { } agentGenerationObject)
            {
                CheckInt(agentGenerationObject, "maxAnswerTokensPerStep", 1, 2048, issues);
                var sampling = Object(agentGenerationObject, "sampling", issues);
                if (sampling is null) issues.Add("metadata.generation.sampling_missing");
                else
                {
                    CheckSampling(sampling.Value, "thinking", issues);
                    CheckSampling(sampling.Value, "nonThinking", issues);
                }
            }

            if (reasoning is { } reasoningObject)
            {
                if (!capabilities.Contains("reasoning") || agent is null ||
                    !Bool(reasoningObject, "supported", out var supported) || !supported)
                    issues.Add("metadata.reasoning.capabilities_or_support_invalid");
                CheckLiteral(reasoningObject, "format", "qwen3-think", issues);
                CheckOneOf(reasoningObject, "toggle", ["hard", "none"], issues);
                CheckLiteral(reasoningObject, "onBudgetExceeded", "force-close", issues);
                CheckLiteral(reasoningObject, "history", "strip-previous-turns", issues);
                if (!Bool(reasoningObject, "defaultEnabled", out var defaultEnabled))
                    issues.Add("metadata.reasoning.default_enabled_invalid");
                else if (String(reasoningObject, "toggle", 32, out var toggle) && toggle == "none" && !defaultEnabled)
                    issues.Add("metadata.reasoning.toggle_default_invalid");
                var start = CheckInt(reasoningObject, "startTokenId", 1, int.MaxValue, issues);
                var end = CheckInt(reasoningObject, "endTokenId", 1, int.MaxValue, issues);
                if (start.HasValue && start == end) issues.Add("metadata.reasoning.token_ids_equal");
                var budget = Object(reasoningObject, "budgetTokens", issues);
                int? defaultBudget = null;
                if (budget is null) issues.Add("metadata.reasoning.budget_missing");
                else
                {
                    var min = CheckInt(budget.Value, "minimum", 1, 8192, issues);
                    var max = CheckInt(budget.Value, "maximum", 1, 8192, issues);
                    var preferred = CheckInt(budget.Value, "default", 1, 8192, issues);
                    defaultBudget = preferred;
                    if (min.HasValue && max.HasValue && preferred.HasValue &&
                        (min > max || preferred < min || preferred > max)) issues.Add("metadata.reasoning.budget_order_invalid");
                }
                var turnBudget = CheckInt(reasoningObject, "turnBudgetTokens", 1, 65_536, issues);
                if (turnBudget.HasValue && defaultBudget.HasValue && turnBudget < defaultBudget)
                    issues.Add("metadata.reasoning.turn_budget_invalid");
                if (!String(reasoningObject, "disabledGenerationSuffix", 128, out var suffix) ||
                    !suffix.Contains("<think>", StringComparison.Ordinal) || !suffix.Contains("</think>", StringComparison.Ordinal))
                    issues.Add("metadata.reasoning.disabled_suffix_invalid");
                if (!String(reasoningObject, "forceCloseText", 128, out var close) ||
                    !close.Contains("</think>", StringComparison.Ordinal))
                    issues.Add("metadata.reasoning.force_close_invalid");
                if (!HashString(reasoningObject, "templateSha256", out var expected))
                    issues.Add("metadata.reasoning.template_hash_invalid");
                else VerifyFileHash(Path.Combine(package, "chat_template.jinja"), expected, files, issues,
                    "metadata.reasoning.template_hash_mismatch");
            }
            else if (capabilities.Contains("reasoning")) issues.Add("metadata.reasoning.block_missing");

            return new(issues.Count == 0, issues);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            return Invalid("metadata.unreadable_or_invalid");
        }
    }

    internal static Result VerifyHashes(string package, KapiLabModelFileAccess files)
    {
        var issues = new List<string>();
        var verified = 0;
        try
        {
            var root = Path.GetFullPath(package);
            using var document = ReadJson(Path.Combine(root, "kapicoder_manifest.json"), files);
            var json = document.RootElement;
            if (json.ValueKind != JsonValueKind.Object || !UniqueProperties(json))
                return Invalid("manifest.object_or_duplicate_property");
            if (!String(json, "schema", 64, out var schema) || schema != ManifestSchema)
                return Invalid("manifest.schema_unsupported");
            if (!json.TryGetProperty("files", out var entries) || entries.ValueKind != JsonValueKind.Array ||
                entries.GetArrayLength() is < 1 or > MaximumManifestFiles)
                return Invalid("manifest.files_invalid");
            var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var validated = new List<(string FullPath, string Expected)>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !UniqueProperties(entry) ||
                    !String(entry, "path", 512, out var relative) || !SafeRelativePath(relative) ||
                    !HashString(entry, "sha256", out var expected))
                {
                    return Invalid("manifest.entry_invalid");
                }
                if (!seen.Add(relative)) return Invalid("manifest.path_duplicate");
                var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsContained(root, full)) return Invalid("manifest.path_outside_package");
                validated.Add((full, expected));
            }
            foreach (var (full, expected) in validated)
            {
                if (!files.FileExists(full)) { issues.Add("manifest.file_missing"); continue; }
                if (VerifyFileHash(full, expected, files, issues, "manifest.hash_mismatch")) verified++;
            }
            return new(issues.Count == 0, issues.Distinct(StringComparer.Ordinal).ToArray(), verified);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            return Invalid("manifest.unreadable_or_invalid");
        }
    }

    private static Result Invalid(string issue) => new(false, [issue]);

    private static JsonDocument ReadJson(string path, KapiLabModelFileAccess files)
    {
        if (!files.FileExists(path) || files.GetFileLength(path) is < 1 or > MaximumJsonBytes)
            throw new InvalidDataException();
        using var stream = files.OpenRead(path);
        return JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 32 });
    }

    private static bool UniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
                if (!names.Add(property.Name) || !UniqueProperties(property.Value)) return false;
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) if (!UniqueProperties(item)) return false;
        return true;
    }

    private static HashSet<string> ReadCapabilities(JsonElement root, List<string> issues)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!root.TryGetProperty("capabilities", out var array) || array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 32)
        { issues.Add("metadata.capabilities_invalid"); return result; }
        foreach (var value in array.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } name ||
                name is not ("autocomplete" or "chat" or "fim" or "embeddings" or "agent" or "tools" or "reasoning") ||
                !result.Add(name)) issues.Add("metadata.capabilities_invalid");
        }
        return result;
    }

    private static JsonElement? Object(JsonElement parent, string name, List<string> issues)
    {
        if (!parent.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Object) return value;
        issues.Add("metadata." + name + "_invalid");
        return null;
    }

    private static bool String(JsonElement parent, string name, int max, out string value)
    {
        value = "";
        if (!parent.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.String) return false;
        value = field.GetString()!;
        return value.Length is > 0 and <= 512 && value.Length <= max && !value.Contains('\0');
    }

    private static bool Bool(JsonElement parent, string name, out bool value)
    {
        value = false;
        if (!parent.TryGetProperty(name, out var field) || field.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        value = field.GetBoolean();
        return true;
    }

    private static int? CheckInt(JsonElement parent, string name, int min, int max, List<string> issues)
    {
        if (parent.TryGetProperty(name, out var field) && field.ValueKind == JsonValueKind.Number &&
            field.TryGetInt32(out var number) && number >= min && number <= max) return number;
        issues.Add("metadata." + name + "_invalid");
        return null;
    }

    private static void CheckLiteral(JsonElement parent, string name, string expected, List<string> issues)
    {
        if (!String(parent, name, 128, out var actual) || actual != expected)
            issues.Add("metadata." + name + "_invalid");
    }

    private static void CheckOneOf(JsonElement parent, string name, IReadOnlyList<string> expected, List<string> issues)
    {
        if (!String(parent, name, 64, out var actual) || !expected.Contains(actual, StringComparer.Ordinal))
            issues.Add("metadata." + name + "_invalid");
    }

    private static void CheckSampling(JsonElement parent, string name, List<string> issues)
    {
        var block = Object(parent, name, issues);
        if (block is null) { issues.Add("metadata.generation." + name + "_missing"); return; }
        CheckNumber(block.Value, "temperature", 0, 2, issues);
        CheckNumber(block.Value, "topP", double.Epsilon, 1, issues);
        CheckInt(block.Value, "topK", 1, 1000, issues);
    }

    private static void CheckNumber(JsonElement parent, string name, double min, double max, List<string> issues)
    {
        if (!parent.TryGetProperty(name, out var field) || field.ValueKind != JsonValueKind.Number ||
            !field.TryGetDouble(out var number) || !double.IsFinite(number) || number < min || number > max)
            issues.Add("metadata." + name + "_invalid");
    }

    private static bool HashString(JsonElement parent, string name, out string value)
    {
        if (!String(parent, name, 64, out value) || value.Length != 64 || !value.All(char.IsAsciiHexDigit)) return false;
        return true;
    }

    private static bool VerifyFileHash(string path, string expected, KapiLabModelFileAccess files, List<string> issues, string issue)
    {
        if (!files.FileExists(path)) { issues.Add(issue); return false; }
        using var stream = files.OpenRead(path);
        if (Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
        issues.Add(issue);
        return false;
    }

    private static bool SafeRelativePath(string path) =>
        !path.StartsWith('/') && !path.Contains('\\') && !path.Contains(':') && !path.Any(char.IsControl) &&
        !path.Equals("kapicoder_manifest.json", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
        path.Split('/').All(static part => part.Length > 0 && part is not ("." or "..") &&
            !part.EndsWith('.') && !part.EndsWith(' '));

    private static bool IsContained(string root, string file)
    {
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return file.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
