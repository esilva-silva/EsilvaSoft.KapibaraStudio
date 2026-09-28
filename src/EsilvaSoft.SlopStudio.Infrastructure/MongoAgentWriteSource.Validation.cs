using System.Collections.Frozen;
using System.Text;
using EsilvaSoft.SlopStudio.Application.Agents;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace EsilvaSoft.SlopStudio.Infrastructure;

// Validation that runs before any connection: the registry's closed literal codec is re-applied (defense in depth),
// then the parsed BSON is checked again. Anything that fails here is InvalidRequest and nothing is sent.
public sealed partial class MongoAgentWriteSource
{
    internal const int UnauthorizedCode = 13;
    internal const int MaximumIndexNameLength = 128;

    private static readonly FrozenSet<string> ReservedDatabases =
        new[] { "admin", "local", "config" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Server codes that prove the command was rejected without effect (parse/validation/immutable field/limits).
    private static readonly FrozenSet<int> DefiniteRejections = new[]
    {
        2, // BadValue
        4, // NoSuchKey
        9, // FailedToParse
        14, // TypeMismatch
        15, // Overflow
        28, // PathNotViable
        40, // ConflictingUpdateOperators
        52, // DollarPrefixedFieldName
        55, // InvalidDBRef
        56, // EmptyFieldName
        57, // DottedFieldName
        66, // ImmutableField
        67, // CannotCreateIndex
        72, // InvalidOptions
        73, // InvalidNamespace
        121, // DocumentValidationFailure
        166, // CommandNotSupportedOnView
        197, // InvalidIndexSpecificationOption
        10334, // BSONObjectTooLarge
        17280 // KeyTooLong
    }.ToFrozenSet();

    internal static bool IsValidMaxTime(int maxTimeMs) => maxTimeMs is >= 1 and <= MaximumMaxTimeMs;

    private static bool IsValidEnvelope(AgentMongoWriteApproval? approval, string? database, string? collection,
        int maxTimeMs) =>
        approval is { IsConsumed: true } && IsValidMaxTime(maxTimeMs) && IsValidNamespace(database, collection);

    /// <summary>Plain user namespaces only: no system/admin/local/config targets and no ambiguous characters.</summary>
    internal static bool IsValidNamespace(string? database, string? collection)
    {
        if (database is not { Length: > 0 } || collection is not { Length: > 0 }) return false;
        if (Encoding.UTF8.GetByteCount(database) > 63 || ReservedDatabases.Contains(database) ||
            database.Any(c => char.IsControl(c) || c is '/' or '\\' or '.' or ' ' or '"' or '$' or '*' or '<' or '>' or ':' or '|' or '?'))
            return false;
        return Encoding.UTF8.GetByteCount(collection) <= 255 && collection == collection.Trim() &&
            !collection.StartsWith("system.", StringComparison.Ordinal) && !collection.StartsWith('.') &&
            !collection.Contains('$') && !collection.Any(char.IsControl);
    }

    /// <summary>Index names that can be targeted: never <c>_id_</c>, never the drop-all wildcard <c>*</c>.</summary>
    internal static bool IsValidIndexName(string? name) =>
        name is { Length: > 0 and <= MaximumIndexNameLength } && name != "_id_" && name != "*" &&
        name == name.Trim() && !name.Contains('$') && !name.Any(char.IsControl);

    internal static bool TryParseIdentifier(string? ejson, out BsonValue id)
    {
        id = BsonNull.Value;
        if (!AgentToolLiteralEjson.IsLiteralIdentifier(ejson)) return false;
        try
        {
            id = BsonSerializer.Deserialize<BsonValue>(ejson);
        }
        catch (FormatException)
        {
            return false;
        }
        return IsValidIdValue(id);
    }

    internal static bool TryParseInsertDocument(string? ejson, out BsonDocument document)
    {
        document = [];
        if (!AgentToolLiteralEjson.IsLiteralDocument(ejson)) return false;
        try
        {
            document = BsonDocument.Parse(ejson);
        }
        catch (FormatException)
        {
            return false;
        }
        // The registry fixes _id before approval: the approved text is exactly what is sent and an uncertain outcome
        // can be reconciled by that id. The driver never generates one here.
        return document.TryGetValue("_id", out var id) && IsValidIdValue(id) && !HasOperatorNames(document);
    }

    internal static bool TryParseUpdate(string? ejson, out BsonDocument update)
    {
        update = [];
        if (!AgentToolLiteralEjson.IsUpdateDocument(ejson)) return false;
        try
        {
            update = BsonDocument.Parse(ejson);
        }
        catch (FormatException)
        {
            return false;
        }
        return update.ElementCount > 0 && update.Names.All(name => name.StartsWith('$')) &&
            !update.Values.Any(ContainsCode);
    }

    /// <summary>
    /// The approved pre-image must be the source's own canonical output: its hash equals the approved hash, it
    /// round-trips byte-identically through BSON and its <c>_id</c> is the requested one.
    /// </summary>
    internal static bool TryParsePreimage(string? ejson, string? expectedHash, BsonValue id, out BsonDocument preimage)
    {
        preimage = [];
        if (ejson is null || !IsStateHash(expectedHash) || Encoding.UTF8.GetByteCount(ejson) > MaximumStateBytes ||
            !string.Equals(ComputeStateHash(ejson), expectedHash, StringComparison.Ordinal))
            return false;
        try
        {
            preimage = BsonDocument.Parse(ejson);
        }
        catch (FormatException)
        {
            return false;
        }
        return string.Equals(ToCanonicalEjson(preimage), ejson, StringComparison.Ordinal) &&
            preimage.TryGetValue("_id", out var stored) &&
            string.Equals(ToCanonicalEjson(stored), ToCanonicalEjson(id), StringComparison.Ordinal);
    }

    /// <summary>
    /// Canonical createIndexes specification: ordered keys (1/-1, hashed, 2dsphere), a name (explicit or MongoDB's
    /// default <c>field_direction</c> form) and only <c>unique</c>/<c>sparse</c> when true.
    /// </summary>
    internal static bool TryBuildIndexSpecification(string? keysEjson, string? requestedName, bool unique, bool sparse,
        out BsonDocument specification, out string name)
    {
        specification = [];
        name = string.Empty;
        if (!AgentToolLiteralEjson.IsIndexKeys(keysEjson)) return false;
        BsonDocument keys;
        try
        {
            keys = BsonDocument.Parse(keysEjson);
        }
        catch (FormatException)
        {
            return false;
        }
        var parts = new List<string>(keys.ElementCount);
        foreach (var element in keys)
        {
            switch (element.Value)
            {
                case BsonInt32 { Value: 1 or -1 } direction:
                    parts.Add(element.Name + "_" + direction.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case BsonString { Value: "hashed" or "2dsphere" } kind:
                    parts.Add(element.Name + "_" + kind.Value);
                    break;
                default:
                    return false;
            }
        }
        // The _id index always exists and is protected; an explicit {_id:1} spec is never an agent operation.
        if (keys.ElementCount == 1 && keys.Names.Single() == "_id") return false;
        name = requestedName ?? string.Join("_", parts);
        if (!IsValidIndexName(name)) return false;
        specification = new BsonDocument { { "key", keys }, { "name", name } };
        if (unique) specification["unique"] = true;
        if (sparse) specification["sparse"] = true;
        return true;
    }

    private static AgentMongoWriteStatus MapServerError(int code, WriteKind kind) => code switch
    {
        UnauthorizedCode => AgentMongoWriteStatus.Forbidden,
        26 or 27 => AgentMongoWriteStatus.NotFound, // NamespaceNotFound, IndexNotFound
        11000 or 11001 => kind == WriteKind.Insert // DuplicateKey
            ? AgentMongoWriteStatus.Conflict
            : AgentMongoWriteStatus.InvalidRequest, // unique index over duplicated data: rejected, nothing built
        68 or 85 or 86 => AgentMongoWriteStatus.Conflict, // IndexAlreadyExists, IndexOptionsConflict, IndexKeySpecsConflict
        _ when DefiniteRejections.Contains(code) => AgentMongoWriteStatus.InvalidRequest,
        // Timeouts (50/262), interruption, step-down, shutdown and anything unclassified: effect not provable.
        _ => AgentMongoWriteStatus.OutcomeUnknown
    };

    private static bool IsValidIdValue(BsonValue id) =>
        id is not (BsonArray or BsonRegularExpression or BsonUndefined or BsonJavaScript) &&
        !(id is BsonDocument document && HasOperatorNames(document));

    private static bool HasOperatorNames(BsonValue value) => value switch
    {
        BsonDocument document => document.Elements.Any(element =>
            element.Name.StartsWith('$') || HasOperatorNames(element.Value)),
        BsonArray array => array.Any(HasOperatorNames),
        _ => false
    };

    private static bool ContainsCode(BsonValue value) => value switch
    {
        BsonJavaScript => true,
        BsonDocument document => document.Elements.Any(element =>
            element.Name is "$where" or "$function" or "$accumulator" or "$expr" || ContainsCode(element.Value)),
        BsonArray array => array.Any(ContainsCode),
        _ => false
    };
}
