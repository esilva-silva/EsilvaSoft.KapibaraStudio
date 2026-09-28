using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Bounded, lexical field inference for complete stages before the cursor. Never evaluates code or fetches data.</summary>
public static class AggregationFieldInference
{
    public static IReadOnlyList<string> Infer(string prefix, IEnumerable<string> sourceFields, Func<string, IReadOnlyList<string>>? foreignFields = null)
    {
        var source = sourceFields.Take(512).ToHashSet(StringComparer.Ordinal);
        if (prefix.Length > 65536) return source.Order(StringComparer.Ordinal).ToArray();
        var tokens = new SyntaxHighlightingService().Highlight(prefix, SyntaxLanguage.MongoScript).Tokens
            .Where(t => t.Type != SyntaxTokenType.Comment).Take(8193).ToArray();
        if (tokens.Length > 8192) return source.Order(StringComparer.Ordinal).ToArray();
        var parser = new ShapeParser(prefix, tokens);
        var start = -1;
        for (var i = 0; i < tokens.Length; i++)
            if (parser.Text(i) == "[" && (i == 0 || i > 1 && parser.Text(i - 1) == "(" && parser.Text(i - 2) == "aggregate")) start = i;
        if (start < 0) return source.Order(StringComparer.Ordinal).ToArray();
        var root = parser.Read(ref start, 0);
        return Pipeline(root, source, true, 0).Order(StringComparer.Ordinal).Take(512).ToArray();

        HashSet<string> Foreign(Shape? lookup)
        {
            var from = lookup?.Property("from")?.Value;
            return from is null ? new(StringComparer.Ordinal) : (foreignFields?.Invoke(from) ?? []).Take(512).ToHashSet(StringComparer.Ordinal);
        }
        HashSet<string> Pipeline(Shape? pipeline, HashSet<string> input, bool atCursor, int depth)
        {
            var fields = new HashSet<string>(input, StringComparer.Ordinal);
            if (pipeline?.Kind != '[' || depth > 32) return fields;
            foreach (var stage in pipeline.Items)
            {
                if (stage.Kind != '{' || stage.Properties.Count != 1) continue;
                var (name, body) = stage.Properties[0];
                if (!stage.Complete && atCursor)
                {
                    if (name == "$lookup")
                    {
                        var nested = body.Property("pipeline");
                        if (nested is { Complete: false })
                        {
                            var scoped = Pipeline(nested, Foreign(body), true, depth + 1);
                            if (body.Property("let") is { } variables)
                                foreach (var variable in variables.Properties) scoped.Add("$" + variable.Key);
                            return scoped;
                        }
                        var current = body.Properties.LastOrDefault().Key;
                        if (current == "foreignField") return Foreign(body);
                        if (current is "as" or "from") return new(StringComparer.Ordinal);
                    }
                    if (name == "$facet")
                    {
                        var active = body.Properties.LastOrDefault().Value;
                        if (active is { Kind: '[', Complete: false }) return Pipeline(active, fields, true, depth + 1);
                    }
                    return fields;
                }
                if (!stage.Complete) break;
                switch (name)
                {
                    case "$group":
                        fields = Output(body, fields, false); break;
                    case "$project":
                        var specification = FlattenProjection(body);
                        var inclusion = specification.Properties.Any(p => p.Key != "_id" && !Excluded(p.Value)) ||
                            specification.Properties.Count == 1 && specification.Properties[0].Key == "_id" && !Excluded(specification.Properties[0].Value);
                        if (inclusion)
                        {
                            var projected = Output(specification, fields, true);
                            if (body.Property("_id") is null) CopyPath(projected, fields, "_id", "_id");
                            fields = projected;
                        }
                        else foreach (var property in specification.Properties) if (Excluded(property.Value)) RemovePath(fields, property.Key);
                        break;
                    case "$set":
                        var additions = Output(body, fields, false);
                        foreach (var property in body.Properties) RemovePath(fields, property.Key);
                        fields.UnionWith(additions); break;
                    case "$unset":
                        if (body.Kind == '[') foreach (var item in body.Items) RemovePath(fields, item.Value);
                        else RemovePath(fields, body.Value);
                        break;
                    case "$lookup":
                        var alias = body.Property("as")?.Value;
                        if (!string.IsNullOrEmpty(alias))
                        {
                            RemovePath(fields, alias); fields.Add(alias);
                            var joined = Foreign(body);
                            if (body.Property("pipeline") is { } nested) joined = Pipeline(nested, joined, false, depth + 1);
                            foreach (var field in joined) fields.Add(alias + "." + field);
                        }
                        break;
                    case "$facet":
                        var facets = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var facet in body.Properties)
                        {
                            facets.Add(facet.Key);
                            foreach (var field in Pipeline(facet.Value, fields, false, depth + 1)) facets.Add(facet.Key + "." + field);
                        }
                        fields = facets; break;
                    case "$count":
                        fields.Clear(); if (body.Value.Length > 0) fields.Add(body.Value); break;
                    case "$match": case "$sort": case "$limit": case "$skip": case "$unwind": break;
                    default: fields.Clear(); break; // Unknown transformations must not advertise the previous shape as certain.
                }
                if (fields.Count > 512) fields = fields.Take(512).ToHashSet(StringComparer.Ordinal);
            }
            return fields;
        }
    }

    private static bool Excluded(Shape shape) => shape.Kind == 'v' && shape.Value is "0" or "false";
    private static Shape FlattenProjection(Shape body)
    {
        var flat = new Shape('{');
        foreach (var property in body.Properties) Add(property.Key, property.Value);
        return flat;
        void Add(string path, Shape value)
        {
            if (value.Kind == '{' && value.Properties.Count > 0 && value.Properties.All(p => !p.Key.StartsWith('$')))
                foreach (var property in value.Properties) Add(path + "." + property.Key, property.Value);
            else flat.Properties.Add(new(path, value));
        }
    }
    private static HashSet<string> Output(Shape body, HashSet<string> input, bool projection)
    {
        var output = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in body.Properties)
        {
            if (projection && Excluded(property.Value)) continue;
            Add(property.Key, property.Value);
        }
        return output;
        void Add(string path, Shape value)
        {
            output.Add(path);
            for (var dot = path.IndexOf('.', StringComparison.Ordinal); dot >= 0; dot = path.IndexOf('.', dot + 1)) output.Add(path[..dot]);
            if (value.Kind == 's' && value.Value.StartsWith('$') && !value.Value.StartsWith("$$", StringComparison.Ordinal))
                CopyPath(output, input, value.Value[1..], path);
            else if (projection && value.Kind == 'v' && value.Value is "1" or "true") CopyPath(output, input, path, path);
            else if (value.Kind == '{' && value.Properties.All(p => !p.Key.StartsWith('$')))
                foreach (var child in value.Properties) Add(path + "." + child.Key, child.Value);
        }
    }
    private static void CopyPath(HashSet<string> output, HashSet<string> input, string source, string destination)
    {
        foreach (var field in input)
            if (field == source || field.StartsWith(source + ".", StringComparison.Ordinal)) output.Add(destination + field[source.Length..]);
    }
    private static void RemovePath(HashSet<string> fields, string path) => fields.RemoveWhere(f => f == path || f.StartsWith(path + ".", StringComparison.Ordinal));

    private sealed class Shape(char kind, string value = "")
    {
        public char Kind { get; } = kind;
        public string Value { get; } = value;
        public bool Complete { get; set; }
        public List<KeyValuePair<string, Shape>> Properties { get; } = [];
        public List<Shape> Items { get; } = [];
        public Shape? Property(string key) => Properties.LastOrDefault(p => p.Key == key).Value;
    }

    private sealed class ShapeParser(string source, SyntaxToken[] tokens)
    {
        public string Text(int index) => index >= 0 && index < tokens.Length ? source.Substring(tokens[index].Start, tokens[index].Length) : "";
        public Shape Read(ref int index, int depth)
        {
            if (index >= tokens.Length || depth > 64) { index = tokens.Length; return new('v'); }
            var value = Text(index++);
            if (value is "{" or "[")
            {
                var node = new Shape(value[0]); var close = value == "{" ? "}" : "]";
                while (index < tokens.Length)
                {
                    if (Text(index) == close) { index++; node.Complete = true; return node; }
                    if (Text(index) == ",") { index++; continue; }
                    if (node.Kind == '{')
                    {
                        var key = Decode(Text(index++));
                        if (Text(index) != ":") return node;
                        index++; node.Properties.Add(new(key, Read(ref index, depth + 1)));
                    }
                    else node.Items.Add(Read(ref index, depth + 1));
                }
                return node;
            }
            var quoted = value.StartsWith('"') || value.StartsWith('\'');
            var scalar = new Shape(quoted ? 's' : 'v', quoted ? Decode(value) : value)
            { Complete = !quoted || value.Length > 1 && value[^1] == value[0] };
            // BSON constructor calls and expressions are opaque; consume their tokens, never execute them.
            if (Text(index) == "(")
            {
                var balance = 0;
                do { var current = Text(index++); if (current == "(") balance++; if (current == ")") balance--; }
                while (index < tokens.Length && balance > 0);
                scalar.Complete = balance == 0;
            }
            return scalar;
        }
        private static string Decode(string value)
        {
            if (value.StartsWith('"')) { try { return JsonSerializer.Deserialize<string>(value) ?? ""; } catch (JsonException) { return ""; } }
            if (value.StartsWith('\'')) return value.Length > 1 && value[^1] == '\'' && !value.Contains('\\') ? value[1..^1] : "";
            return value;
        }
    }
}
