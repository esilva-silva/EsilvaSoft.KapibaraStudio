using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static class JsonContractValidation
{
    internal static void RequireUniqueProperties(ReadOnlyMemory<byte> utf8Json, int maximumDepth = 32)
    {
        using var document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions { MaxDepth = maximumDepth });
        RequireUniqueProperties(document.RootElement);
    }

    internal static void RequireUniqueProperties(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            // Web-default deserialization is case-insensitive, so case variants are ambiguous too.
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException("JSON contém propriedades duplicadas.");
                RequireUniqueProperties(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) RequireUniqueProperties(item);
        }
    }
}
