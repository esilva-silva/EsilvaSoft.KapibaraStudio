using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Converts shell constructor literals (<c>Date</c>/<c>ISODate</c>/<c>ObjectId</c>/UUID constructors) to and from
/// canonical Extended JSON, byte-for-byte preserving the rest of the text. Shared by
/// <see cref="IdentifierRepresentationService"/> (ObjectId and UUID together) and <see cref="UuidCodec"/> (UUID only).
/// </summary>
internal static class ExtendedJsonConstructorRewriter
{
    internal static UuidDisplayText Format(string json, UuidRepresentation representation, bool objectIds)
    {
        if (string.IsNullOrEmpty(json) || !(json.Contains("$binary", StringComparison.Ordinal) || (objectIds && (json.Contains("$oid", StringComparison.Ordinal) || json.Contains("$date", StringComparison.Ordinal)))))
            return new(json, 0);
        var utf8 = Encoding.UTF8.GetBytes(json);
        var output = new StringBuilder(json.Length);
        var copied = 0;
        var unknown = 0;
        try
        {
            var reader = new Utf8JsonReader(utf8, new JsonReaderOptions { MaxDepth = 512 });
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.StartObject) continue;
                var start = (int)reader.TokenStartIndex;
                var probe = reader;
                string? replacement = null;
                if (objectIds && TryReadDate(ref probe, out var date)) replacement = date;
                else if (objectIds && TryReadObjectId(ref probe, out var hex)) replacement = IdentifierRepresentationService.FormatObjectId(hex);
                else
                {
                    probe = reader;
                    if (!TryReadBinary(ref probe, out var base64, out var subType)) continue;
                    var value = UuidCodec.Describe(base64, subType, representation);
                    if (value.Kind == UuidDisplayKind.UnknownLegacy) unknown++;
                    if (value.Kind == UuidDisplayKind.Uuid) replacement = value.Text;
                }
                reader = probe;
                if (replacement is null) continue;
                output.Append(Encoding.UTF8.GetString(utf8, copied, start - copied)).Append(replacement);
                copied = (int)reader.BytesConsumed;
            }
        }
        catch (JsonException)
        {
            return new(json, 0);
        }
        if (copied == 0) return new(json, unknown);
        output.Append(Encoding.UTF8.GetString(utf8, copied, utf8.Length - copied));
        return new(output.ToString(), unknown);
    }

    internal static string Rewrite(string text, bool objectIds)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!text.Contains("UUID", StringComparison.Ordinal) && !(objectIds && (text.Contains("ObjectId", StringComparison.Ordinal) || text.Contains("Date", StringComparison.Ordinal)))) return text;
        StringBuilder? result = null;
        var copied = 0;
        for (var i = 0; i < text.Length;)
        {
            var ch = text[i];
            if (ch is '"' or '\'' or '`') { i = SkipQuoted(text, i); continue; }
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '/') { var line = text.IndexOf('\n', i); i = line < 0 ? text.Length : line; continue; }
            if (ch == '/' && i + 1 < text.Length && text[i + 1] == '*') { var close = text.IndexOf("*/", i + 2, StringComparison.Ordinal); i = close < 0 ? text.Length : close + 2; continue; }
            if (ch == '/') { i = SkipRegex(text, i); continue; }
            if (!IsIdentifierStart(ch)) { i++; continue; }
            var end = i + 1;
            while (end < text.Length && IsIdentifierPart(text[end])) end++;
            var name = text[i..end];
            var memberAccess = i > 0 && text[i - 1] == '.';
            var isObjectId = objectIds && name is "ObjectId" or "Date" or "ISODate";
            if (!memberAccess && (isObjectId || UuidCodec.TryParseConstructor(name, out _)))
            {
                var open = SkipWhitespace(text, end);
                if (open < text.Length && text[open] == '(')
                {
                    var replacement = ReadCall(text, i, name, open, out var callEnd);
                    // "new ObjectId(…)" and "new UUID(…)" are accepted by shell-mode JSON; the keyword goes with the call.
                    var start = NewKeywordStart(text, i, copied);
                    (result ??= new StringBuilder(text.Length)).Append(text, copied, start - copied).Append(replacement);
                    copied = callEnd;
                    i = callEnd;
                    continue;
                }
            }
            i = end;
        }
        return result is null ? text : result.Append(text, copied, text.Length - copied).ToString();
    }

    private static string ReadCall(string text, int start, string name, int open, out int end)
    {
        var cursor = SkipWhitespace(text, open + 1);
        if (cursor >= text.Length || text[cursor] is not ('"' or '\'')) throw CallError(text, start, name);
        var quote = text[cursor];
        var close = text.IndexOf(quote, cursor + 1);
        if (close < 0) throw CallError(text, start, name);
        var value = text[(cursor + 1)..close];
        cursor = SkipWhitespace(text, close + 1);
        if (cursor >= text.Length || text[cursor] != ')' || value.Contains('\\', StringComparison.Ordinal)) throw CallError(text, start, name);
        end = cursor + 1;
        if (name is "Date" or "ISODate") return BsonDatePresentation.ToExtendedJson(value);
        if (name == "ObjectId")
        {
            return IdentifierRepresentationService.IsObjectIdHex(value)
                ? IdentifierRepresentationService.ObjectIdToExtendedJson(value)
                : throw new FormatException($"ObjectId(\"{Shorten(value)}\") não contém um ObjectId válido. {IdentifierRepresentationService.ObjectIdHint} {Location(text, start)}");
        }
        if (!UuidCodec.TryParseConstructor(name, out var representation)) throw CallError(text, start, name);
        try
        {
            return UuidCodec.ToExtendedJson(UuidCodec.ParseText(name, value), representation);
        }
        catch (FormatException exception)
        {
            throw new FormatException(exception.Message + " " + Location(text, start), exception);
        }
    }

    private static int NewKeywordStart(string text, int constructorStart, int copied)
    {
        var cursor = constructorStart;
        while (cursor > copied && char.IsWhiteSpace(text[cursor - 1])) cursor--;
        if (cursor == constructorStart || cursor - 3 < copied || !text.AsSpan(cursor - 3, 3).SequenceEqual("new")) return constructorStart;
        return cursor - 3 > 0 && IsIdentifierPart(text[cursor - 4]) ? constructorStart : cursor - 3;
    }

    private static bool TryReadDate(ref Utf8JsonReader reader, out string? literal)
    {
        var probe = reader;
        literal = null;
        if (!probe.Read() || probe.TokenType != JsonTokenType.PropertyName || !probe.ValueTextEquals("$date")) return false;
        probe = reader;
        using var document = JsonDocument.ParseValue(ref probe);
        literal = BsonDatePresentation.Format(document.RootElement);
        if (literal is null) return false;
        reader = probe;
        return true;
    }

    private static bool TryReadObjectId(ref Utf8JsonReader reader, out string hex)
    {
        hex = "";
        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("$oid")) return false;
        if (!reader.Read() || reader.TokenType != JsonTokenType.String) return false;
        var value = reader.GetString()!;
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject || !IdentifierRepresentationService.IsObjectIdHex(value)) return false;
        hex = value;
        return true;
    }

    private static bool TryReadBinary(ref Utf8JsonReader reader, out string base64, out string subType)
    {
        base64 = subType = "";
        if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("$binary")) return false;
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
        string? data = null, kind = null;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isData = reader.ValueTextEquals("base64");
            var isKind = reader.ValueTextEquals("subType");
            if (!reader.Read() || reader.TokenType != JsonTokenType.String || (isData ? data is not null : !isKind || kind is not null)) return false;
            if (isData) data = reader.GetString(); else kind = reader.GetString();
        }
        if (reader.TokenType != JsonTokenType.EndObject || data is null || kind is null) return false;
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject) return false;
        base64 = data; subType = kind;
        return true;
    }

    private static FormatException CallError(string text, int start, string name) =>
        new($"{name}(...) exige um único texto entre aspas. {(name == "ObjectId" ? IdentifierRepresentationService.ObjectIdHint : "Use 32 dígitos hexadecimais, com ou sem hífens.")} {Location(text, start)}");

    private static string Location(string text, int index)
    {
        var line = 1;
        var lineStart = 0;
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '\n') { line++; lineStart = i + 1; }
        }
        return $"(linha {line}, coluna {index - lineStart + 1})";
    }

    private static int SkipQuoted(string text, int start)
    {
        var quote = text[start];
        for (var i = start + 1; i < text.Length; i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == quote) return i + 1;
        }
        return text.Length;
    }

    /// <summary>Skips a /pattern/flags literal on one line; a slash without closing delimiter is ordinary text.</summary>
    private static int SkipRegex(string text, int start)
    {
        var inClass = false;
        for (var i = start + 1; i < text.Length && text[i] is not ('\n' or '\r'); i++)
        {
            if (text[i] == '\\') i++;
            else if (text[i] == '[') inClass = true;
            else if (text[i] == ']') inClass = false;
            else if (text[i] == '/' && !inClass) return i + 1;
        }
        return start + 1;
    }

    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
        return index;
    }

    private static bool IsIdentifierStart(char ch) => char.IsLetter(ch) || ch is '_' or '$';
    private static bool IsIdentifierPart(char ch) => char.IsLetterOrDigit(ch) || ch is '_' or '$';
    private static string Shorten(string value) => value.Length <= 60 ? value : value[..60] + "…";
}
