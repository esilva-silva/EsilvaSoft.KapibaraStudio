using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace EsilvaSoft.SlopStudio.Infrastructure;

// Atomic precondition for update/delete. MongoDB has no "hash" operator, so the approved hash is re-derived from a
// fresh read and the write is then conditioned on the COMPLETE pre-image inside the same command: the server only
// applies the effect when the stored document is still byte-for-byte the one whose hash was approved. The read is
// never trusted alone; it only supplies the literal that the single write command compares atomically.
public sealed partial class MongoAgentWriteSource
{
    internal const int MaximumStateBytes = 256 * 1024;
    internal const int MaximumTypeGuards = 4_096;
    internal const int MaximumFilterBytes = 4 * 1024 * 1024;

    private static readonly JsonWriterSettings CanonicalJson = new()
    {
        OutputMode = JsonOutputMode.CanonicalExtendedJson
    };

    /// <summary>
    /// Compact canonical Extended JSON of a document, value or index definition: the driver's canonical mode with
    /// insignificant whitespace removed (same relaxed escaping as the registry's frozen payloads). Deterministic, so the
    /// hash and the pre-image text round-trip exactly.
    /// </summary>
    internal static string ToCanonicalEjson(BsonValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var parsed = JsonDocument.Parse(value.ToJson(CanonicalJson), new JsonDocumentOptions { MaxDepth = 256 });
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
               {
                   Indented = false,
                   Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                   MaxDepth = 256
               }))
        {
            parsed.RootElement.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>Lowercase hex SHA-256 of the UTF-8 canonical Extended JSON, as exposed in the snapshot.</summary>
    internal static string ComputeStateHash(string canonicalEjson)
    {
        ArgumentNullException.ThrowIfNull(canonicalEjson);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalEjson)));
    }

    internal static bool IsStateHash(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>
    /// Filter that matches only the exact pre-image: <c>_id</c> equality (index lookup) plus an <c>$expr</c> that
    /// compares the whole stored document with the literal pre-image, its BSON size and the concrete type (and, for
    /// Decimal128, the textual representation) of every scalar whose MongoDB comparison is value-based. Returns null
    /// when a safe predicate cannot be built within the limits; callers then report PreconditionUnavailable.
    /// </summary>
    internal static BsonDocument? BuildPreimageFilter(BsonDocument preimage)
    {
        ArgumentNullException.ThrowIfNull(preimage);
        if (!preimage.TryGetValue("_id", out var id)) return null;
        var size = preimage.ToBson().Length;
        if (size > MaximumStateBytes) return null;

        // $$ROOT equality compares field names, order and values; numbers compare by value (1 == 1L == 1.0) and
        // strings/symbols share a canonical type, hence the per-scalar guards below.
        var conditions = new BsonArray
        {
            new BsonDocument("$eq", new BsonArray { "$$ROOT", new BsonDocument("$literal", preimage) }),
            new BsonDocument("$eq", new BsonArray { new BsonDocument("$bsonSize", "$$ROOT"), size })
        };
        var budget = MaximumTypeGuards;
        if (!AddTypeGuards(preimage, "$$ROOT", conditions, ref budget)) return null;

        var filter = new BsonDocument
        {
            { "_id", new BsonDocument("$eq", id) },
            { "$expr", new BsonDocument("$and", conditions) }
        };
        return filter.ToBson().Length <= MaximumFilterBytes ? filter : null;
    }

    private static bool AddTypeGuards(BsonValue value, BsonValue expression, BsonArray conditions, ref int budget)
    {
        switch (value)
        {
            case BsonDocument document:
                foreach (var element in document)
                {
                    // $getField + $literal reads names with dots or a leading '$' without path interpretation.
                    var child = new BsonDocument("$getField", new BsonDocument
                    {
                        { "field", new BsonDocument("$literal", element.Name) },
                        { "input", expression }
                    });
                    if (!AddTypeGuards(element.Value, child, conditions, ref budget)) return false;
                }
                return true;
            case BsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    var child = new BsonDocument("$arrayElemAt", new BsonArray { expression, index });
                    if (!AddTypeGuards(array[index], child, conditions, ref budget)) return false;
                }
                return true;
        }

        var alias = value.BsonType switch
        {
            BsonType.Int32 => "int",
            BsonType.Int64 => "long",
            BsonType.Double => "double",
            BsonType.Decimal128 => "decimal",
            BsonType.String => "string",
            BsonType.Symbol => "symbol",
            _ => null
        };
        if (alias is null) return true;
        if (--budget < 0) return false;
        conditions.Add(new BsonDocument("$eq", new BsonArray { new BsonDocument("$type", expression), alias }));
        if (value.BsonType == BsonType.Decimal128)
        {
            // 1.0 and 1.00 are equal by value but distinct Decimal128 values; the server's string form keeps the
            // exponent, so the approved representation is required as well.
            if (--budget < 0) return false;
            conditions.Add(new BsonDocument("$eq", new BsonArray
            {
                new BsonDocument("$toString", expression), value.AsDecimal128.ToString()
            }));
        }
        return true;
    }
}
