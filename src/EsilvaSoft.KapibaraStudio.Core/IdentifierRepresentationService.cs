using System.Security.Cryptography;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Single place for identifier detection, parsing, formatting and the ObjectId → UUID alternative representation.
/// UUID bytes and subtypes are delegated to <see cref="UuidCodec"/>. Constructor/Extended JSON rewriting is delegated
/// to <see cref="ExtendedJsonConstructorRewriter"/>. Nothing here changes a stored BSON type.
/// </summary>
public static class IdentifierRepresentationService
{
    public const string SampleObjectId = "66e3bd5b3bc3f54c840d73ac";
    /// <summary>Well-formed UUID v4 (version 4, RFC 4122 variant) used as a script placeholder.</summary>
    public static Guid PlaceholderUuid { get; } = Guid.Parse("00000000-0000-4000-8000-000000000000");
    public const string PlaceholderObjectId = "000000000000000000000000";
    internal const string ObjectIdHint = "Use 24 dígitos hexadecimais.";
    private static readonly byte[] ProcessUnique = RandomNumberGenerator.GetBytes(5);
    private static int _counter = RandomNumberGenerator.GetInt32(0x1000000);

    public static IReadOnlyList<IdentifierRepresentationMode> All { get; } =
        [IdentifierRepresentationMode.Standard, IdentifierRepresentationMode.ObjectId, IdentifierRepresentationMode.UuidV4];

    public static string DisplayName(IdentifierRepresentationMode mode) => mode switch
    {
        IdentifierRepresentationMode.Standard => "Standard",
        IdentifierRepresentationMode.ObjectId => "ObjectId",
        IdentifierRepresentationMode.UuidV4 => "UUID v4",
        _ => throw UnknownMode(mode)
    };

    public static string ChoiceLabel(IdentifierRepresentationMode mode) => mode switch
    {
        IdentifierRepresentationMode.Standard => "Standard · ObjectId + UUID v4",
        IdentifierRepresentationMode.ObjectId => "ObjectId · MongoDB ObjectId",
        IdentifierRepresentationMode.UuidV4 => "UUID v4 · BSON subtype 4",
        _ => throw UnknownMode(mode)
    };

    public static string Description(IdentifierRepresentationMode mode) => mode switch
    {
        IdentifierRepresentationMode.Standard => "Aceita ObjectId e UUID v4. A IDE preserva o tipo BSON original e utiliza a representação mais adequada para cada identificador.",
        IdentifierRepresentationMode.ObjectId => "Prioriza MongoDB ObjectId como representação padrão de identificadores. UUIDs existentes continuam UUID, na representação escolhida abaixo.",
        IdentifierRepresentationMode.UuidV4 => "Prioriza UUID padrão BSON subtype 4. ObjectIds podem ser representados como UUID quando a conversão for necessária, sem alterar o ObjectId gravado.",
        _ => throw UnknownMode(mode)
    };

    // ----- Detection -----

    /// <summary>Uses the BSON type expressed by the Extended JSON value; a JSON string is never an identifier.</summary>
    public static IdentifierKind DetectType(JsonElement value, UuidRepresentation uuid)
    {
        if (TryGetObjectId(value, out _)) return IdentifierKind.ObjectId;
        return UuidCodec.Describe(value, uuid).Kind switch
        {
            UuidDisplayKind.Uuid => IdentifierKind.Uuid,
            UuidDisplayKind.UnknownLegacy => IdentifierKind.UnknownLegacyUuid,
            _ => IdentifierKind.None
        };
    }

    /// <summary>Textual inference, used only when no BSON value is available.</summary>
    public static IdentifierKind DetectType(string text, IdentifierDisplayOptions options) =>
        TryParseIdentifier(text, options, out var value) ? value.Kind : IdentifierKind.None;

    public static bool IsObjectIdHex(ReadOnlySpan<char> text)
    {
        if (text.Length != 24) return false;
        foreach (var ch in text) if (!char.IsAsciiHexDigit(ch)) return false;
        return true;
    }

    public static bool TryGetObjectId(JsonElement value, out string hex)
    {
        hex = "";
        if (value.ValueKind != JsonValueKind.Object) return false;
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (++count > 1 || !property.NameEquals("$oid") || property.Value.ValueKind != JsonValueKind.String) return false;
            hex = property.Value.GetString()!;
        }
        if (count == 1 && IsObjectIdHex(hex)) { hex = hex.ToLowerInvariant(); return true; }
        hex = "";
        return false;
    }

    // ----- Formatting and conversion -----

    public static string FormatObjectId(string hex) => "ObjectId(\"" + NormalizeObjectId(hex) + "\")";

    public static string ObjectIdToExtendedJson(string hex) => "{\"$oid\":\"" + NormalizeObjectId(hex) + "\"}";

    public static string FormatUuid(Guid value, UuidRepresentation representation) => UuidCodec.FormatConstructor(value, representation);

    /// <summary>
    /// Deterministic, reversible alternative: the 12 ObjectId bytes followed by 4 zero bytes, read in RFC 4122 order.
    /// The result is not a UUID v4 and is never written in place of the ObjectId.
    /// </summary>
    public static Guid ObjectIdToUuid(string hex)
    {
        Span<byte> bytes = stackalloc byte[16];
        Convert.FromHexString(NormalizeObjectId(hex)).CopyTo(bytes);
        return new Guid(bytes, bigEndian: true);
    }

    /// <summary>Inverse of <see cref="ObjectIdToUuid"/>; only UUIDs whose last four bytes are zero correspond to an ObjectId.</summary>
    public static bool TryUuidToObjectId(Guid value, out string hex)
    {
        var bytes = value.ToByteArray(bigEndian: true);
        hex = bytes.AsSpan(12).IndexOfAnyExcept((byte)0) < 0 ? Convert.ToHexStringLower(bytes, 0, 12) : "";
        return hex.Length > 0;
    }

    /// <summary>
    /// Human text of an ObjectId; UUID v4 mode appends the alternative UUID after the constructor, so the stored type stays first.
    /// The suffix is short enough for the 720 px value column of the result trees at the default code font.
    /// </summary>
    public static string DescribeObjectId(string hex, IdentifierDisplayOptions options) =>
        options.Mode == IdentifierRepresentationMode.UuidV4
            ? FormatObjectId(hex) + " · UUID " + ObjectIdToUuid(hex).ToString("D")
            : FormatObjectId(hex);

    /// <summary>Describes one Extended JSON value when it is an identifier; returns null for any other value.</summary>
    public static IdentifierValue? Describe(JsonElement value, IdentifierDisplayOptions options)
    {
        if (TryGetObjectId(value, out var hex))
            return new(IdentifierKind.ObjectId, FormatObjectId(hex), ObjectIdToExtendedJson(hex), ObjectIdToUuid(hex).ToString("D"), true);
        var uuid = UuidCodec.Describe(value, options.Uuid);
        return uuid.Kind switch
        {
            UuidDisplayKind.Uuid => new(IdentifierKind.Uuid, uuid.Text, RewriteConstructors(uuid.Text), null, true),
            UuidDisplayKind.UnknownLegacy => new(IdentifierKind.UnknownLegacyUuid, value.GetRawText(), value.GetRawText(), null, true),
            _ => null
        };
    }

    // ----- Parsing -----

    /// <summary>
    /// Accepts <c>ObjectId("…")</c>, 24 hexadecimal digits, <c>UUID("…")</c>/<c>CGUUID</c>/<c>JUUID</c>/<c>GUUID</c>,
    /// a UUID with 32 digits (grouped or not) and canonical <c>$oid</c>/<c>$binary</c> wrappers. Wrappers and constructors
    /// carry the type explicitly and take priority; bare UUID text uses the configured UUID representation.
    /// The mode never rejects a valid explicit identifier, so existing data remains reachable in every mode.
    /// </summary>
    public static IdentifierValue ParseIdentifier(string text, IdentifierDisplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!Enum.IsDefined(options.Mode)) throw UnknownMode(options.Mode);
        var trimmed = text.Trim();
        if (trimmed.StartsWith('{') || trimmed.Contains('(', StringComparison.Ordinal))
        {
            // Invalid constructor arguments keep the precise message and location from the rewrite.
            var canonical = trimmed.StartsWith('{') ? trimmed : RewriteConstructors(trimmed);
            try
            {
                using var document = JsonDocument.Parse(canonical);
                if (Describe(document.RootElement, options) is { } described) return described;
            }
            catch (JsonException) { }
            throw Unrecognized(trimmed);
        }
        if (IsObjectIdHex(trimmed))
        {
            var hex = trimmed.ToLowerInvariant();
            return new(IdentifierKind.ObjectId, FormatObjectId(hex), ObjectIdToExtendedJson(hex), ObjectIdToUuid(hex).ToString("D"), false);
        }
        if ((trimmed.Length == 36 && Guid.TryParseExact(trimmed, "D", out var uuid)) || (trimmed.Length == 32 && Guid.TryParseExact(trimmed, "N", out uuid)))
            return new(IdentifierKind.Uuid, FormatUuid(uuid, options.Uuid), UuidCodec.ToExtendedJson(uuid, options.Uuid), null, false);
        throw Unrecognized(trimmed);
    }

    public static bool TryParseIdentifier(string text, IdentifierDisplayOptions options, out IdentifierValue value)
    {
        try { value = ParseIdentifier(text, options); return true; }
        catch (FormatException) { value = null!; return false; }
    }

    // ----- Generation and scripts -----

    /// <summary>New ObjectId: 4-byte big-endian seconds, 5 process-unique random bytes and a 3-byte counter.</summary>
    public static string NewObjectId(DateTimeOffset? now = null)
    {
        Span<byte> bytes = stackalloc byte[12];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)(now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds());
        ProcessUnique.CopyTo(bytes[4..]);
        var counter = Interlocked.Increment(ref _counter) & 0xFFFFFF;
        bytes[9] = (byte)(counter >> 16); bytes[10] = (byte)(counter >> 8); bytes[11] = (byte)counter;
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Random UUID v4 (version 4, RFC 4122 variant).</summary>
    public static Guid NewUuidV4() => Guid.NewGuid();

    /// <summary>ObjectId mode generates ObjectId, UUID v4 mode a UUID v4 in the configured representation, Standard both.</summary>
    public static IReadOnlyList<GeneratedIdentifier> Generate(IdentifierDisplayOptions options)
    {
        var list = new List<GeneratedIdentifier>(2);
        if (options.Mode is IdentifierRepresentationMode.Standard or IdentifierRepresentationMode.ObjectId)
        {
            var hex = NewObjectId();
            list.Add(new(IdentifierKind.ObjectId, FormatObjectId(hex), ObjectIdToExtendedJson(hex)));
        }
        if (options.Mode is IdentifierRepresentationMode.Standard or IdentifierRepresentationMode.UuidV4)
        {
            var uuid = NewUuidV4();
            list.Add(new(IdentifierKind.Uuid, FormatUuid(uuid, options.Uuid), UuidCodec.ToExtendedJson(uuid, options.Uuid)));
        }
        if (list.Count == 0) throw UnknownMode(options.Mode);
        return list;
    }

    /// <summary>Placeholder <c>_id</c> of generated CRUD scripts. Standard shows the ObjectId and, in a comment, the UUID alternative.</summary>
    public static string ScriptIdentifierPlaceholder(IdentifierDisplayOptions options) => options.Mode switch
    {
        IdentifierRepresentationMode.ObjectId => FormatObjectId(PlaceholderObjectId),
        IdentifierRepresentationMode.UuidV4 => FormatUuid(PlaceholderUuid, options.Uuid),
        IdentifierRepresentationMode.Standard => FormatObjectId(PlaceholderObjectId) + " /* ou " + FormatUuid(PlaceholderUuid, options.Uuid) + " */",
        _ => throw UnknownMode(options.Mode)
    };

    // ----- Human text <-> canonical Extended JSON -----

    /// <summary>
    /// Replaces canonical Date, ObjectId and UUID wrappers by shell constructors, byte-for-byte preserving the rest of the text.
    /// The result goes back to identical BSON through <see cref="RewriteConstructors"/>.
    /// </summary>
    public static UuidDisplayText FormatForDisplay(string json, IdentifierDisplayOptions options) =>
        ExtendedJsonConstructorRewriter.Format(json, options.Uuid, objectIds: true);

    /// <summary>Converts Date("…"), ISODate("…"), ObjectId("…") and the UUID constructors written outside strings and comments into Canonical Extended JSON.</summary>
    public static string RewriteConstructors(string text) => ExtendedJsonConstructorRewriter.Rewrite(text, objectIds: true);

    private static string NormalizeObjectId(string hex) =>
        IsObjectIdHex(hex) ? hex.ToLowerInvariant() : throw new FormatException($"\"{Shorten(hex)}\" não é um ObjectId válido. {ObjectIdHint}");

    private static FormatException Unrecognized(string text) =>
        new($"\"{Shorten(text)}\" não é um identificador reconhecido. Use ObjectId(\"…\") ou 24 dígitos hexadecimais, UUID(\"…\")/CGUUID/JUUID/GUUID ou um UUID com 32 dígitos, com ou sem hífens.");

    private static ArgumentOutOfRangeException UnknownMode(IdentifierRepresentationMode mode) =>
        new(nameof(mode), mode, "Modo de identificador desconhecido.");

    private static string Shorten(string value) => value.Length <= 60 ? value : value[..60] + "…";
}
