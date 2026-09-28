using System.Globalization;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Recognizes BSON types expressed as Canonical (and legacy) Extended JSON without converting their values.</summary>
public static class ExtendedJsonValue
{
    public static ExtendedJsonDescription Describe(JsonElement value, UuidRepresentation representation) =>
        Describe(value, new IdentifierDisplayOptions(IdentifierRepresentationMode.Standard, representation));

    public static ExtendedJsonDescription Describe(JsonElement value, IdentifierDisplayOptions options)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String: return Scalar("String", value.GetRawText());
            case JsonValueKind.Number: return Scalar("Número", value.GetRawText());
            case JsonValueKind.True or JsonValueKind.False: return Scalar("Boolean", value.GetRawText());
            case JsonValueKind.Null: return Scalar("Null", "null");
            case JsonValueKind.Array:
                var length = value.GetArrayLength();
                return new("Array", length == 0 ? "[]" : Count(length, "item", "itens"), ExtendedJsonShape.Array, length);
            case JsonValueKind.Object:
                return DescribeObject(value, options);
            default:
                return Scalar("Desconhecido", value.GetRawText());
        }
    }

    private static ExtendedJsonDescription DescribeObject(JsonElement value, IdentifierDisplayOptions options)
    {
        var count = 0;
        JsonProperty first = default, second = default;
        foreach (var property in value.EnumerateObject())
        {
            if (count == 0) first = property; else if (count == 1) second = property;
            count++;
        }
        var wrapper = count switch
        {
            1 => DescribeWrapper(value, first, options),
            2 => DescribePair(first, second),
            _ => null
        };
        return wrapper ?? new("Objeto", count == 0 ? "{}" : Count(count, "campo", "campos"), ExtendedJsonShape.Document, count);
    }

    private static ExtendedJsonDescription? DescribeWrapper(JsonElement element, JsonProperty property, IdentifierDisplayOptions identifiers)
    {
        var inner = property.Value;
        var representation = identifiers.Uuid;
        return property.Name switch
        {
            // The type stays ObjectId in every mode; UUID v4 mode only appends the alternative UUID text.
            "$oid" when IdentifierRepresentationService.TryGetObjectId(element, out var hex) => Scalar("ObjectId", IdentifierRepresentationService.DescribeObjectId(hex, identifiers)),
            "$oid" when inner.ValueKind == JsonValueKind.String => Scalar("ObjectId", "ObjectId(" + inner.GetRawText() + ")"),
            "$numberInt" when inner.ValueKind == JsonValueKind.String => Scalar("Int32", inner.GetString()!),
            "$numberLong" when inner.ValueKind == JsonValueKind.String => Scalar("Int64", inner.GetString()!),
            "$numberDouble" when inner.ValueKind == JsonValueKind.String => Scalar("Double", inner.GetString()!),
            "$numberDecimal" when inner.ValueKind == JsonValueKind.String => Scalar("Decimal128", inner.GetString()!),
            "$date" => BsonDatePresentation.Format(element) is { } date ? Scalar("Date", date) : DescribeDate(inner),
            "$binary" when inner.ValueKind == JsonValueKind.Object => DescribeBinary(element, inner, representation),
            "$regularExpression" when TryStrings(inner, "pattern", "options", out var pattern, out var options) => Scalar("Regex", "/" + pattern + "/" + options),
            "$timestamp" when inner.ValueKind == JsonValueKind.Object && inner.TryGetProperty("t", out var t) && inner.TryGetProperty("i", out var i)
                => Scalar("Timestamp", "Timestamp(" + t.GetRawText() + ", " + i.GetRawText() + ")"),
            "$minKey" => Scalar("MinKey", "MinKey"),
            "$maxKey" => Scalar("MaxKey", "MaxKey"),
            "$undefined" => Scalar("Undefined", "undefined"),
            "$symbol" when inner.ValueKind == JsonValueKind.String => Scalar("Symbol", inner.GetRawText()),
            "$code" when inner.ValueKind == JsonValueKind.String => Scalar("JavaScript", inner.GetRawText()),
            "$dbPointer" when inner.ValueKind == JsonValueKind.Object => Scalar("DBPointer", inner.GetRawText()),
            _ => null
        };
    }

    private static ExtendedJsonDescription? DescribePair(JsonProperty first, JsonProperty second)
    {
        if (first.Name == "$code" && second.Name == "$scope" && first.Value.ValueKind == JsonValueKind.String)
            return Scalar("JavaScript com escopo", first.Value.GetRawText());
        if (first.Name == "$binary" && second.Name == "$type" && first.Value.ValueKind == JsonValueKind.String && second.Value.ValueKind == JsonValueKind.String)
            return Scalar("Binary " + second.Value.GetString(), "base64 " + first.Value.GetRawText());
        if (first.Name == "$regex" && second.Name == "$options" && first.Value.ValueKind == JsonValueKind.String && second.Value.ValueKind == JsonValueKind.String)
            return Scalar("Regex", "/" + first.Value.GetString() + "/" + second.Value.GetString());
        return null;
    }

    private static ExtendedJsonDescription? DescribeDate(JsonElement inner)
    {
        if (inner.ValueKind == JsonValueKind.String) return Scalar("Date", inner.GetString()!);
        if (inner.ValueKind != JsonValueKind.Object || !inner.TryGetProperty("$numberLong", out var number) || number.ValueKind != JsonValueKind.String) return null;
        var text = number.GetString()!;
        // Dates outside the .NET range keep their exact milliseconds instead of being clamped.
        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var milliseconds)
            && milliseconds >= DateTimeOffset.MinValue.ToUnixTimeMilliseconds() && milliseconds <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()
            ? Scalar("Date", DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture))
            : Scalar("Date", text + " ms desde 1970-01-01T00:00:00Z");
    }

    private static ExtendedJsonDescription? DescribeBinary(JsonElement element, JsonElement binary, UuidRepresentation representation)
    {
        if (!TryStrings(binary, "base64", "subType", out var base64, out var subType)) return null;
        var uuid = UuidCodec.Describe(element, representation);
        if (uuid.Kind == UuidDisplayKind.Uuid)
            return Scalar(subType.TrimStart('0') == "3" ? "UUID " + UuidCodec.DisplayName(representation) : "UUID", uuid.Text);
        if (uuid.Kind == UuidDisplayKind.UnknownLegacy)
            return Scalar("Binary 03 · UUID legado", "origem desconhecida · base64 \"" + base64 + "\"");
        var bytes = (base64.Length * 3 / 4) - (base64.EndsWith("==", StringComparison.Ordinal) ? 2 : base64.EndsWith('=') ? 1 : 0);
        return Scalar("Binary " + subType, Count(bytes, "byte", "bytes") + " · base64 \"" + base64 + "\"");
    }

    private static bool TryStrings(JsonElement element, string firstName, string secondName, out string first, out string second)
    {
        first = second = "";
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(firstName, out var a) || !element.TryGetProperty(secondName, out var b)
            || a.ValueKind != JsonValueKind.String || b.ValueKind != JsonValueKind.String) return false;
        first = a.GetString()!;
        second = b.GetString()!;
        return true;
    }

    private static ExtendedJsonDescription Scalar(string type, string display) => new(type, display, ExtendedJsonShape.Scalar, 0);
    private static string Count(int count, string singular, string plural) => count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? singular : plural);
}
