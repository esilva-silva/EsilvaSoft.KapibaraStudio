using System.Globalization;
using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;

/// <summary>
/// Resolves a caret's namespace chain (db/getConnection/const aliases) from the tokens of one document.
/// Implementation detail of <see cref="NamespaceTargetResolver"/>: pure, bounded, single-use per call.
/// </summary>
internal sealed class NamespaceChainReader(string document, List<MongoToken> tokens, NamespaceTarget? tabTarget, CancellationToken cancellationToken)
{
    private enum Receiver { Connection, Database, Collection, Cursor, Result }
    private sealed record Reference(NamespaceTarget Target, Receiver Receiver, bool IsStatic = true);
    private readonly record struct Statement(int Start, int End);

    private readonly Dictionary<string, Reference> _aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _declarations = new(StringComparer.Ordinal);
    private readonly HashSet<string> _writes = new(StringComparer.Ordinal);

    public NamespaceTarget Resolve(int caret)
    {
        var statements = SplitStatements();
        if (statements is null) return NamespaceTarget.Unknown;
        for (var i = 0; i < tokens.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Value(i) is "const" or "let" or "var" or "function" or "class")
            {
                // Destructuring and generators need scope analysis, outside this subset.
                if (!IsIdentifier(i + 1)) return NamespaceTarget.Unknown;
                var name = Value(i + 1);
                _declarations[name] = _declarations.GetValueOrDefault(name) + 1;
            }
            if (IsIdentifier(i) && tokens[i].Start < caret &&
                Value(i - 1) is not ("const" or "let" or "var") &&
                (Value(i + 1) == "=" || Value(i + 2) == "=" && Value(i + 1) is "+" or "-" or "*" or "/" or "%" ||
                    Value(i + 1) is "+" or "-" && Value(i + 2) == Value(i + 1) ||
                    Value(i - 1) is "+" or "-" && Value(i - 2) == Value(i - 1)))
                _writes.Add(Value(i));
        }

        foreach (var statement in statements)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tokens[statement.Start].Start >= caret) break;
            var end = statement.Start;
            while (end < statement.End && tokens[end].Start < caret) end++;
            var isEarlier = statement.End < tokens.Count && tokens[statement.End].Start < caret;
            if (isEarlier)
            {
                AddAlias(statement);
                continue;
            }
            // A consumed semicolon starts a new, empty statement, never the previous namespace.
            if (statement.End < tokens.Count && Value(statement.End) == ";" && tokens[statement.End].End <= caret)
                return NamespaceTarget.Unknown;
            var start = statement.Start;
            if (Value(start) == "const" && IsIdentifier(start + 1) && Value(start + 2) == "=") start += 3;
            var reference = ParseChain(start, end, caret, allowOpenCall: true);
            return reference?.Target ?? NamespaceTarget.Unknown;
        }
        return NamespaceTarget.Unknown;
    }

    private List<Statement>? SplitStatements()
    {
        var result = new List<Statement>();
        var stack = new Stack<string>();
        var start = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = Value(i);
            if (stack.Count == 0 && i > start && IsIdentifier(i) && value is not ("in" or "instanceof") &&
                CanEndExpression(i - 1) && document.AsSpan(tokens[i - 1].End, tokens[i].Start - tokens[i - 1].End).IndexOfAny('\r', '\n') >= 0)
            {
                result.Add(new(start, i));
                start = i;
            }
            if (value is "(" or "[" or "{")
            {
                stack.Push(value);
                if (stack.Count > NamespaceTargetResolver.MaximumDepth) return null;
            }
            else if (value is ")" or "]" or "}")
            {
                if (stack.Count == 0 || !Matches(stack.Pop(), value)) return null;
            }
            else if (value == ";" && stack.Count == 0)
            {
                if (start < i) result.Add(new(start, i));
                start = i + 1;
            }
        }
        if (start < tokens.Count) result.Add(new(start, tokens.Count));
        return result;
    }

    private void AddAlias(Statement statement)
    {
        var start = statement.Start;
        if (Value(start) != "const" || !IsIdentifier(start + 1) || Value(start + 2) != "=") return;
        var name = Value(start + 1);
        if (_declarations.GetValueOrDefault(name) != 1 || _writes.Contains(name)) return;
        var reference = ParseChain(start + 3, statement.End, document.Length, allowOpenCall: false);
        if (reference is { IsStatic: true })
            _aliases[name] = reference with { Target = reference.Target with { Confidence = NamespaceTargetConfidence.Inferred } };
    }

    private Reference? ParseChain(int start, int end, int caret, bool allowOpenCall)
    {
        if (start >= end || !IsIdentifier(start) || tokens[start].End > caret) return null;
        var root = Value(start);
        if (_writes.Contains(root)) return null;
        var cursor = start + 1;
        Reference? reference;
        if (root is "db" or "getConnection" or "ConnectionPool")
        {
            if (_declarations.ContainsKey(root)) return null;
            if (root == "db")
            {
                if (string.IsNullOrEmpty(tabTarget?.Database)) return null;
                reference = new(tabTarget with { Collection = null, Confidence = NamespaceTargetConfidence.Explicit }, Receiver.Database);
            }
            else
            {
                string? name;
                if (root == "getConnection") name = Argument(ref cursor, end, caret);
                else
                {
                    name = Segment(ref cursor, end, caret);
                    if (name == "get" && cursor < end && Value(cursor) == "(") name = Argument(ref cursor, end, caret);
                }
                if (string.IsNullOrEmpty(name)) return null;
                var identity = string.Equals(name, tabTarget?.ConnectionName, StringComparison.Ordinal) ? tabTarget?.Connection : null;
                reference = new(new(name, null, null, NamespaceTargetConfidence.Explicit) { Connection = identity }, Receiver.Connection);
            }
        }
        else if (!_aliases.TryGetValue(root, out reference)) return null;

        while (cursor < end)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Value(cursor) == "." && cursor + 1 == end) return allowOpenCall ? reference : null;
            var indexed = Value(cursor) == "[";
            var segment = Segment(ref cursor, end, caret);
            if (segment is null) return null;
            var call = cursor < end && Value(cursor) == "(";
            if ((!indexed || call) && ((reference.Receiver == Receiver.Connection && segment == "getDatabase") ||
                (reference.Receiver == Receiver.Database && segment is "getSiblingDB" or "getCollection")))
            {
                var name = Argument(ref cursor, end, caret);
                if (name is null) return null;
                reference = segment == "getCollection"
                    ? reference with { Target = reference.Target with { Collection = name }, Receiver = Receiver.Collection }
                    : reference with { Target = reference.Target with { Database = name, Collection = null }, Receiver = Receiver.Database };
            }
            else if (reference.Receiver is Receiver.Connection or Receiver.Database && !call)
            {
                reference = reference.Receiver == Receiver.Connection
                    ? reference with { Target = reference.Target with { Database = segment }, Receiver = Receiver.Database }
                    : reference with { Target = reference.Target with { Collection = segment }, Receiver = Receiver.Collection };
            }
            else if (IsKnownMethod(reference.Receiver, segment))
            {
                reference = reference with { IsStatic = false };
                if (!call) return cursor == end && allowOpenCall ? reference : null;
                var closed = SkipCall(ref cursor, end);
                if (closed is null || !closed.Value && !allowOpenCall) return null;
                if (!closed.Value) return reference;
                reference = reference with { Receiver = segment is "find" or "aggregate" || reference.Receiver == Receiver.Cursor ? Receiver.Cursor : Receiver.Result };
            }
            else return null;
        }
        return reference;
    }

    private string? Segment(ref int cursor, int end, int caret)
    {
        if (cursor + 1 < end && Value(cursor) == "." && IsIdentifier(cursor + 1))
        {
            var token = tokens[cursor + 1];
            cursor += 2;
            return token.End <= caret ? document.Substring(token.Start, token.Length) : null;
        }
        if (cursor + 2 < end && Value(cursor) == "[" && Value(cursor + 2) == "]")
        {
            var value = Literal(cursor + 1, caret);
            cursor += 3;
            return value;
        }
        return null;
    }

    private string? Argument(ref int cursor, int end, int caret)
    {
        if (cursor + 2 >= end || Value(cursor) != "(" || Value(cursor + 2) != ")") return null;
        var value = Literal(cursor + 1, caret);
        cursor += 3;
        return value;
    }

    private string? Literal(int index, int caret)
    {
        var token = tokens[index];
        if (token.Kind != MongoTokenKind.String || !token.IsTerminated || token.Has(MongoTokenTraits.Continuation) || token.End > caret)
            return null;
        var raw = document.AsSpan(token.Start + 1, token.Length - 2);
        var builder = new StringBuilder(raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var ch = raw[i];
            if (ch < ' ') return null;
            if (ch != '\\') { builder.Append(ch); continue; }
            if (++i >= raw.Length) return null;
            ch = raw[i];
            if (ch is '\\' or '\'' or '"' or '/') builder.Append(ch);
            else if (ch is 'u' or 'x')
            {
                var digits = ch == 'u' ? 4 : 2;
                if (i + digits >= raw.Length || !ushort.TryParse(raw.Slice(i + 1, digits), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out var code) || code < ' ') return null;
                builder.Append((char)code);
                i += digits;
            }
            else return null; // Unknown/legacy escapes are deliberately unresolved.
        }
        return builder.Length == 0 ? null : builder.ToString();
    }

    private bool? SkipCall(ref int cursor, int end)
    {
        var stack = new Stack<string>();
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = Value(cursor++);
            // Scope-bearing expressions and nested namespace chains need the caller's cursor/shape analysis.
            if (value is "function" or "class" || value == "=" && Value(cursor) == ">" ||
                tokens[cursor - 1].Kind == MongoTokenKind.Identifier && Value(cursor) is "." or "[" &&
                (value is "db" or "ConnectionPool" || _aliases.ContainsKey(value)) ||
                value == "getConnection" && Value(cursor) == "(") return null;
            if (value is "(" or "[" or "{")
            {
                stack.Push(value);
                if (stack.Count > NamespaceTargetResolver.MaximumDepth) return null;
            }
            else if (value is ")" or "]" or "}")
            {
                if (stack.Count == 0 || !Matches(stack.Pop(), value)) return null;
                if (stack.Count == 0) return true;
            }
        } while (cursor < end);
        return false;
    }

    private bool CanEndExpression(int index) => tokens[index].Kind is MongoTokenKind.Identifier or MongoTokenKind.String or
        MongoTokenKind.Number or MongoTokenKind.Regex or MongoTokenKind.Template || Value(index) is ")" or "]" or "}";
    private bool IsIdentifier(int index) => index >= 0 && index < tokens.Count && tokens[index].Kind == MongoTokenKind.Identifier;
    private string Value(int index) => index >= 0 && index < tokens.Count ? document.Substring(tokens[index].Start, tokens[index].Length) : "";
    private static bool Matches(string open, string close) => (open, close) is ("(", ")") or ("[", "]") or ("{", "}");
    private static bool IsKnownMethod(Receiver receiver, string method) => receiver switch
    {
        Receiver.Collection => method is "find" or "findOne" or "aggregate" or "countDocuments" or "estimatedDocumentCount" or
            "distinct" or "insertOne" or "insertMany" or "updateOne" or "updateMany" or "replaceOne" or "deleteOne" or "deleteMany" or
            "findOneAndUpdate" or "findOneAndReplace" or "findOneAndDelete" or "bulkWrite" or "createIndex",
        Receiver.Cursor => method is "sort" or "limit" or "skip" or "project" or "collation" or "hint" or "batchSize" or "maxTimeMS" or
            "allowDiskUse" or "comment",
        _ => false
    };
}
