using System.Text.Json;
using System.Text.Json.Nodes;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static class AgentFixtureResponseComparison
{
    private static readonly string[] AllowedFields = ["executionId", "proposalId", "durationMs", "observedAt", "workspaceFolder", "connectionId", "id"];
    internal static void ValidateFields(string[]? fields)
    {
        if (fields is not null && (fields.Length > AllowedFields.Length || fields.Any(field => !AllowedFields.Contains(field, StringComparer.Ordinal))))
            throw new InvalidDataException("Tolerância limitada aos campos voláteis documentados.");
    }
    internal static bool Matches(string expected, string actual, string[]? fields)
    {
        ValidateFields(fields);
        if (fields is null or { Length: 0 }) return expected == actual;
        return JsonNode.DeepEquals(Normalize(expected, fields), Normalize(actual, fields));
    }
    private static JsonObject Normalize(string response, string[] fields)
    {
        var envelope = JsonNode.Parse(response)!.AsObject();
        if (envelope["truncated"]?.GetValue<bool>() != true && envelope["result"] is JsonValue text && text.TryGetValue<string>(out var data))
        {
            try
            {
                var node = JsonNode.Parse(data);
                Visit(node, fields);
                envelope["result"] = node;
            }
            catch (JsonException) { }
        }
        return envelope;
    }
    private static void Visit(JsonNode? node, string[] fields)
    {
        if (node is JsonObject obj)
            foreach (var name in obj.Select(item => item.Key).ToArray())
            {
                if (fields.Contains(name, StringComparer.Ordinal)) obj[name] = "<volatile>";
                else Visit(obj[name], fields);
            }
        else if (node is JsonArray array) foreach (var item in array) Visit(item, fields);
    }
}
