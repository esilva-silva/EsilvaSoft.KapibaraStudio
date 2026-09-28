using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Structural equality of Extended JSON texts: strings compare unescaped, numbers by literal text, arrays by position.</summary>
public static class ExtendedJsonComparer
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = ExtendedJsonFormatter.MaxDepth };

    /// <param name="left">First JSON text.</param>
    /// <param name="right">Second JSON text.</param>
    /// <param name="ignoreObjectOrder">
    /// False matches BSON document equality (field order matters). True tolerates reordered fields, e.g. integer-like keys
    /// enumerated first by JavaScript; objects with duplicate names always compare in order.
    /// </param>
    public static bool AreEquivalent(string left, string right, bool ignoreObjectOrder = false)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        try
        {
            using var a = JsonDocument.Parse(left, Options);
            using var b = JsonDocument.Parse(right, Options);
            return Equal(a.RootElement, b.RootElement, ignoreObjectOrder);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Same comparison over already parsed elements.</summary>
    public static bool AreEquivalent(JsonElement left, JsonElement right, bool ignoreObjectOrder = false) => Equal(left, right, ignoreObjectOrder);

    private static bool Equal(JsonElement a, JsonElement b, bool ignoreObjectOrder)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.String:
                return string.Equals(a.GetString(), b.GetString(), StringComparison.Ordinal);
            case JsonValueKind.Number:
                return string.Equals(a.GetRawText(), b.GetRawText(), StringComparison.Ordinal);
            case JsonValueKind.Array:
                if (a.GetArrayLength() != b.GetArrayLength()) return false;
                using (var left = a.EnumerateArray().GetEnumerator())
                using (var right = b.EnumerateArray().GetEnumerator())
                {
                    while (left.MoveNext() && right.MoveNext())
                        if (!Equal(left.Current, right.Current, ignoreObjectOrder)) return false;
                }
                return true;
            case JsonValueKind.Object:
                return EqualObjects(a, b, ignoreObjectOrder);
            default:
                return true;
        }
    }

    private static bool EqualObjects(JsonElement a, JsonElement b, bool ignoreObjectOrder)
    {
        var left = a.EnumerateObject().ToArray();
        var right = b.EnumerateObject().ToArray();
        if (left.Length != right.Length) return false;
        if (ignoreObjectOrder)
        {
            var names = new Dictionary<string, JsonElement>(right.Length, StringComparer.Ordinal);
            var unique = right.All(property => names.TryAdd(property.Name, property.Value)) && left.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == left.Length;
            if (unique) return left.All(property => names.TryGetValue(property.Name, out var other) && Equal(property.Value, other, ignoreObjectOrder));
        }
        for (var i = 0; i < left.Length; i++)
            if (!string.Equals(left[i].Name, right[i].Name, StringComparison.Ordinal) || !Equal(left[i].Value, right[i].Value, ignoreObjectOrder)) return false;
        return true;
    }
}
