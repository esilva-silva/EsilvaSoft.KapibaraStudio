using System.Collections.Frozen;
using System.Text.Json;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Closed validation for the literal inputs of unitary write tools (lote 10). Same parser and allowlist philosophy as
/// the read codec: strict JSON, known EJSON type wrappers, no <c>ENV</c>, constructors, JavaScript, aggregation
/// pipeline updates, upsert-only operators or unknown <c>$</c> names. Nothing is evaluated or rewritten.
/// </summary>
public static partial class AgentToolLiteralEjson
{
    public const int MaximumIndexKeys = 32;

    // $setOnInsert only acts on upsert, which is outside the baseline; $bit/$pullAll/positional forms are not needed
    // for the first unitary writes and stay denied until reviewed.
    private static readonly FrozenSet<string> UpdateOperators = new[]
    {
        "$set", "$unset", "$inc", "$mul", "$min", "$max", "$rename", "$currentDate", "$push", "$addToSet", "$pop",
        "$pull"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> NumericWrappers = new[]
    {
        "$numberInt", "$numberLong", "$numberDouble", "$numberDecimal"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> IndexKinds = new[] { "hashed", "2dsphere" }
        .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>A document to insert: pure data at every level (type wrappers allowed, operator names denied).</summary>
    public static bool IsLiteralDocument(string? json, int maximumBytes = MaximumInputBytes) =>
        TryParse(json, maximumBytes, element => element.ValueKind == JsonValueKind.Object &&
            !IsTypeWrapper(element, out _) && ValidateLiteral(element) &&
            (!element.TryGetProperty("_id", out var id) || id.ValueKind != JsonValueKind.Array));

    /// <summary>
    /// An exact <c>_id</c> value for update/delete. Unlike <see cref="IsLiteralValue"/>, query operators are denied:
    /// the value is matched by equality and must identify one document, never a range.
    /// </summary>
    public static bool IsLiteralIdentifier(string? json, int maximumBytes = MaximumInputBytes) =>
        TryParse(json, maximumBytes, element => element.ValueKind != JsonValueKind.Array &&
            element.ValueKind != JsonValueKind.Undefined && ValidateLiteral(element));

    /// <summary>
    /// An update-operator document applied to one <c>_id</c>. Pipeline updates, replacement documents, upsert-only
    /// operators, positional paths and changes to <c>_id</c> are denied.
    /// </summary>
    public static bool IsUpdateDocument(string? json, int maximumBytes = MaximumInputBytes) =>
        TryParse(json, maximumBytes, ValidateUpdateDocument);

    /// <summary>Index key specification: 1..32 ordered fields with direction 1/-1, <c>hashed</c> or <c>2dsphere</c>.</summary>
    public static bool IsIndexKeys(string? json, int maximumBytes = MaximumInputBytes) =>
        TryParse(json, maximumBytes, ValidateIndexKeys);

    private static bool ValidateUpdateDocument(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object) return false;
        var operators = document.EnumerateObject().ToArray();
        if (operators.Length == 0) return false;
        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var update in operators)
        {
            if (!UpdateOperators.Contains(update.Name) || update.Value.ValueKind != JsonValueKind.Object) return false;
            var fields = update.Value.EnumerateObject().ToArray();
            if (fields.Length == 0) return false;
            foreach (var field in fields)
            {
                if (!IsUpdatePath(field.Name) || !touched.Add(field.Name)) return false;
                var valid = update.Name switch
                {
                    "$set" or "$min" or "$max" or "$unset" => ValidateLiteral(field.Value),
                    "$inc" or "$mul" => IsNumericLiteral(field.Value),
                    "$rename" => field.Value.ValueKind == JsonValueKind.String &&
                        IsUpdatePath(field.Value.GetString()) && touched.Add(field.Value.GetString()!),
                    "$currentDate" => field.Value.ValueKind == JsonValueKind.True ||
                        IsSingleProperty(field.Value, "$type", out var type) && type.ValueKind == JsonValueKind.String &&
                        type.GetString() is "date" or "timestamp",
                    "$push" => ValidateArrayAppend(field.Value, allowPushModifiers: true),
                    "$addToSet" => ValidateArrayAppend(field.Value, allowPushModifiers: false),
                    "$pop" => field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out var pop) &&
                        pop is 1 or -1,
                    // $pull removes matching elements of this document only; its condition is a read-only query.
                    "$pull" => ValidateFieldCondition(field.Value) ||
                        field.Value.ValueKind == JsonValueKind.Object && ValidateQueryDocument(field.Value),
                    _ => false
                };
                if (!valid) return false;
            }
        }
        return true;
    }

    private static bool IsNumericLiteral(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number ||
        IsTypeWrapper(value, out var wrapper) && NumericWrappers.Contains(wrapper!) && ValidateTypeWrapper(value);

    private static bool ValidateArrayAppend(JsonElement value, bool allowPushModifiers)
    {
        if (value.ValueKind != JsonValueKind.Object || IsTypeWrapper(value, out _)) return ValidateLiteral(value);
        var properties = value.EnumerateObject().ToArray();
        if (properties.Length == 0 || !IsOperatorName(properties[0].Name)) return ValidateLiteral(value);
        var hasEach = false;
        foreach (var property in properties)
        {
            var valid = property.Name switch
            {
                "$each" => property.Value.ValueKind == JsonValueKind.Array &&
                    property.Value.EnumerateArray().All(ValidateLiteral) && (hasEach = true),
                "$slice" or "$position" when allowPushModifiers => property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out _),
                "$sort" when allowPushModifiers => property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out var direction) && direction is 1 or -1 ||
                    property.Value.ValueKind == JsonValueKind.Object && ValidateSort(property.Value),
                _ => false
            };
            if (!valid) return false;
        }
        return hasEach;
    }

    private static bool ValidateIndexKeys(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object) return false;
        var count = 0;
        foreach (var property in document.EnumerateObject())
        {
            if (++count > MaximumIndexKeys || !IsIndexPath(property.Name)) return false;
            var valid = property.Value.ValueKind switch
            {
                JsonValueKind.Number => property.Value.TryGetInt32(out var direction) && direction is 1 or -1 &&
                    property.Value.GetRawText() is "1" or "-1",
                JsonValueKind.String => IndexKinds.Contains(property.Value.GetString()!),
                _ => false
            };
            if (!valid) return false;
        }
        return count > 0;
    }

    // Plain dotted path: no operator/positional segment ($, $[], $[id]) and never the immutable _id.
    private static bool IsUpdatePath(string? path) =>
        IsIndexPath(path) && path != "_id" && !path!.StartsWith("_id.", StringComparison.Ordinal);

    private static bool IsIndexPath(string? path)
    {
        if (path is not { Length: > 0 and <= 1024 } || path.Any(char.IsControl)) return false;
        foreach (var segment in path.Split('.'))
        {
            if (segment.Length == 0 || segment.StartsWith('$')) return false;
        }
        return true;
    }
}
