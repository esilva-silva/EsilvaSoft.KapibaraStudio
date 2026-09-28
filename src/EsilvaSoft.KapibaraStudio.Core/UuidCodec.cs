using System.Globalization;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Single UUID codec of the IDE: byte order, BSON subtype, named shell constructors and human-readable output.
/// C# legacy reverses the first three RFC 4122 fields; Java legacy reverses each 8-byte half; Go/Standard keeps RFC 4122 bytes.
/// </summary>
public static class UuidCodec
{
    private const string InvalidValueHint = "Use 32 dígitos hexadecimais, com ou sem hífens.";
    public static IReadOnlyList<UuidRepresentation> All { get; } = [UuidRepresentation.Standard, UuidRepresentation.CSharpLegacy, UuidRepresentation.JavaLegacy, UuidRepresentation.GoStandard];

    public static string Constructor(UuidRepresentation representation) => representation switch
    {
        UuidRepresentation.Standard => "UUID",
        UuidRepresentation.CSharpLegacy => "CGUUID",
        UuidRepresentation.JavaLegacy => "JUUID",
        UuidRepresentation.GoStandard => "GUUID",
        _ => throw new ArgumentOutOfRangeException(nameof(representation), "Representação UUID desconhecida.")
    };

    public static string DisplayName(UuidRepresentation representation) => representation switch
    {
        UuidRepresentation.Standard => "Standard",
        UuidRepresentation.CSharpLegacy => "C# legacy",
        UuidRepresentation.JavaLegacy => "Java legacy",
        UuidRepresentation.GoStandard => "Go/Standard",
        _ => throw new ArgumentOutOfRangeException(nameof(representation), "Representação UUID desconhecida.")
    };

    public static byte SubType(UuidRepresentation representation) =>
        representation is UuidRepresentation.CSharpLegacy or UuidRepresentation.JavaLegacy ? (byte)3
        : Enum.IsDefined(representation) ? (byte)4 : throw new ArgumentOutOfRangeException(nameof(representation), "Representação UUID desconhecida.");

    public static bool TryParseConstructor(string name, out UuidRepresentation representation)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(Constructor(candidate), name, StringComparison.Ordinal)) { representation = candidate; return true; }
        }
        representation = default;
        return false;
    }

    public static byte[] ToBytes(Guid value, UuidRepresentation representation)
    {
        var bytes = value.ToByteArray(bigEndian: true);
        Reorder(bytes, representation);
        return bytes;
    }

    public static Guid FromBytes(ReadOnlySpan<byte> bytes, UuidRepresentation representation)
    {
        if (bytes.Length != 16) throw new ArgumentException("Um UUID binário possui exatamente 16 bytes.", nameof(bytes));
        var copy = bytes.ToArray();
        // Every reordering is its own inverse.
        Reorder(copy, representation);
        return new Guid(copy, bigEndian: true);
    }

    /// <summary>Accepts only 32 hexadecimal digits, optionally grouped 8-4-4-4-12; braces and other GUID formats are rejected.</summary>
    public static Guid ParseText(string constructor, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if ((text.Length == 36 && Guid.TryParseExact(text, "D", out var value)) || (text.Length == 32 && Guid.TryParseExact(text, "N", out value))) return value;
        throw new FormatException($"{constructor}(\"{Shorten(text)}\") não contém um UUID válido. {InvalidValueHint}");
    }

    /// <summary>C# and Go forms use uppercase text; Java and Standard use lowercase. Capitalization never changes the bytes.</summary>
    public static string FormatText(Guid value, UuidRepresentation representation) =>
        representation is UuidRepresentation.CSharpLegacy or UuidRepresentation.GoStandard ? value.ToString("D").ToUpperInvariant() : value.ToString("D");

    public static string FormatConstructor(Guid value, UuidRepresentation representation) =>
        Constructor(representation) + "(\"" + FormatText(value, representation) + "\")";

    public static string ToExtendedJson(Guid value, UuidRepresentation representation) =>
        "{\"$binary\":{\"base64\":\"" + Convert.ToBase64String(ToBytes(value, representation)) + "\",\"subType\":\"" + SubType(representation).ToString("x2", CultureInfo.InvariantCulture) + "\"}}";

    /// <summary>Subtype 4 is always standard; subtype 3 is decoded only when a legacy profile was chosen explicitly.</summary>
    public static UuidDisplayValue Describe(string base64, string subType, UuidRepresentation representation)
    {
        if (subType.Length is not (1 or 2) || !byte.TryParse(subType, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var kind) || kind is not (3 or 4))
            return new(UuidDisplayKind.NotUuid, "");
        Span<byte> bytes = stackalloc byte[18];
        if (!Convert.TryFromBase64String(base64, bytes, out var written) || written != 16) return new(UuidDisplayKind.NotUuid, "");
        var uuid = bytes[..16];
        if (kind == 4)
        {
            var standard = representation == UuidRepresentation.GoStandard ? UuidRepresentation.GoStandard : UuidRepresentation.Standard;
            return new(UuidDisplayKind.Uuid, FormatConstructor(FromBytes(uuid, standard), standard));
        }
        return representation is UuidRepresentation.CSharpLegacy or UuidRepresentation.JavaLegacy
            ? new(UuidDisplayKind.Uuid, FormatConstructor(FromBytes(uuid, representation), representation))
            : new(UuidDisplayKind.UnknownLegacy, "");
    }

    public static UuidDisplayValue Describe(JsonElement element, UuidRepresentation representation)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("$binary", out var binary) || binary.ValueKind != JsonValueKind.Object)
            return new(UuidDisplayKind.NotUuid, "");
        string? base64 = null, subType = null;
        var outer = 0;
        foreach (var _ in element.EnumerateObject()) outer++;
        foreach (var property in binary.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.String) return new(UuidDisplayKind.NotUuid, "");
            if (property.NameEquals("base64") && base64 is null) base64 = property.Value.GetString();
            else if (property.NameEquals("subType") && subType is null) subType = property.Value.GetString();
            else return new(UuidDisplayKind.NotUuid, "");
        }
        return outer == 1 && base64 is not null && subType is not null ? Describe(base64, subType, representation) : new(UuidDisplayKind.NotUuid, "");
    }

    /// <summary>
    /// Replaces canonical UUID binaries by named constructors, byte-for-byte preserving the rest of the JSON text.
    /// ObjectIds are left untouched; <see cref="IdentifierRepresentationService.FormatForDisplay"/> formats both.
    /// </summary>
    public static UuidDisplayText FormatForDisplay(string json, UuidRepresentation representation) =>
        ExtendedJsonConstructorRewriter.Format(json, representation, objectIds: false);

    /// <summary>
    /// Converts UUID("…"), CGUUID("…"), JUUID("…") and GUUID("…") written outside strings and comments into
    /// Canonical Extended JSON. Text that only looks like a UUID remains a string.
    /// </summary>
    public static string RewriteConstructors(string text) => ExtendedJsonConstructorRewriter.Rewrite(text, objectIds: false);

    private static void Reorder(byte[] bytes, UuidRepresentation representation)
    {
        switch (representation)
        {
            case UuidRepresentation.Standard or UuidRepresentation.GoStandard:
                break;
            case UuidRepresentation.CSharpLegacy:
                Array.Reverse(bytes, 0, 4); Array.Reverse(bytes, 4, 2); Array.Reverse(bytes, 6, 2);
                break;
            case UuidRepresentation.JavaLegacy:
                Array.Reverse(bytes, 0, 8); Array.Reverse(bytes, 8, 8);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(representation), "Representação UUID desconhecida.");
        }
    }

    private static string Shorten(string value) => value.Length <= 60 ? value : value[..60] + "…";
}
