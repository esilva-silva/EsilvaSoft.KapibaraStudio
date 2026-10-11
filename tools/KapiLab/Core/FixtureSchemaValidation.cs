using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Validates the output-schema vocabulary currently published by the registry; unknown assertions fail closed.</summary>
internal static class FixtureSchemaValidation
{
    internal static void Validate(string schemaJson, string dataJson)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        using var data = JsonDocument.Parse(dataJson);
        Check(schema.RootElement, data.RootElement, "$", 0);
    }

    private static void Check(JsonElement schema, JsonElement data, string path, int depth)
    {
        if (depth > 64) throw new InvalidDataException("Schema excede a profundidade permitida.");
        foreach (var property in schema.EnumerateObject())
            if (property.Name is not ("$schema" or "$id" or "title" or "description" or "type" or "properties" or
                "required" or "additionalProperties" or "items" or "maxItems" or "minItems" or "maxLength" or
                "minLength" or "minimum" or "maximum" or "enum" or "const" or "format"))
                throw new InvalidDataException($"Assertion de schema sem suporte: {property.Name}.");
        if (schema.TryGetProperty("type", out var type))
        {
            var types = type.ValueKind == JsonValueKind.Array ? type.EnumerateArray().Select(item => item.GetString()).ToArray() : [type.GetString()];
            var valid = types.Any(value => value switch
            {
                "object" => data.ValueKind == JsonValueKind.Object,
                "array" => data.ValueKind == JsonValueKind.Array,
                "string" => data.ValueKind == JsonValueKind.String,
                "boolean" => data.ValueKind is JsonValueKind.True or JsonValueKind.False,
                "null" => data.ValueKind == JsonValueKind.Null,
                "integer" => data.ValueKind == JsonValueKind.Number && data.TryGetDecimal(out var number) && decimal.Truncate(number) == number,
                "number" => data.ValueKind == JsonValueKind.Number,
                _ => false,
            });
            if (!valid) Fail(path, "type");
        }
        if (schema.TryGetProperty("const", out var constant) && !JsonElement.DeepEquals(constant, data)) Fail(path, "const");
        if (schema.TryGetProperty("enum", out var values) && !values.EnumerateArray().Any(item => JsonElement.DeepEquals(item, data))) Fail(path, "enum");
        if (data.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required))
                foreach (var name in required.EnumerateArray())
                    if (!data.TryGetProperty(name.GetString()!, out _)) Fail(path, "required:" + name.GetString());
            var hasProperties = schema.TryGetProperty("properties", out var properties);
            foreach (var property in data.EnumerateObject())
            {
                if (hasProperties && properties.TryGetProperty(property.Name, out var child)) Check(child, property.Value, path + "." + property.Name, depth + 1);
                else if (schema.TryGetProperty("additionalProperties", out var additional) && additional.ValueKind == JsonValueKind.False) Fail(path, "additionalProperties:" + property.Name);
            }
        }
        if (data.ValueKind == JsonValueKind.Array)
        {
            Bounds(schema, data.GetArrayLength(), "minItems", "maxItems", path);
            if (schema.TryGetProperty("items", out var items))
                foreach (var item in data.EnumerateArray()) Check(items, item, path + "[]", depth + 1);
        }
        if (data.ValueKind == JsonValueKind.String)
        {
            var value = data.GetString()!;
            Bounds(schema, value.EnumerateRunes().Count(), "minLength", "maxLength", path);
            if (schema.TryGetProperty("format", out var format))
            {
                if (format.GetString() == "uuid" && !Guid.TryParseExact(value, "D", out _)) Fail(path, "uuid");
                if (format.GetString() == "date-time" && !DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out _)) Fail(path, "date-time");
                if (format.GetString() is not ("uuid" or "date-time")) Fail(path, "format sem suporte");
            }
        }
        if (data.ValueKind == JsonValueKind.Number) Bounds(schema, data.GetDouble(), "minimum", "maximum", path);
    }
    private static void Bounds(JsonElement schema, double value, string min, string max, string path)
    {
        if (schema.TryGetProperty(min, out var minimum) && value < minimum.GetDouble()) Fail(path, min);
        if (schema.TryGetProperty(max, out var maximum) && value > maximum.GetDouble()) Fail(path, max);
    }
    private static void Fail(string path, string assertion) => throw new InvalidDataException($"Saída incompatível com schema: {path} ({assertion}).");
}
