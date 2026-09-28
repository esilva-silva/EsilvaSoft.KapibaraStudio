using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>Collects field evidence from results, validators, indexes and samples without retaining values.</summary>
public sealed class SchemaBuilder
{
    private const int MaximumEnumLiterals = 16;
    private const int MaximumEnumLength = 64;
    private readonly Node _root = new("", "", 0);
    private readonly int _maximumDepth;
    private readonly int _maximumNodes;
    private readonly HashSet<Node> _documentFields = [];
    private int _nodes;
    private long _sequence = 1;
    private int _sampleSize;
    private bool _truncated;
    private EvidenceSources _evidence;

    public SchemaBuilder(int maximumDepth = 12, int maximumNodes = 10_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumNodes);
        _maximumDepth = maximumDepth;
        _maximumNodes = maximumNodes;
    }

    /// <summary>Canonical or relaxed Extended JSON documents, such as the results of a tab.</summary>
    public SchemaBuilder AddDocuments(IEnumerable<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        foreach (var document in documents)
        {
            try
            {
                using var json = JsonDocument.Parse(document);
                VisitResult(json.RootElement, _root, 0);
                _evidence |= EvidenceSources.Results;
            }
            catch (JsonException)
            {
                // A document that cannot be read never prevents inference from the others.
            }
        }
        return this;
    }

    /// <summary>A collection validator document or its $jsonSchema content.</summary>
    public SchemaBuilder AddJsonSchema(string validatorJson)
    {
        ArgumentNullException.ThrowIfNull(validatorJson);
        try
        {
            using var json = JsonDocument.Parse(validatorJson);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return this;
            var schema = root.TryGetProperty("$jsonSchema", out var declared) ? declared : root;
            if (schema.ValueKind == JsonValueKind.Object && (schema.TryGetProperty("properties", out _) || schema.TryGetProperty("items", out _)))
            {
                VisitJsonSchema(schema, _root, 0);
                _evidence |= EvidenceSources.Validator;
            }
        }
        catch (JsonException)
        {
            // An unreadable validator adds no evidence.
        }
        return this;
    }

    public SchemaBuilder AddIndexes(IEnumerable<IndexInfo> indexes)
    {
        ArgumentNullException.ThrowIfNull(indexes);
        foreach (var index in indexes)
        {
            try
            {
                using var keys = JsonDocument.Parse(index.Keys);
                if (keys.RootElement.ValueKind != JsonValueKind.Object) continue;
                foreach (var key in keys.RootElement.EnumerateObject())
                {
                    if (key.Name is "_fts" or "_ftsx" || key.Name.Contains("$**", StringComparison.Ordinal)) continue;
                    if (AddPath(key.Name, EvidenceSources.Index, FieldTraits.Indexed)) _evidence |= EvidenceSources.Index;
                }
            }
            catch (JsonException)
            {
                // Index keys come from the server; a malformed definition is ignored.
            }
        }
        return this;
    }

    public SchemaBuilder AddSample(IReadOnlyList<SampledDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        foreach (var document in documents)
        {
            _sampleSize++;
            _documentFields.Clear();
            foreach (var field in document.Fields) VisitSample(field, _root, 0);
        }
        if (documents.Count > 0) _evidence |= EvidenceSources.Sample;
        return this;
    }

    public SchemaBuilder AddSchema(CollectionSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        _sampleSize += schema.SampleSize;
        _evidence |= schema.Evidence;
        _truncated |= schema.IsTruncated;
        Copy(schema.Root, _root, 0);
        return this;
    }

    public CollectionSchema Build() => new(Freeze(_root), _evidence, _sampleSize, _truncated, _nodes);

    private FieldNode Freeze(Node node) => new(node.Name, node.Path, new Dictionary<string, int>(node.Types, StringComparer.Ordinal), node.Occurrences, _sampleSize,
        node.Flags, node.Evidence, node.Children.Values.OrderBy(child => child.Sequence).Select(Freeze).ToArray(), node.Enum?.ToArray() ?? [], node.Sequence);

    private void VisitResult(JsonElement element, Node parent, int depth)
    {
        if (depth >= _maximumDepth)
        {
            if (element.ValueKind is JsonValueKind.Object or JsonValueKind.Array) _truncated = true;
            return;
        }
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (ExtendedJsonType(element) is not null) return;
                foreach (var property in element.EnumerateObject())
                {
                    if (Child(parent, property.Name) is not { } child) continue;
                    child.AddType(TypeOf(property.Value));
                    child.Evidence |= EvidenceSources.Results;
                    VisitResult(property.Value, child, depth + 1);
                }
                break;
            case JsonValueKind.Array:
                if (!ReferenceEquals(parent, _root)) parent.Flags |= FieldTraits.Array;
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object && ExtendedJsonType(item) is null && !ReferenceEquals(parent, _root)) parent.Flags |= FieldTraits.ArrayOfDocuments;
                    VisitResult(item, parent, depth + 1);
                }
                break;
        }
    }

    private void VisitJsonSchema(JsonElement schema, Node node, int depth)
    {
        if (depth >= _maximumDepth) { _truncated = true; return; }
        var required = schema.TryGetProperty("required", out var names) && names.ValueKind == JsonValueKind.Array
            ? names.EnumerateArray().Where(name => name.ValueKind == JsonValueKind.String).Select(name => name.GetString()!).ToHashSet(StringComparer.Ordinal)
            : [];
        if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object || Child(node, property.Name) is not { } child) continue;
                child.Evidence |= EvidenceSources.Validator;
                if (required.Contains(property.Name)) child.Flags |= FieldTraits.Required;
                foreach (var type in DeclaredTypes(property.Value))
                {
                    child.AddType(type);
                    if (type == "array") child.Flags |= FieldTraits.Array;
                }
                if (property.Value.TryGetProperty("enum", out var literals) && literals.ValueKind == JsonValueKind.Array) child.AddEnum(literals);
                VisitJsonSchema(property.Value, child, depth + 1);
            }
        }
        if (!schema.TryGetProperty("items", out var items)) return;
        if (!ReferenceEquals(node, _root)) node.Flags |= FieldTraits.Array;
        if (items.ValueKind == JsonValueKind.Object) VisitItems(items);
        else if (items.ValueKind == JsonValueKind.Array)
            foreach (var item in items.EnumerateArray()) if (item.ValueKind == JsonValueKind.Object) VisitItems(item);

        void VisitItems(JsonElement item)
        {
            if (item.TryGetProperty("properties", out _) && !ReferenceEquals(node, _root)) node.Flags |= FieldTraits.ArrayOfDocuments;
            VisitJsonSchema(item, node, depth + 1);
        }
    }

    private void VisitSample(SampledField field, Node parent, int depth)
    {
        if (depth >= _maximumDepth) { _truncated = true; return; }
        if (Child(parent, field.Name) is not { } child) return;
        child.Evidence |= EvidenceSources.Sample;
        if (_documentFields.Add(child)) child.Occurrences++;
        child.AddType(field.Type);
        if (field.Type == "array") child.Flags |= FieldTraits.Array;
        foreach (var nested in field.Children) VisitSample(nested, child, depth + 1);
        foreach (var element in field.Elements)
        {
            if (element.Type == "object") child.Flags |= FieldTraits.ArrayOfDocuments;
            foreach (var nested in element.Children) VisitSample(nested, child, depth + 1);
        }
    }

    private void Copy(FieldNode source, Node target, int depth)
    {
        if (depth >= _maximumDepth) { _truncated = true; return; }
        foreach (var field in source.OrderedChildren)
        {
            if (Child(target, field.Name) is not { } child) continue;
            foreach (var (type, count) in field.Types) child.AddType(type, count);
            child.Flags |= field.Flags;
            child.Evidence |= field.Evidence;
            child.Occurrences += field.Occurrences;
            foreach (var literal in field.EnumLiterals) child.AddEnum(literal);
            Copy(field, child, depth + 1);
        }
    }

    /// <summary>
    /// Adds one field of persisted schema learning (L15), addressed by explicit segments — never a dotted string —
    /// so a literal field named <c>"Customer.Id"</c> cannot collide with the nested path <c>Customer</c> ›
    /// <c>Id</c> at the call site (the resulting <see cref="FieldNode.Path"/> is still dot-joined display text,
    /// the same pre-existing limitation every other builder method already has). Sets only
    /// <see cref="EvidenceSources.Learned"/> and never touches the sample size, so <see cref="FieldNode.Occurrence"/>
    /// stays null by construction (DEC-L-MERGE): the learned source presents its own frequency, with its own
    /// denominator, independently of this schema's <see cref="CollectionSchema.SampleSize"/>.
    /// </summary>
    public SchemaBuilder AddLearnedField(IReadOnlyList<string> segments, IReadOnlyDictionary<string, long> typeObservations, bool isArray,
        IReadOnlyDictionary<string, long>? arrayElementTypeObservations = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(typeObservations);
        if (segments.Count == 0 || segments.Count > _maximumDepth || segments.Any(string.IsNullOrEmpty)) return this;
        var node = _root;
        foreach (var segment in segments)
        {
            if (Child(node, segment) is not { } child) return this;
            child.Evidence |= EvidenceSources.Learned;
            node = child;
        }
        foreach (var (type, count) in typeObservations) node.AddType(type, (int)Math.Clamp(count, 0, int.MaxValue));
        if (isArray)
        {
            node.Flags |= FieldTraits.Array;
            if (arrayElementTypeObservations is not null && arrayElementTypeObservations.Keys.Any(type => type is "object" or "document"))
                node.Flags |= FieldTraits.ArrayOfDocuments;
        }
        _evidence |= EvidenceSources.Learned;
        return this;
    }

    /// <summary>
    /// Registra um campo inferido de um estágio de agregação: caminho pontilhado, tipos observados, traços e evidência.
    /// Nenhum valor de documento é lido; a chamada apenas declara a existência do campo naquela posição do pipeline.
    /// </summary>
    internal SchemaBuilder AddPipelineField(string path, IEnumerable<string>? types = null, FieldTraits traits = FieldTraits.None,
        EvidenceSources evidence = EvidenceSources.Pipeline)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!AddPath(path, evidence, traits)) return this;
        _evidence |= evidence;
        if (types is null) return this;
        var node = _root;
        foreach (var segment in path.Split('.'))
            if (Child(node, segment) is { } child) node = child; else return this;
        foreach (var type in types) node.AddType(type);
        return this;
    }

    private bool AddPath(string path, EvidenceSources evidence, FieldTraits flags)
    {
        var segments = path.Split('.');
        if (segments.Length > _maximumDepth || segments.Any(segment => segment.Length == 0)) return false;
        var node = _root;
        foreach (var segment in segments)
        {
            if (Child(node, segment) is not { } child) return false;
            child.Evidence |= evidence;
            node = child;
        }
        node.Flags |= flags;
        return true;
    }

    private Node? Child(Node parent, string name)
    {
        if (parent.Children.TryGetValue(name, out var child)) return child;
        if (_nodes >= _maximumNodes)
        {
            _truncated = true;
            parent.Flags |= FieldTraits.Truncated;
            return null;
        }
        child = new Node(name, parent.Path.Length == 0 ? name : parent.Path + "." + name, _sequence++);
        parent.Children.Add(name, child);
        _nodes++;
        return child;
    }

    private static IEnumerable<string> DeclaredTypes(JsonElement schema)
    {
        foreach (var name in new[] { "bsonType", "type" })
        {
            if (!schema.TryGetProperty(name, out var value)) continue;
            var types = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(item => item.GetString()) : [value.GetString()];
            foreach (var type in types.OfType<string>())
                yield return name == "type" ? type switch { "integer" => "int", "boolean" => "bool", _ => type } : type;
        }
    }

    internal static string? ExtendedJsonType(JsonElement element)
    {
        using var properties = element.EnumerateObject();
        if (!properties.MoveNext()) return null;
        var first = properties.Current;
        if (properties.MoveNext()) return null;
        return first.Name switch
        {
            "$oid" => "objectId",
            "$date" => "date",
            "$numberInt" => "int",
            "$numberLong" => "long",
            "$numberDouble" => "double",
            "$numberDecimal" => "decimal",
            "$binary" => first.Value.ValueKind == JsonValueKind.Object && first.Value.TryGetProperty("subType", out var subtype)
                && subtype.GetString() is "03" or "04" ? "uuid" : "binData",
            "$regularExpression" => "regex",
            "$timestamp" => "timestamp",
            _ => null
        };
    }

    private static string TypeOf(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => ExtendedJsonType(element) ?? "object",
        JsonValueKind.Array => "array",
        JsonValueKind.String => "string",
        JsonValueKind.Number => "double",
        JsonValueKind.True or JsonValueKind.False => "bool",
        JsonValueKind.Null => "null",
        _ => "unknown"
    };

    private sealed class Node(string name, string path, long sequence)
    {
        public string Name { get; } = name;
        public string Path { get; } = path;
        public long Sequence { get; } = sequence;
        public Dictionary<string, int> Types { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, Node> Children { get; } = new(StringComparer.Ordinal);
        public List<string>? Enum { get; private set; }
        public FieldTraits Flags { get; set; }
        public EvidenceSources Evidence { get; set; }
        public int Occurrences { get; set; }

        public void AddType(string type, int count = 1) => Types[type] = Types.GetValueOrDefault(type) + count;

        public void AddEnum(JsonElement literals)
        {
            foreach (var literal in literals.EnumerateArray())
                if (literal.ValueKind == JsonValueKind.String) AddEnum(literal.GetString()!);
        }

        public void AddEnum(string literal)
        {
            // Declared schema literals only; secrets recognizable by the privacy filter never enter the catalog.
            if (literal.Length > MaximumEnumLength || CompletionPrivacy.ContainsSensitiveText(literal)) return;
            Enum ??= [];
            if (Enum.Count >= MaximumEnumLiterals || Enum.Contains(literal, StringComparer.Ordinal)) return;
            Enum.Add(literal);
            Flags |= FieldTraits.Enum;
        }
    }
}
