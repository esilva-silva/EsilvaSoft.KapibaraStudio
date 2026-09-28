using System.Globalization;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>UTC shell dates, with millisecond precision and lossless canonical fallback.</summary>
public static class BsonDatePresentation
{
    private static readonly string[] Formats = ["yyyy-MM-dd HH:mm:ss.FFF", "yyyy-MM-dd'T'HH:mm:ss.FFFK", "yyyy-MM-dd HH:mm:ss.FFFzzz", "yyyy-MM-dd"];

    public static bool TryParse(string text, out DateTimeOffset value) => DateTimeOffset.TryParseExact(
        text, Formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);

    public static string ToExtendedJson(string text)
    {
        if (!TryParse(text, out var value)) throw new FormatException("Data inválida. Use ISODate(\"2024-12-30T20:56:44.999Z\") ou ISO 8601 com offset.");
        return "{\"$date\":{\"$numberLong\":\"" + value.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + "\"}}";
    }

    public static string? Format(JsonElement wrapper)
    {
        if (wrapper.ValueKind != JsonValueKind.Object || wrapper.EnumerateObject().Count() != 1 || !wrapper.TryGetProperty("$date", out var inner)) return null;
        DateTimeOffset value;
        if (inner.ValueKind == JsonValueKind.String)
        {
            if (!TryParse(inner.GetString()!, out value)) return null;
        }
        else
        {
            if (inner.ValueKind != JsonValueKind.Object || inner.EnumerateObject().Count() != 1 || !inner.TryGetProperty("$numberLong", out var number)
                || number.ValueKind != JsonValueKind.String || !long.TryParse(number.GetString(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var milliseconds)
                || milliseconds < DateTimeOffset.MinValue.ToUnixTimeMilliseconds() || milliseconds > DateTimeOffset.MaxValue.ToUnixTimeMilliseconds()) return null;
            value = DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
        }
        return "ISODate(\"" + value.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) + "\")";
    }
}
