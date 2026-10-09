using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record CurrentActivityEntry(
    string Kind,
    long? OperationId,
    string? Host,
    string? Operation,
    string? Namespace,
    bool? Active,
    long? SecondsRunning,
    bool? WaitingForLock,
    string? Locks,
    string? SessionTag,
    string? CommentTag);

public sealed record CurrentActivitySnapshot(string Source, IReadOnlyList<CurrentActivityEntry> Entries, bool LimitReached)
{
    public const int MaximumEntries = 200;

    public static CurrentActivitySnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("inprog", out var inProgress)
            || inProgress.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("A resposta de operações não contém a lista esperada.", nameof(json));

        var source = root.TryGetProperty("source", out var sourceValue)
            && sourceValue.ValueKind == JsonValueKind.String
            && sourceValue.GetString() == "currentOp (legacy)"
            ? "currentOp (legacy)" : "$currentOp";
        var entries = new List<CurrentActivityEntry>(MaximumEntries);
        foreach (var entry in inProgress.EnumerateArray())
        {
            if (entries.Count == MaximumEntries)
                break;
            if (entry.ValueKind != JsonValueKind.Object)
                continue;

            var kind = SafeText(entry, "type", 32) == "idleSession" ? "idleSession" : "operation";
            var command = entry.TryGetProperty("command", out var commandValue)
                && commandValue.ValueKind == JsonValueKind.Object ? commandValue : default;
            var comment = command.ValueKind == JsonValueKind.Object
                && command.TryGetProperty("comment", out var nestedComment) ? nestedComment
                : entry.TryGetProperty("comment", out var topComment) ? topComment : default;
            var session = entry.TryGetProperty("lsid", out var lsid) ? lsid : default;
            entries.Add(new CurrentActivityEntry(
                kind,
                ReadLong(entry, "opid"),
                SafeText(entry, "host", 128),
                SafeText(entry, "op", 32),
                SafeText(entry, "ns", 128),
                ReadBoolean(entry, "active"),
                ReadLong(entry, "secs_running"),
                ReadBoolean(entry, "waitingForLock"),
                ReadLocks(entry),
                Tag(session),
                Tag(comment)));
        }

        var truncated = inProgress.GetArrayLength() > MaximumEntries
            || root.TryGetProperty("truncated", out var truncatedValue)
                && truncatedValue.ValueKind == JsonValueKind.True;
        return new CurrentActivitySnapshot(source, entries, truncated);
    }

    public static string? CommentTag(string? comment)
    {
        if (string.IsNullOrEmpty(comment))
            return null;
        return Tag(JsonSerializer.SerializeToElement(comment));
    }

    private static string? Tag(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.GetRawText()));
        return Convert.ToHexString(bytes.AsSpan(0, 8));
    }

    private static string? SafeText(JsonElement objectValue, string property, int maxLength)
    {
        if (!objectValue.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        if (string.IsNullOrEmpty(text))
            return null;
        return new string(text.Where(character => !char.IsControl(character)).Take(maxLength).ToArray());
    }

    private static bool? ReadBoolean(JsonElement objectValue, string property) =>
        objectValue.TryGetProperty(property, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            }
            : null;

    private static long? ReadLong(JsonElement objectValue, string property)
    {
        if (!objectValue.TryGetProperty(property, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Object
            && (value.TryGetProperty("$numberLong", out var wrapped)
                || value.TryGetProperty("$numberInt", out wrapped)))
            value = wrapped;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt64(out var numeric) => numeric,
            JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null
        };
    }

    private static string? ReadLocks(JsonElement entry)
    {
        if (!entry.TryGetProperty("locks", out var locks) || locks.ValueKind != JsonValueKind.Object)
            return null;
        var items = new List<string>(8);
        foreach (var lockField in locks.EnumerateObject())
        {
            if (items.Count == 8)
                break;
            if (lockField.Value.ValueKind != JsonValueKind.String)
                continue;
            var mode = lockField.Value.GetString();
            if (mode is not ("R" or "W" or "r" or "w"))
                continue;
            var name = new string(lockField.Name.Where(char.IsLetterOrDigit).Take(32).ToArray());
            if (name.Length > 0)
                items.Add(name + ":" + mode);
        }

        return items.Count == 0 ? null : string.Join(",", items);
    }
}
