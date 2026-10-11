using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Application.Agents;

/// <summary>Offline mirror of the fixed Qwen3 role/content template. Never replaces a package template in production.</summary>
public static class Qwen3ChatMlFallbackRenderer
{
    public const string DisabledReasoningSuffix = "<think>\n\n</think>\n\n";

    public static string Render(ModelChatPrompt prompt, string? disabledReasoningSuffix = DisabledReasoningSuffix)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (prompt.Messages.Count is < 1 or > 128) throw new ArgumentException("Quantidade de mensagens inválida.");
        var messages = prompt.Messages;
        var output = new StringBuilder();
        using var tools = JsonDocument.Parse(prompt.ToolsJson ?? "[]");
        if (tools.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("Tools devem ser uma lista.");
        if (tools.RootElement.GetArrayLength() > 0)
        {
            output.Append("<|im_start|>system\n");
            if (messages[0].Role == "system") output.Append(messages[0].Content).Append("\n\n");
            output.Append("# Tools\n\nYou may call one or more functions to assist with the user query.\n\nYou are provided with function signatures within <tools></tools> XML tags:\n<tools>");
            foreach (var tool in tools.RootElement.EnumerateArray()) output.Append('\n').Append(NativeToolJson(tool));
            output.Append("\n</tools>\n\nFor each function call, return a json object with function name and arguments within <tool_call></tool_call> XML tags:\n<tool_call>\n{\"name\": <function-name>, \"arguments\": <args-json-object>}\n</tool_call><|im_end|>\n");
        }
        else if (messages[0].Role == "system")
            output.Append("<|im_start|>system\n").Append(messages[0].Content).Append("<|im_end|>\n");

        var lastQuery = messages.Count - 1;
        for (var i = messages.Count - 1; i >= 0; i--)
            if (messages[i].Role == "user" && !(messages[i].Content.StartsWith("<tool_response>", StringComparison.Ordinal) &&
                messages[i].Content.EndsWith("</tool_response>", StringComparison.Ordinal))) { lastQuery = i; break; }
        for (var i = 0; i < messages.Count; i++)
        {
            var (role, content) = messages[i];
            if (role == "system" && i == 0) continue;
            if (role is "system" or "user")
                output.Append("<|im_start|>").Append(role).Append('\n').Append(content).Append("<|im_end|>\n");
            else if (role == "assistant")
            {
                // GenAI role/content messages do not extract reasoning_content from raw <think> text.
                output.Append("<|im_start|>assistant\n");
                if (i > lastQuery && i == messages.Count - 1)
                    output.Append("<think>\n\n</think>\n\n").Append(content.TrimStart('\n'));
                else output.Append(content);
                output.Append("<|im_end|>\n");
            }
            else if (role == "tool")
            {
                if (i == 0 || messages[i - 1].Role != "tool") output.Append("<|im_start|>user");
                output.Append("\n<tool_response>\n").Append(content).Append("\n</tool_response>");
                if (i == messages.Count - 1 || messages[i + 1].Role != "tool") output.Append("<|im_end|>\n");
            }
            else throw new ArgumentException("Role inválido.");
        }
        if (prompt.AddGenerationPrompt) output.Append("<|im_start|>assistant\n");
        if (prompt.DisableReasoning)
        {
            if (!prompt.AddGenerationPrompt || disabledReasoningSuffix is null) throw new ArgumentException("Sufixo de raciocínio indisponível.");
            output.Append(disabledReasoningSuffix);
        }
        return output.ToString();
    }

    // GenAI 0.15.2 flattens function schemas before Jinja. This is intentionally not the original HF tool JSON.
    private static string NativeToolJson(JsonElement tool)
    {
        var function = tool.GetProperty("function");
        var parameters = new List<string>();
        if (function.GetProperty("parameters").TryGetProperty("properties", out var properties))
            foreach (var property in properties.EnumerateObject())
            {
                var fields = new List<string>();
                if (property.Value.TryGetProperty("type", out var type))
                {
                    var name = type.ValueKind == JsonValueKind.String ? type.GetString()! : "string";
                    fields.Add("\"type\": " + Quote(name == "string" ? "str" : name));
                }
                if (property.Value.TryGetProperty("description", out var description))
                    fields.Add("\"description\": " + TemplateJson(description));
                parameters.Add(Quote(property.Name) + ": {" + string.Join(", ", fields) + "}");
            }
        return "{\"name\": " + Quote(function.GetProperty("name").GetString()!) + ", \"description\": " +
            Quote(function.GetProperty("description").GetString()!) + ", \"parameters\": {" + string.Join(", ", parameters) + "}}";
    }

    // minja tojson uses spaces after separators and unescaped Unicode.
    private static string TemplateJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(", ", value.EnumerateObject().Select(p => Quote(p.Name) + ": " + TemplateJson(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(", ", value.EnumerateArray().Select(TemplateJson)) + "]",
        JsonValueKind.String => Quote(value.GetString()!),
        _ => value.GetRawText(),
    };
    private static string Quote(string value)
    {
        var output = new StringBuilder("\"");
        foreach (var rune in value.EnumerateRunes())
            output.Append(rune.Value switch
            {
                34 => "\\\"", 92 => "\\\\", 8 => "\\b", 9 => "\\t", 10 => "\\n", 12 => "\\f", 13 => "\\r",
                < 32 => "\\u" + rune.Value.ToString("x4", System.Globalization.CultureInfo.InvariantCulture),
                _ => rune.ToString(),
            });
        return output.Append('"').ToString();
    }
}
