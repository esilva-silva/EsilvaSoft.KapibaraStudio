using System.Buffers;
using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.SlopStudio.Application.Agents;

/// <summary>
/// Closed validator for Extended JSON supplied to agent tools. Accepts strict JSON only: shell constructors,
/// <c>ENV</c> resolution, JavaScript and unknown <c>$</c> operators are rejected. Validation is an allowlist over
/// query operators, aggregation expressions, pipeline stages and canonical/relaxed EJSON type wrappers, so a
/// construct the parser cannot prove safe is denied. It never evaluates or rewrites the input; the caller parses
/// the same text into BSON afterwards, preserving Int64, Decimal128, dates and binary UUID subtypes.
/// </summary>
public static partial class AgentToolLiteralEjson
{
    public const int MaximumInputBytes = 64 * 1024;
    public const int MaximumDepth = 64;
    public const int MaximumPipelineStages = 32;
    public const int MaximumSampleSize = 100;

    private static readonly FrozenSet<string> TypeWrappers = new[]
    {
        "$oid", "$numberLong", "$numberInt", "$numberDouble", "$numberDecimal", "$date", "$binary", "$uuid",
        "$regularExpression", "$timestamp", "$minKey", "$maxKey"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> LiteralComparisonOperators = new[]
    {
        "$eq", "$ne", "$gt", "$gte", "$lt", "$lte"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> BitOperators = new[]
    {
        "$bitsAllSet", "$bitsAnySet", "$bitsAllClear", "$bitsAnyClear"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> GeoQueryOperators = new[]
    {
        "$geoWithin", "$geoIntersects", "$near", "$nearSphere"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> GeoShapeOperators = new[]
    {
        "$geometry", "$box", "$polygon", "$center", "$centerSphere", "$maxDistance", "$minDistance"
    }.ToFrozenSet(StringComparer.Ordinal);

    // Read-only aggregation expressions. JavaScript ($function/$accumulator), $where and unknown operators
    // are absent by construction; adding an operator requires review of its data and execution effects.
    private static readonly FrozenSet<string> ExpressionOperators = new[]
    {
        "$abs", "$add", "$ceil", "$divide", "$exp", "$floor", "$ln", "$log", "$log10", "$mod", "$multiply",
        "$pow", "$round", "$sqrt", "$subtract", "$trunc",
        "$arrayElemAt", "$arrayToObject", "$concatArrays", "$filter", "$first", "$firstN", "$in",
        "$indexOfArray", "$isArray", "$last", "$lastN", "$map", "$maxN", "$minN", "$objectToArray", "$range",
        "$reduce", "$reverseArray", "$size", "$slice", "$sortArray", "$zip",
        "$and", "$or", "$not",
        "$cmp", "$eq", "$gt", "$gte", "$lt", "$lte", "$ne",
        "$cond", "$ifNull", "$switch",
        "$dateAdd", "$dateDiff", "$dateFromParts", "$dateFromString", "$dateSubtract", "$dateToParts",
        "$dateToString", "$dateTrunc", "$dayOfMonth", "$dayOfWeek", "$dayOfYear", "$hour", "$isoDayOfWeek",
        "$isoWeek", "$isoWeekYear", "$millisecond", "$minute", "$month", "$second", "$week", "$year",
        "$literal", "$getField", "$mergeObjects",
        "$allElementsTrue", "$anyElementTrue", "$setDifference", "$setEquals", "$setIntersection",
        "$setIsSubset", "$setUnion",
        "$concat", "$indexOfBytes", "$indexOfCP", "$ltrim", "$regexFind", "$regexFindAll", "$regexMatch",
        "$replaceOne", "$replaceAll", "$rtrim", "$split", "$strLenBytes", "$strLenCP", "$strcasecmp",
        "$substr", "$substrBytes", "$substrCP", "$toLower", "$toString", "$trim", "$toUpper",
        "$convert", "$isNumber", "$toBool", "$toDate", "$toDecimal", "$toDouble", "$toInt", "$toLong",
        "$toObjectId", "$type",
        "$let", "$avg", "$max", "$min", "$sum", "$stdDevPop", "$stdDevSamp"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> GroupAccumulators = new[]
    {
        "$sum", "$avg", "$min", "$max", "$first", "$last", "$push", "$addToSet", "$count", "$stdDevPop",
        "$stdDevSamp", "$mergeObjects", "$top", "$bottom", "$topN", "$bottomN", "$firstN", "$lastN", "$maxN",
        "$minN"
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> SystemVariables = new[]
    {
        "ROOT", "CURRENT", "NOW", "REMOVE"
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Validates a find/count/distinct filter document.</summary>
    public static bool IsQueryFilter(string? json, int maximumBytes = MaximumInputBytes) =>
        TryParse(json, maximumBytes, element => element.ValueKind == JsonValueKind.Object &&
            ValidateQueryDocument(element));

    /// <summary>
    /// Validates a literal value such as a document identifier. Callers must still wrap it in <c>$eq</c>;
    /// operator-shaped documents are accepted only when every operator is a closed read-only query operator.
    /// </summary>
    public static bool IsLiteralValue(string? json, int maximumBytes = MaximumInputBytes) =>
        TryParse(json, maximumBytes, ValidateFieldCondition);

    /// <summary>
    /// Validates a read-only aggregation pipeline, including nested <c>$facet</c>, <c>$lookup</c> and
    /// <c>$unionWith</c> pipelines. Every collection referenced outside the source collection is returned so the
    /// caller can authorize each namespace; an unauthorized or unprovable namespace must deny the call.
    /// </summary>
    public static bool TryValidatePipeline(string? json, out IReadOnlyList<string> referencedCollections,
        int maximumBytes = MaximumInputBytes)
    {
        var collections = new SortedSet<string>(StringComparer.Ordinal);
        var valid = TryParse(json, maximumBytes, element => ValidatePipeline(element, collections));
        referencedCollections = valid ? collections.ToArray() : [];
        return valid;
    }

    private static bool TryParse(string? json, int maximumBytes, Func<JsonElement, bool> validate)
    {
        if (string.IsNullOrWhiteSpace(json) || maximumBytes < 1 || json.Length > maximumBytes) return false;
        try
        {
            if (Encoding.UTF8.GetByteCount(json) > maximumBytes) return false;
        }
        catch (EncoderFallbackException) { return false; }
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = MaximumDepth,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            return IsWellFormed(document.RootElement) && validate(document.RootElement);
        }
        catch (JsonException) { return false; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    // Structural checks shared by every mode: no duplicate names, no control characters in names and only
    // well-formed UTF-16 in names and strings (BSON requires valid UTF-8).
    private static bool IsWellFormed(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!seen.Add(property.Name) || property.Name.Length == 0 ||
                        property.Name.Any(char.IsControl) || !IsValidUtf16(property.Name) ||
                        !IsWellFormed(property.Value)) return false;
                }
                return true;
            case JsonValueKind.Array:
                return element.EnumerateArray().All(IsWellFormed);
            case JsonValueKind.String:
                return IsValidUtf16(element.GetString());
            default:
                return true;
        }
    }

    private static bool IsValidUtf16(string? value)
    {
        if (value is null) return false;
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out _, out var consumed) != OperationStatus.Done) return false;
            remaining = remaining[consumed..];
        }
        return true;
    }

    private static bool IsOperatorName(string name) => name.StartsWith('$');

    private static bool ValidateQueryDocument(JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object) return false;
        foreach (var property in document.EnumerateObject())
        {
            var value = property.Value;
            if (!IsOperatorName(property.Name))
            {
                if (!ValidateFieldCondition(value)) return false;
                continue;
            }
            var valid = property.Name switch
            {
                "$and" or "$or" or "$nor" => value.ValueKind == JsonValueKind.Array &&
                    value.GetArrayLength() > 0 && value.EnumerateArray().All(ValidateQueryDocument),
                "$expr" => ValidateExpression(value),
                "$text" => ValidateText(value),
                "$comment" => value.ValueKind == JsonValueKind.String,
                "$jsonSchema" => value.ValueKind == JsonValueKind.Object && ValidateLiteral(value),
                _ => false
            };
            if (!valid) return false;
        }
        return true;
    }

    private static bool ValidateText(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        var hasSearch = false;
        foreach (var property in value.EnumerateObject())
        {
            var valid = property.Name switch
            {
                "$search" => value.ValueKind == JsonValueKind.Object &&
                    property.Value.ValueKind == JsonValueKind.String && (hasSearch = true),
                "$language" => property.Value.ValueKind == JsonValueKind.String,
                "$caseSensitive" or "$diacriticSensitive" => property.Value.ValueKind is
                    JsonValueKind.True or JsonValueKind.False,
                _ => false
            };
            if (!valid) return false;
        }
        return hasSearch;
    }

    // Value at a field path. Either an implicit-equality literal or a document made only of query operators.
    private static bool ValidateFieldCondition(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || IsTypeWrapper(value, out _)) return ValidateLiteral(value);
        var properties = value.EnumerateObject().ToArray();
        if (properties.Length == 0 || !IsOperatorName(properties[0].Name)) return ValidateLiteral(value);
        // Mixing operators and field names is ambiguous for the server; deny instead of guessing.
        if (properties.Any(property => !IsOperatorName(property.Name))) return false;
        foreach (var property in properties)
        {
            var operand = property.Value;
            bool valid;
            if (LiteralComparisonOperators.Contains(property.Name))
                valid = ValidateLiteral(operand);
            else if (BitOperators.Contains(property.Name))
                valid = operand.ValueKind == JsonValueKind.Number ||
                    operand.ValueKind == JsonValueKind.Array &&
                        operand.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Number) ||
                    IsTypeWrapper(operand, out var wrapper) && wrapper == "$binary" && ValidateLiteral(operand);
            else if (GeoQueryOperators.Contains(property.Name))
                valid = ValidateGeo(operand);
            else
                valid = property.Name switch
                {
                    "$in" or "$nin" => operand.ValueKind == JsonValueKind.Array &&
                        operand.EnumerateArray().All(ValidateLiteral),
                    "$all" => operand.ValueKind == JsonValueKind.Array &&
                        operand.EnumerateArray().All(item => IsSingleOperator(item, "$elemMatch", out var match)
                            ? ValidateElemMatch(match) : ValidateLiteral(item)),
                    "$exists" => operand.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Number,
                    "$type" => IsTypeAlias(operand) || operand.ValueKind == JsonValueKind.Array &&
                        operand.GetArrayLength() > 0 && operand.EnumerateArray().All(IsTypeAlias),
                    "$regex" => operand.ValueKind == JsonValueKind.String ||
                        IsTypeWrapper(operand, out var regex) && regex == "$regularExpression" && ValidateLiteral(operand),
                    "$options" => operand.ValueKind == JsonValueKind.String,
                    "$not" => operand.ValueKind == JsonValueKind.Object && ValidateFieldCondition(operand),
                    "$elemMatch" => ValidateElemMatch(operand),
                    "$size" => operand.ValueKind == JsonValueKind.Number,
                    "$mod" => operand.ValueKind == JsonValueKind.Array && operand.GetArrayLength() == 2 &&
                        operand.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Number),
                    "$maxDistance" or "$minDistance" => operand.ValueKind == JsonValueKind.Number,
                    _ => false
                };
            if (!valid) return false;
        }
        return true;
    }

    private static bool IsTypeAlias(JsonElement value) =>
        value.ValueKind is JsonValueKind.String or JsonValueKind.Number;

    private static bool ValidateElemMatch(JsonElement operand)
    {
        if (operand.ValueKind != JsonValueKind.Object) return false;
        var properties = operand.EnumerateObject().ToArray();
        if (properties.Length == 0) return false;
        // Operator form applies to scalar elements; otherwise the operand is a query over embedded documents.
        return properties.All(property => IsOperatorName(property.Name)) &&
               properties.All(property => property.Name is not ("$and" or "$or" or "$nor" or "$expr"))
            ? ValidateFieldCondition(operand)
            : ValidateQueryDocument(operand);
    }

    private static bool ValidateGeo(JsonElement operand)
    {
        if (operand.ValueKind == JsonValueKind.Array)
            return operand.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Number);
        if (operand.ValueKind != JsonValueKind.Object) return false;
        var any = false;
        foreach (var property in operand.EnumerateObject())
        {
            any = true;
            if (!GeoShapeOperators.Contains(property.Name)) return false;
            var valid = property.Name is "$maxDistance" or "$minDistance"
                ? property.Value.ValueKind == JsonValueKind.Number
                : ValidateLiteral(property.Value);
            if (!valid) return false;
        }
        return any;
    }

    // Pure data. Documents may use EJSON type wrappers but no operator-named fields.
    private static bool ValidateLiteral(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Array:
                return value.EnumerateArray().All(ValidateLiteral);
            case JsonValueKind.Object:
                if (IsTypeWrapper(value, out _)) return ValidateTypeWrapper(value);
                foreach (var property in value.EnumerateObject())
                    if (IsOperatorName(property.Name) || !ValidateLiteral(property.Value)) return false;
                return true;
            default:
                return true;
        }
    }

    private static bool IsTypeWrapper(JsonElement value, out string? wrapper)
    {
        wrapper = null;
        if (value.ValueKind != JsonValueKind.Object) return false;
        using var enumerator = value.EnumerateObject();
        if (!enumerator.MoveNext() || !TypeWrappers.Contains(enumerator.Current.Name)) return false;
        wrapper = enumerator.Current.Name;
        return true;
    }

    private static bool ValidateTypeWrapper(JsonElement value)
    {
        var properties = value.EnumerateObject().ToArray();
        if (properties.Length != 1) return false;
        var operand = properties[0].Value;
        return properties[0].Name switch
        {
            "$oid" => operand.ValueKind == JsonValueKind.String && operand.GetString() is { Length: 24 } oid &&
                oid.All(char.IsAsciiHexDigit),
            "$numberLong" or "$numberInt" or "$numberDouble" or "$numberDecimal" or "$uuid" =>
                operand.ValueKind == JsonValueKind.String && operand.GetString()!.Length is > 0 and <= 64,
            "$date" => operand.ValueKind == JsonValueKind.String ||
                operand.ValueKind == JsonValueKind.Object && HasExactStringProperties(operand, "$numberLong") &&
                    ValidateTypeWrapper(operand),
            "$binary" => operand.ValueKind == JsonValueKind.Object &&
                HasExactStringProperties(operand, "base64", "subType") &&
                operand.GetProperty("subType").GetString() is { Length: 1 or 2 } subType &&
                subType.All(char.IsAsciiHexDigit),
            "$regularExpression" => operand.ValueKind == JsonValueKind.Object &&
                HasExactStringProperties(operand, "pattern", "options"),
            "$timestamp" => operand.ValueKind == JsonValueKind.Object && HasExactNumberProperties(operand, "t", "i"),
            "$minKey" or "$maxKey" => operand.ValueKind == JsonValueKind.Number &&
                operand.TryGetInt32(out var marker) && marker == 1,
            _ => false
        };
    }

    private static bool HasExactStringProperties(JsonElement value, params string[] names)
    {
        var properties = value.EnumerateObject().ToArray();
        return properties.Length == names.Length && names.All(name =>
            properties.Count(property => property.Name == name && property.Value.ValueKind == JsonValueKind.String) == 1);
    }

    private static bool HasExactNumberProperties(JsonElement value, params string[] names)
    {
        var properties = value.EnumerateObject().ToArray();
        return properties.Length == names.Length && names.All(name =>
            properties.Count(property => property.Name == name && property.Value.ValueKind == JsonValueKind.Number) == 1);
    }

    private static bool IsSingleOperator(JsonElement value, string name, out JsonElement operand)
    {
        operand = default;
        if (value.ValueKind != JsonValueKind.Object) return false;
        var properties = value.EnumerateObject().ToArray();
        if (properties.Length != 1 || properties[0].Name != name) return false;
        operand = properties[0].Value;
        return true;
    }

    private static bool ValidateExpression(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return IsSafeExpressionString(value.GetString()!);
            case JsonValueKind.Array:
                return value.EnumerateArray().All(ValidateExpression);
            case JsonValueKind.Object:
                if (IsTypeWrapper(value, out _)) return ValidateTypeWrapper(value);
                var properties = value.EnumerateObject().ToArray();
                if (properties.Length == 0) return true;
                if (!IsOperatorName(properties[0].Name))
                    return properties.All(property => !IsOperatorName(property.Name) &&
                        ValidateExpression(property.Value));
                if (properties.Length != 1 || !ExpressionOperators.Contains(properties[0].Name)) return false;
                return properties[0].Name == "$literal"
                    ? ValidateLiteral(properties[0].Value)
                    : ValidateExpression(properties[0].Value);
            default:
                return true;
        }
    }

    // "$field.path" reads the current document; "$$var" must be a user variable or a harmless system variable.
    // Variables such as $$USER_ROLES or $$CLUSTER_TIME disclose server state and are denied.
    private static bool IsSafeExpressionString(string value)
    {
        if (!value.StartsWith('$')) return true;
        if (!value.StartsWith("$$", StringComparison.Ordinal)) return value.Length > 1;
        var variable = value[2..];
        var dot = variable.IndexOf('.');
        if (dot >= 0) variable = variable[..dot];
        return variable.Length > 0 &&
            (SystemVariables.Contains(variable) ||
             char.IsAsciiLetterLower(variable[0]) && variable.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'));
    }

    private static bool ValidatePipeline(JsonElement pipeline, ISet<string> collections)
    {
        if (pipeline.ValueKind != JsonValueKind.Array || pipeline.GetArrayLength() > MaximumPipelineStages)
            return false;
        foreach (var stage in pipeline.EnumerateArray())
        {
            if (stage.ValueKind != JsonValueKind.Object) return false;
            var properties = stage.EnumerateObject().ToArray();
            if (properties.Length != 1) return false;
            var spec = properties[0].Value;
            var valid = properties[0].Name switch
            {
                "$match" => ValidateQueryDocument(spec),
                "$project" or "$addFields" or "$set" => ValidateFieldExpressions(spec, allowEmpty: false),
                "$unset" => IsFieldName(spec) || spec.ValueKind == JsonValueKind.Array &&
                    spec.GetArrayLength() > 0 && spec.EnumerateArray().All(IsFieldName),
                "$sort" => ValidateSort(spec),
                "$limit" or "$skip" => spec.ValueKind == JsonValueKind.Number &&
                    spec.TryGetInt64(out var bound) && bound >= 0,
                "$count" => IsFieldName(spec),
                "$group" => ValidateGroup(spec),
                "$unwind" => ValidateUnwind(spec),
                "$replaceRoot" => spec.ValueKind == JsonValueKind.Object &&
                    IsSingleProperty(spec, "newRoot", out var root) && ValidateExpression(root),
                "$replaceWith" or "$sortByCount" => ValidateExpression(spec),
                "$sample" => spec.ValueKind == JsonValueKind.Object && IsSingleProperty(spec, "size", out var size) &&
                    size.TryGetInt32(out var sampleSize) && sampleSize is >= 1 and <= MaximumSampleSize,
                "$bucket" or "$bucketAuto" => ValidateBucket(spec),
                "$facet" => spec.ValueKind == JsonValueKind.Object && spec.EnumerateObject().Any() &&
                    spec.EnumerateObject().All(facet => !IsOperatorName(facet.Name) &&
                        ValidatePipeline(facet.Value, collections)),
                "$lookup" => ValidateLookup(spec, collections),
                "$unionWith" => ValidateUnionWith(spec, collections),
                "$graphLookup" => ValidateGraphLookup(spec, collections),
                // $out, $merge, $currentOp, $collStats, $indexStats, $listSessions, $documents, $changeStream,
                // $planCacheStats and every unknown stage fall here.
                _ => false
            };
            if (!valid) return false;
        }
        return true;
    }

    private static bool ValidateFieldExpressions(JsonElement spec, bool allowEmpty)
    {
        if (spec.ValueKind != JsonValueKind.Object) return false;
        var any = false;
        foreach (var property in spec.EnumerateObject())
        {
            any = true;
            if (IsOperatorName(property.Name) || !ValidateExpression(property.Value)) return false;
        }
        return any || allowEmpty;
    }

    private static bool ValidateSort(JsonElement spec)
    {
        if (spec.ValueKind != JsonValueKind.Object) return false;
        var any = false;
        foreach (var property in spec.EnumerateObject())
        {
            any = true;
            if (IsOperatorName(property.Name)) return false;
            var valid = property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetInt32(out var direction) && direction is 1 or -1 ||
                IsSingleOperator(property.Value, "$meta", out var meta) &&
                    meta.ValueKind == JsonValueKind.String && meta.GetString() == "textScore";
            if (!valid) return false;
        }
        return any;
    }

    private static bool ValidateGroup(JsonElement spec)
    {
        if (spec.ValueKind != JsonValueKind.Object) return false;
        var hasId = false;
        foreach (var property in spec.EnumerateObject())
        {
            if (property.Name == "_id")
            {
                hasId = true;
                if (!ValidateExpression(property.Value)) return false;
                continue;
            }
            if (!ValidateAccumulator(property)) return false;
        }
        return hasId;
    }

    private static bool ValidateAccumulator(JsonProperty field)
    {
        if (IsOperatorName(field.Name) || field.Value.ValueKind != JsonValueKind.Object) return false;
        var accumulator = field.Value.EnumerateObject().ToArray();
        return accumulator.Length == 1 && GroupAccumulators.Contains(accumulator[0].Name) &&
            ValidateExpression(accumulator[0].Value);
    }

    private static bool ValidateUnwind(JsonElement spec)
    {
        if (spec.ValueKind == JsonValueKind.String) return IsFieldPath(spec.GetString());
        if (spec.ValueKind != JsonValueKind.Object) return false;
        var hasPath = false;
        foreach (var property in spec.EnumerateObject())
        {
            var valid = property.Name switch
            {
                "path" => property.Value.ValueKind == JsonValueKind.String &&
                    IsFieldPath(property.Value.GetString()) && (hasPath = true),
                "includeArrayIndex" => IsFieldName(property.Value),
                "preserveNullAndEmptyArrays" => property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                _ => false
            };
            if (!valid) return false;
        }
        return hasPath;
    }

    private static bool ValidateBucket(JsonElement spec)
    {
        if (spec.ValueKind != JsonValueKind.Object) return false;
        var hasGroupBy = false;
        foreach (var property in spec.EnumerateObject())
        {
            var valid = property.Name switch
            {
                "groupBy" => ValidateExpression(property.Value) && (hasGroupBy = true),
                "boundaries" => property.Value.ValueKind == JsonValueKind.Array && ValidateLiteral(property.Value),
                "default" or "buckets" or "granularity" => ValidateLiteral(property.Value),
                "output" => property.Value.ValueKind == JsonValueKind.Object &&
                    property.Value.EnumerateObject().All(ValidateAccumulator),
                _ => false
            };
            if (!valid) return false;
        }
        return hasGroupBy;
    }

    private static bool ValidateLookup(JsonElement spec, ISet<string> collections)
    {
        if (spec.ValueKind != JsonValueKind.Object) return false;
        string? from = null;
        var hasAs = false;
        foreach (var property in spec.EnumerateObject())
        {
            var valid = property.Name switch
            {
                // A document-form "from" can name another database; only same-database names are provable here.
                "from" => TryCollectionName(property.Value, out from),
                "localField" or "foreignField" => property.Value.ValueKind == JsonValueKind.String &&
                    IsFieldName(property.Value),
                "as" => IsFieldName(property.Value) && (hasAs = true),
                "let" => ValidateFieldExpressions(property.Value, allowEmpty: true),
                "pipeline" => ValidatePipeline(property.Value, collections),
                _ => false
            };
            if (!valid) return false;
        }
        if (from is null || !hasAs) return false;
        collections.Add(from);
        return true;
    }

    private static bool ValidateUnionWith(JsonElement spec, ISet<string> collections)
    {
        if (TryCollectionName(spec, out var direct))
        {
            collections.Add(direct!);
            return true;
        }
        if (spec.ValueKind != JsonValueKind.Object) return false;
        string? coll = null;
        foreach (var property in spec.EnumerateObject())
        {
            var valid = property.Name switch
            {
                "coll" => TryCollectionName(property.Value, out coll),
                "pipeline" => ValidatePipeline(property.Value, collections),
                _ => false
            };
            if (!valid) return false;
        }
        if (coll is null) return false;
        collections.Add(coll);
        return true;
    }

    private static bool ValidateGraphLookup(JsonElement spec, ISet<string> collections)
    {
        if (spec.ValueKind != JsonValueKind.Object) return false;
        string? from = null;
        var required = 0;
        foreach (var property in spec.EnumerateObject())
        {
            var valid = property.Name switch
            {
                "from" => TryCollectionName(property.Value, out from),
                "startWith" => ValidateExpression(property.Value) && ++required > 0,
                "connectFromField" or "connectToField" or "as" => IsFieldName(property.Value) && ++required > 0,
                "maxDepth" => property.Value.ValueKind == JsonValueKind.Number,
                "depthField" => IsFieldName(property.Value),
                "restrictSearchWithMatch" => ValidateQueryDocument(property.Value),
                _ => false
            };
            if (!valid) return false;
        }
        if (from is null || required != 4) return false;
        collections.Add(from);
        return true;
    }

    private static bool TryCollectionName(JsonElement value, out string? name)
    {
        name = null;
        if (value.ValueKind != JsonValueKind.String) return false;
        var candidate = value.GetString()!;
        if (candidate.Length is < 1 or > 255 || Encoding.UTF8.GetByteCount(candidate) > 255 ||
            candidate != candidate.Trim() || candidate.Contains('$') ||
            candidate.StartsWith("system.", StringComparison.Ordinal) ||
            candidate.Any(c => char.IsControl(c) || c is '/' or '\\' or '@')) return false;
        name = candidate;
        return true;
    }

    private static bool IsSingleProperty(JsonElement value, string name, out JsonElement operand)
    {
        operand = default;
        var properties = value.EnumerateObject().ToArray();
        if (properties.Length != 1 || properties[0].Name != name) return false;
        operand = properties[0].Value;
        return true;
    }

    private static bool IsFieldName(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 and <= 1024 } name &&
        !name.StartsWith('$') && !name.StartsWith('.') && !name.EndsWith('.') &&
        !name.Contains("..", StringComparison.Ordinal);

    private static bool IsFieldPath(string? value) =>
        value is { Length: > 1 and <= 1025 } && value[0] == '$' && value[1] != '$' &&
        !value.EndsWith('.') && !value.Contains("..", StringComparison.Ordinal);
}
