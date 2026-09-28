using System.Buffers;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

/// <summary>
/// Presentation-only indentation of Extended JSON. Token bytes (escapes, numbers and type wrappers) are copied verbatim;
/// only whitespace outside strings changes, so the formatted text denotes exactly the same BSON values.
/// </summary>
public static class ExtendedJsonFormatter
{
    /// <summary>BSON nests up to 100 levels; the extra room keeps console values that wrap documents readable.</summary>
    public const int MaxDepth = 256;

    // Canonical and legacy type wrappers stay on one line, e.g. {"$oid":"…"} and {"$date":{"$numberLong":"…"}}.
    private static readonly HashSet<string> WrapperKeys = new(StringComparer.Ordinal)
    {
        "$oid", "$symbol", "$numberInt", "$numberLong", "$numberDouble", "$numberDecimal", "$binary", "$uuid", "$type", "$code", "$scope",
        "$timestamp", "$regularExpression", "$regex", "$options", "$dbPointer", "$date", "$minKey", "$maxKey", "$undefined"
    };

    /// <summary>Indents objects and arrays with two spaces. Throws <see cref="JsonException"/> for invalid JSON.</summary>
    public static string Format(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var input = Encoding.UTF8.GetBytes(json);
        var output = new ArrayBufferWriter<byte>(input.Length + (input.Length >> 1) + 16);
        var reader = new Utf8JsonReader(input, new JsonReaderOptions { MaxDepth = MaxDepth });
        var hasItems = new bool[MaxDepth + 2];
        var afterName = false;
        var inlineDepth = -1;
        while (reader.Read())
        {
            var depth = reader.CurrentDepth;
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    BeginValue(output, hasItems, depth, ref afterName, inlineDepth >= 0);
                    if (inlineDepth < 0 && reader.TokenType == JsonTokenType.StartObject && IsWrapper(reader)) inlineDepth = depth;
                    output.Write(reader.TokenType == JsonTokenType.StartObject ? "{"u8 : "["u8);
                    hasItems[depth + 1] = false;
                    break;
                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    if (hasItems[depth + 1] && inlineDepth < 0) NewLine(output, depth);
                    output.Write(reader.TokenType == JsonTokenType.EndObject ? "}"u8 : "]"u8);
                    if (inlineDepth == depth) inlineDepth = -1;
                    break;
                case JsonTokenType.PropertyName:
                    Separate(output, hasItems, depth, inlineDepth >= 0);
                    WriteQuoted(output, reader.ValueSpan);
                    output.Write(inlineDepth >= 0 ? ":"u8 : ": "u8);
                    afterName = true;
                    break;
                case JsonTokenType.String:
                    BeginValue(output, hasItems, depth, ref afterName, inlineDepth >= 0);
                    WriteQuoted(output, reader.ValueSpan);
                    break;
                default:
                    BeginValue(output, hasItems, depth, ref afterName, inlineDepth >= 0);
                    output.Write(reader.ValueSpan);
                    break;
            }
        }
        return Encoding.UTF8.GetString(output.WrittenSpan);
    }

    /// <summary>Formats valid JSON; invalid input is returned unchanged together with the parser message.</summary>
    public static bool TryFormat(string json, out string formatted, out string? error)
    {
        try
        {
            formatted = Format(json);
            error = null;
            return true;
        }
        catch (JsonException exception)
        {
            formatted = json;
            error = exception.Message;
            return false;
        }
    }

    private static void BeginValue(ArrayBufferWriter<byte> output, bool[] hasItems, int depth, ref bool afterName, bool inline)
    {
        if (afterName) { afterName = false; return; }
        Separate(output, hasItems, depth, inline);
    }

    private static void Separate(ArrayBufferWriter<byte> output, bool[] hasItems, int depth, bool inline)
    {
        if (depth == 0) return;
        if (hasItems[depth]) output.Write(","u8);
        hasItems[depth] = true;
        if (!inline) NewLine(output, depth);
    }

    private static void NewLine(ArrayBufferWriter<byte> output, int depth)
    {
        var span = output.GetSpan(1 + depth * 2);
        span[0] = (byte)'\n';
        span.Slice(1, depth * 2).Fill((byte)' ');
        output.Advance(1 + depth * 2);
    }

    private static void WriteQuoted(ArrayBufferWriter<byte> output, ReadOnlySpan<byte> raw)
    {
        output.Write("\""u8);
        output.Write(raw);
        output.Write("\""u8);
    }

    /// <summary>Looks ahead on a copy of the reader: one or two known wrapper keys and nothing else.</summary>
    private static bool IsWrapper(Utf8JsonReader probe)
    {
        var depth = probe.CurrentDepth + 1;
        var count = 0;
        while (probe.Read() && probe.CurrentDepth >= depth)
        {
            if (probe.TokenType != JsonTokenType.PropertyName || probe.ValueIsEscaped || probe.ValueSpan.Length < 2 || probe.ValueSpan[0] != (byte)'$'
                || ++count > 2 || !WrapperKeys.Contains(probe.GetString()!)) return false;
            probe.Read();
            if (probe.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) probe.Skip();
        }
        return count > 0;
    }
}
