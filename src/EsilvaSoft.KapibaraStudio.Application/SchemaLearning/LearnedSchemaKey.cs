using System.Buffers.Binary;
using System.Text;

namespace EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

/// <summary>
/// Durable identity of a learned schema namespace (DEC-L-KEY): profile, database and collection.
/// It never carries a URI, credential, resolved host, friendly profile name, connection fingerprint or
/// source generation; the generation is a non-key column whose trust is derived at hydration (DEC-L-TRUST).
/// </summary>
public readonly record struct LearnedSchemaKey(Guid ProfileId, string Database, string Collection)
{
    /// <summary>
    /// Version of the <em>key encoding</em> (DEC-L-KEY, adjustment 3). It changes only when the name encoding rule
    /// changes; the content format version is a document field, never part of the identity.
    /// </summary>
    public const byte EncodingVersion = 0x01;

    /// <summary>Fixed size of the encoded profile identifier inside the canonical id.</summary>
    public const int ProfileIdLength = 16;

    /// <summary>Builds a validated key. Empty or blank names are rejected at the border, not at the database.</summary>
    public static LearnedSchemaKey Create(Guid profileId, string database, string collection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(collection);
        if (profileId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(profileId), "ProfileId não pode ser vazio.");
        return new LearnedSchemaKey(profileId, database, collection);
    }

    /// <summary>True when the three components are usable as a durable identity.</summary>
    public bool IsComplete => ProfileId != Guid.Empty && !string.IsNullOrWhiteSpace(Database) && !string.IsNullOrWhiteSpace(Collection);

    /// <summary>Ordinal comparison: <c>Orders</c> and <c>orders</c> are different MongoDB namespaces.</summary>
    public bool Equals(LearnedSchemaKey other) => ProfileId.Equals(other.ProfileId)
        && string.Equals(Database, other.Database, StringComparison.Ordinal)
        && string.Equals(Collection, other.Collection, StringComparison.Ordinal);

    /// <summary>Hash consistent with the ordinal equality above.</summary>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(ProfileId);
        hash.Add(Database, StringComparer.Ordinal);
        hash.Add(Collection, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    /// <summary>
    /// Canonical <c>_id</c> encoding, stored as BSON binary so the LiteDB collation (culture, case insensitive by
    /// default) can never merge two distinct namespaces:
    /// <code>
    /// 0x01 ‖ ProfileId (16 bytes, RFC 4122 big-endian) ‖ len32be(utf8(Database)) ‖ utf8(Database)
    ///      ‖ len32be(utf8(Collection)) ‖ utf8(Collection)
    /// </code>
    /// Every multi-byte integer, including the GUID, is big-endian. The GUID is <em>not</em>
    /// <see cref="Guid.ToByteArray()"/> order, which is little-endian in the first three groups.
    /// The length prefixes are what keep <c>("a", "b.c")</c> and <c>("a.b", "c")</c> distinct.
    /// It is an encoding, not a hash: it stays reversible so a namespace can be enumerated and renamed.
    /// </summary>
    public byte[] ToCanonicalId()
    {
        var database = Database ?? string.Empty;
        var collection = Collection ?? string.Empty;
        var databaseLength = Encoding.UTF8.GetByteCount(database);
        var collectionLength = Encoding.UTF8.GetByteCount(collection);
        var buffer = new byte[1 + ProfileIdLength + 4 + databaseLength + 4 + collectionLength];
        buffer[0] = EncodingVersion;
        if (!ProfileId.TryWriteBytes(buffer.AsSpan(1, ProfileIdLength), bigEndian: true, out _))
            throw new InvalidOperationException("Não foi possível codificar o ProfileId da chave de schema aprendido.");
        var offset = 1 + ProfileIdLength;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), (uint)databaseLength);
        offset += 4;
        Encoding.UTF8.GetBytes(database, buffer.AsSpan(offset, databaseLength));
        offset += databaseLength;
        BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(offset, 4), (uint)collectionLength);
        offset += 4;
        Encoding.UTF8.GetBytes(collection, buffer.AsSpan(offset, collectionLength));
        return buffer;
    }

    /// <summary>Reverses <see cref="ToCanonicalId"/>. Returns false for a foreign or truncated encoding version.</summary>
    public static bool TryFromCanonicalId(ReadOnlySpan<byte> canonicalId, out LearnedSchemaKey key)
    {
        key = default;
        if (canonicalId.Length < 1 + ProfileIdLength + 8 || canonicalId[0] != EncodingVersion) return false;
        var profileId = new Guid(canonicalId.Slice(1, ProfileIdLength), bigEndian: true);
        var offset = 1 + ProfileIdLength;
        if (!TryReadSegment(canonicalId, ref offset, out var database)) return false;
        if (!TryReadSegment(canonicalId, ref offset, out var collection)) return false;
        if (offset != canonicalId.Length) return false;
        key = new LearnedSchemaKey(profileId, database, collection);
        return true;
    }

    private static bool TryReadSegment(ReadOnlySpan<byte> source, ref int offset, out string value)
    {
        value = string.Empty;
        if (offset + 4 > source.Length) return false;
        var length = BinaryPrimitives.ReadUInt32BigEndian(source.Slice(offset, 4));
        offset += 4;
        if (length > (uint)(source.Length - offset)) return false;
        value = Encoding.UTF8.GetString(source.Slice(offset, (int)length));
        offset += (int)length;
        return true;
    }
}
