using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>Finds conservative ordered-key prefix candidates; it never decides that an index is safe to remove.</summary>
public static class IndexRedundancyAnalyzer
{
    public static IReadOnlyList<IndexRedundancyFinding> Analyze(IEnumerable<string> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        var indexes = definitions.Select(TryParse).Where(static index => index is not null).Select(static index => index!).ToArray();
        var findings = new List<IndexRedundancyFinding>();

        foreach (var candidate in indexes)
        {
            if (!candidate.IsEligible) continue;
            foreach (var covering in indexes)
            {
                if (ReferenceEquals(candidate, covering)
                    || covering.Keys.Count <= candidate.Keys.Count
                    || !covering.IsEligible
                    || !SamePolicy(candidate, covering)
                    || !IsPrefix(candidate.Keys, covering.Keys)) continue;

                findings.Add(new IndexRedundancyFinding(candidate.Name, covering.Name));
                break;
            }
        }

        return findings;
    }

    private static ParsedIndex? TryParse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("name", out var nameValue)
                || nameValue.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(nameValue.GetString())
                || !root.TryGetProperty("key", out var keysValue)
                || keysValue.ValueKind != JsonValueKind.Object
                || !keysValue.EnumerateObject().Any())
                return null;

            if (root.EnumerateObject().Any(property => property.Name is not (
                "name" or "key" or "ns" or "v" or "unique" or "sparse" or "hidden" or "expireAfterSeconds"
                or "partialFilterExpression" or "collation")))
            {
                // An unfamiliar option can change index semantics. Do not infer redundancy from a shape we do not understand.
                return new ParsedIndex(nameValue.GetString()!, [], false);
            }

            var keys = new List<KeyPart>();
            foreach (var key in keysValue.EnumerateObject())
            {
                if (!TryReadDirection(key.Value, out var direction))
                    return new ParsedIndex(nameValue.GetString()!, [], false);
                keys.Add(new KeyPart(key.Name, direction));
            }

            var excluded = ReadTrue(root, "unique") || ReadTrue(root, "sparse") || ReadTrue(root, "hidden")
                || root.TryGetProperty("expireAfterSeconds", out _);
            // Unknown options are deliberately not guessed. Only known ordinary ordered indexes are considered.
            var eligible = !excluded && keys.All(static key => key.Direction is 1 or -1);
            var partial = root.TryGetProperty("partialFilterExpression", out var partialValue) ? partialValue.GetRawText() : null;
            var collation = root.TryGetProperty("collation", out var collationValue) ? collationValue.GetRawText() : null;
            return new ParsedIndex(nameValue.GetString()!, keys, eligible, partial, collation);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool SamePolicy(ParsedIndex left, ParsedIndex right) =>
        string.Equals(left.PartialFilter, right.PartialFilter, StringComparison.Ordinal)
        && string.Equals(left.Collation, right.Collation, StringComparison.Ordinal);

    private static bool IsPrefix(IReadOnlyList<KeyPart> prefix, IReadOnlyList<KeyPart> keys)
    {
        for (var index = 0; index < prefix.Count; index++)
        {
            if (!string.Equals(prefix[index].Name, keys[index].Name, StringComparison.Ordinal)
                || prefix[index].Direction != keys[index].Direction) return false;
        }
        return true;
    }

    private static bool ReadTrue(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    private static bool TryReadDirection(JsonElement value, out double direction)
    {
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetDouble(out direction);
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var wrapper in new[] { "$numberInt", "$numberLong", "$numberDouble", "$numberDecimal" })
            {
                if (value.TryGetProperty(wrapper, out var wrapped)
                    && double.TryParse(wrapped.ToString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out direction)) return true;
            }
        }
        direction = default;
        return false;
    }

    private sealed record KeyPart(string Name, double Direction);
    private sealed record ParsedIndex(string Name, IReadOnlyList<KeyPart> Keys, bool IsEligible, string? PartialFilter = null, string? Collation = null);
}

public sealed record IndexRedundancyFinding(string CandidateIndex, string CoveringIndex);
