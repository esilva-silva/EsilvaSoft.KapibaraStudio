using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Resolves explicit Console paths only. Dynamic variables never inherit another collection's fields.</summary>
public static class MongoCompletionTarget
{
    public static SyntaxNamespace? Resolve(string prefix, SyntaxContext context)
    {
        if (prefix.Length > 65536) return null;
        var snapshot = new SyntaxHighlightingService().Highlight(prefix, SyntaxLanguage.MongoScript, context);
        var tokens = snapshot.Tokens.Where(t => t.Type != SyntaxTokenType.Comment).ToArray();
        SyntaxNamespace? target = null;
        for (var i = 0; i < tokens.Length; i++)
        {
            if (Value(i) == ";") { target = null; continue; }
            if (tokens[i].Type is SyntaxTokenType.String or SyntaxTokenType.PropertyName or SyntaxTokenType.Regex) continue;
            var root = Value(i);
            if (root is not ("db" or "ConnectionPool" or "getConnection")) continue;
            target = null;
            var connection = context.Connection; var database = context.Database; string? collection = null;
            var cursor = i + 1;
            if (root == "getConnection")
            {
                connection = Argument(ref cursor) ?? ""; database = "";
            }
            else if (root == "ConnectionPool")
            {
                connection = Segment(ref cursor) ?? ""; database = "";
            }
            while (cursor < tokens.Length)
            {
                var segment = Segment(ref cursor);
                if (segment is null) break;
                if (segment is "getDatabase" or "getSiblingDB") { database = Argument(ref cursor) ?? ""; collection = null; }
                else if (segment == "getCollection") collection = Argument(ref cursor);
                else if (segment is "find" or "findOne" or "aggregate" or "countDocuments" or "distinct")
                {
                    if (connection.Length > 0 && database.Length > 0 && !string.IsNullOrEmpty(collection)) target = new(connection, database, collection);
                    break;
                }
                else if (database.Length == 0) database = segment;
                else if (collection is null) collection = segment;
                else break;
            }
        }
        return target;

        string Value(int index) => index < tokens.Length ? prefix.Substring(tokens[index].Start, tokens[index].Length) : "";
        string? Literal(int index)
        {
            if (index >= tokens.Length) return null;
            var value = Value(index);
            if (value.Length < 2 || value[0] is not ('\'' or '"') || value[^1] != value[0]) return null;
            if (value[0] == '"') { try { return JsonSerializer.Deserialize<string>(value); } catch (JsonException) { return null; } }
            // Ambiguous escape sequences are deliberately unresolved; no evaluation of JavaScript strings.
            return value.Contains('\\') ? null : value[1..^1];
        }
        string? Argument(ref int cursor)
        {
            if (Value(cursor) != "(" || Value(cursor + 2) != ")") return null;
            var value = Literal(cursor + 1); cursor += 3; return value;
        }
        string? Segment(ref int cursor)
        {
            if (Value(cursor) == "." && cursor + 1 < tokens.Length)
            { var value = Value(cursor + 1); cursor += 2; return value; }
            if (Value(cursor) == "[" && Value(cursor + 2) == "]")
            { var value = Literal(cursor + 1); cursor += 3; return value; }
            return null;
        }
    }
}
