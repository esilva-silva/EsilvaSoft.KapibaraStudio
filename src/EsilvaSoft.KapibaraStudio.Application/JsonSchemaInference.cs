using System.Text.Json;
using System.Text.Json.Nodes;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Infers a JSON Schema ($jsonSchema-style, bsonType) document from sampled Extended JSON documents.</summary>
public static class JsonSchemaInference
{
    public static string Infer(IEnumerable<string> documents, int maximumDepth = 12)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        var root = new SchemaNode();
        foreach (var document in documents)
        {
            try
            {
                using var json = JsonDocument.Parse(document);
                AddSchema(json.RootElement, root, 0, maximumDepth);
            }
            catch (JsonException)
            {
                // A inferência permanece útil quando um documento isolado não pode ser lido.
            }
        }

        return ToSchema(root, true).ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void AddSchema(JsonElement element, SchemaNode node, int depth, int maximumDepth)
    {
        if (depth >= maximumDepth)
        {
            return;
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (TryGetExtendedJsonType(element, out var extendedType))
                {
                    node.Types.Add(extendedType);
                    return;
                }

                node.Types.Add("object");
                foreach (var property in element.EnumerateObject())
                {
                    if (!node.Properties.TryGetValue(property.Name, out var child))
                    {
                        child = new SchemaNode();
                        node.Properties[property.Name] = child;
                    }

                    AddSchema(property.Value, child, depth + 1, maximumDepth);
                }

                break;
            case JsonValueKind.Array:
                node.Types.Add("array");
                node.Items ??= new SchemaNode();
                foreach (var item in element.EnumerateArray())
                {
                    AddSchema(item, node.Items, depth + 1, maximumDepth);
                }

                break;
            case JsonValueKind.String: node.Types.Add("string"); break;
            case JsonValueKind.Number: node.Types.Add("double"); break;
            case JsonValueKind.True:
            case JsonValueKind.False: node.Types.Add("bool"); break;
            case JsonValueKind.Null: node.Types.Add("null"); break;
        }
    }

    private static bool TryGetExtendedJsonType(JsonElement element, out string type)
    {
        type = string.Empty;
        var properties = element.EnumerateObject().ToArray();
        if (properties.Length != 1)
        {
            return false;
        }

        type = properties[0].Name switch
        {
            "$oid" => "objectId",
            "$date" => "date",
            "$numberInt" => "int",
            "$numberLong" => "long",
            "$numberDouble" => "double",
            "$numberDecimal" => "decimal",
            "$binary" => "binData",
            "$regularExpression" => "regex",
            "$timestamp" => "timestamp",
            _ => string.Empty
        };
        return type.Length > 0;
    }

    private static JsonObject ToSchema(SchemaNode node, bool isRoot = false)
    {
        var schema = new JsonObject();
        var types = node.Types.OrderBy(type => type, StringComparer.Ordinal).ToArray();
        if (isRoot && types.Length == 0)
        {
            schema["bsonType"] = "object";
        }
        else if (types.Length == 1)
        {
            schema["bsonType"] = types[0];
        }
        else if (types.Length > 1)
        {
            schema["bsonType"] = new JsonArray(types.Select(value => JsonValue.Create(value)).ToArray());
        }

        if (node.Properties.Count > 0)
        {
            var properties = new JsonObject();
            foreach (var property in node.Properties.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                properties[property.Key] = ToSchema(property.Value);
            }

            schema["properties"] = properties;
        }

        if (node.Items is not null && (node.Items.Types.Count > 0 || node.Items.Properties.Count > 0))
        {
            schema["items"] = ToSchema(node.Items);
        }

        return schema;
    }

    private sealed class SchemaNode
    {
        public HashSet<string> Types { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, SchemaNode> Properties { get; } = new(StringComparer.Ordinal);

        public SchemaNode? Items { get; set; }
    }
}
