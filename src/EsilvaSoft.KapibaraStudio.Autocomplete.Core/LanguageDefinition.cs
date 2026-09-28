using System.Collections.Frozen;
using System.Text.Json;

namespace EsilvaSoft.KapibaraStudio.Autocomplete.Core;

/// <summary>MongoDB language knowledge loaded from versioned data: symbols, signatures, shapes and snippets.</summary>
public sealed class LanguageDefinition
{
    public const string ResourceName = "EsilvaSoft.KapibaraStudio.Autocomplete.Core.mongodb-language.v1.json";

    private static readonly FrozenSet<string> ShapeRules = new[] { "FieldPath", "Operator", "Fixed", "Dynamic", "Exclusive" }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly Lazy<LanguageDefinition> DefaultInstance = new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    private LanguageDefinition(int version, IReadOnlyList<CatalogSymbol> symbols, IReadOnlyDictionary<string, ShapeDefinition> shapes)
    {
        Version = version;
        Symbols = symbols;
        Shapes = shapes;
    }

    /// <summary>Value kinds that are not object shapes.</summary>
    public static IReadOnlySet<string> PrimitiveValues { get; } = new[]
    {
        "Any", "Value", "FieldValue", "Boolean", "Integer", "NonNegativeInteger", "Number", "String", "Regex", "Date", "Null",
        "Array", "Document", "FieldPathString", "FieldReference", "CollectionNameString", "DatabaseNameString", "IndexNameString",
        "NewFieldName", "SortDirection", "IndexDirection", "BsonTypeAlias", "Cursor", "Collection", "Database", "Connection"
    }.ToFrozenSet(StringComparer.Ordinal);

    public static LanguageDefinition Default => DefaultInstance.Value;

    public int Version { get; }
    public IReadOnlyList<CatalogSymbol> Symbols { get; }
    public IReadOnlyDictionary<string, ShapeDefinition> Shapes { get; }

    public IEnumerable<CatalogSymbol> OfKinds(SymbolKinds kinds) => Symbols.Where(symbol => (kinds & symbol.Kind.ToFlag()) != 0);

    public static LanguageDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var version = root.GetProperty("version").GetInt32();
            if (version != 1) throw new InvalidDataException("Versão de linguagem MongoDB não suportada.");
            var symbols = new List<CatalogSymbol>();
            foreach (var group in root.GetProperty("groups").EnumerateArray()) symbols.AddRange(ParseGroup(group));
            foreach (var snippet in root.GetProperty("snippets").EnumerateArray()) symbols.Add(ParseSnippet(snippet));
            var shapes = root.GetProperty("shapes").EnumerateObject().ToFrozenDictionary(property => property.Name, property => ParseShape(property.Name, property.Value), StringComparer.Ordinal);
            return new(version, symbols.ToArray(), shapes);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("Arquivo de linguagem MongoDB inválido.", exception);
        }
    }

    /// <summary>Structural integrity: unique ids, known references, valid snippets. Empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach (var duplicate in Symbols.GroupBy(symbol => symbol.Id, StringComparer.Ordinal).Where(group => group.Skip(1).Any()))
            errors.Add("Símbolo duplicado: " + duplicate.Key);
        foreach (var symbol in Symbols)
        {
            if (string.IsNullOrWhiteSpace(symbol.Name) || string.IsNullOrWhiteSpace(symbol.Detail)) errors.Add("Nome ou descrição ausente: " + symbol.Id);
            if (symbol.Dialects == EditorDialects.None) errors.Add("Símbolo sem dialeto: " + symbol.Id);
            foreach (var parameter in symbol.Parameters) CheckReference(parameter, symbol.Id);
            if (symbol.ValueShape is { } shape) CheckReference(shape, symbol.Id);
            if (symbol.Returns is { } returns) CheckReference(returns, symbol.Id);
            if (symbol.Snippet is { } snippet && !SnippetSyntax.IsValid(snippet)) errors.Add("Snippet inválido: " + symbol.Id);
        }
        foreach (var shape in Shapes.Values)
        {
            if (shape.Keys.Count == 0 && shape.Values.Count == 0 && shape.Element is null) errors.Add("Shape vazio: " + shape.Id);
            foreach (var rule in shape.Keys)
            {
                if (!ShapeRules.Contains(rule.Rule)) errors.Add($"Regra desconhecida {rule.Rule} em {shape.Id}");
                if (rule.Rule == "Fixed" && rule.Names.Count == 0) errors.Add("Regra Fixed sem nomes em " + shape.Id);
                if (rule.Rule is "Operator" or "Exclusive" && rule.Kinds.Count == 0 && rule.Names.Count == 0) errors.Add("Regra de símbolo sem tipos em " + shape.Id);
                if (rule.Value is { } value) CheckReference(value, shape.Id);
            }
            foreach (var value in shape.Values) CheckReference(value, shape.Id);
            if (shape.Element is { } element) CheckReference(element, shape.Id);
        }
        return errors;

        void CheckReference(string reference, string owner)
        {
            if (!Shapes.ContainsKey(reference) && !PrimitiveValues.Contains(reference)) errors.Add($"Referência desconhecida {reference} em {owner}");
        }
    }

    private static LanguageDefinition LoadEmbedded()
    {
        using var stream = typeof(LanguageDefinition).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidDataException("Recurso de linguagem MongoDB ausente.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    private static IEnumerable<CatalogSymbol> ParseGroup(JsonElement group)
    {
        var kind = Enum.Parse<SymbolKind>(RequiredString(group, "kind"));
        var category = OptionalString(group, "category") ?? "";
        var detail = OptionalString(group, "detail") ?? "";
        var dialects = ParseFlags(group, "dialects", EditorDialects.Mql);
        var flags = ParseFlags(group, "flags", SymbolTraits.None);
        var valueShape = OptionalString(group, "valueShape");
        var types = Strings(group, "applicableTypes");
        foreach (var entry in group.GetProperty("symbols").EnumerateArray())
        {
            switch (entry.ValueKind)
            {
                case JsonValueKind.String:
                    yield return Create(entry.GetString()!, detail) with { Category = category, Dialects = dialects, Flags = flags, ValueShape = valueShape, ApplicableTypes = types };
                    break;
                case JsonValueKind.Array:
                    yield return Create(entry[0].GetString()!, entry.GetArrayLength() > 1 ? entry[1].GetString()! : detail)
                        with { Category = category, Dialects = dialects, Flags = flags, ValueShape = valueShape, ApplicableTypes = types };
                    break;
                case JsonValueKind.Object:
                    yield return Create(RequiredString(entry, "name"), OptionalString(entry, "detail") ?? detail) with
                    {
                        Category = OptionalString(entry, "category") ?? category,
                        Dialects = ParseFlags(entry, "dialects", dialects),
                        Flags = ParseFlags(entry, "flags", flags),
                        ValueShape = OptionalString(entry, "valueShape") ?? valueShape,
                        ApplicableTypes = entry.TryGetProperty("applicableTypes", out _) ? Strings(entry, "applicableTypes") : types,
                        Parameters = Strings(entry, "parameters"),
                        Returns = OptionalString(entry, "returns"),
                        Snippet = OptionalString(entry, "snippet"),
                        Since = OptionalString(entry, "since")
                    };
                    break;
                default:
                    throw new InvalidDataException("Símbolo inválido na linguagem MongoDB.");
            }
        }

        CatalogSymbol Create(string name, string description) => new($"{kind}/{name}", kind, name, description);
    }

    private static CatalogSymbol ParseSnippet(JsonElement snippet)
    {
        var id = RequiredString(snippet, "id");
        return new CatalogSymbol("Snippet/" + id, SymbolKind.Snippet, id, RequiredString(snippet, "detail"))
        {
            Category = OptionalString(snippet, "category") ?? "",
            Dialects = ParseFlags(snippet, "dialects", EditorDialects.All),
            Flags = ParseFlags(snippet, "flags", SymbolTraits.None),
            ValueShape = OptionalString(snippet, "shape"),
            Snippet = RequiredString(snippet, "body")
        };
    }

    private static ShapeDefinition ParseShape(string id, JsonElement shape) => new(id)
    {
        Keys = shape.TryGetProperty("keys", out var keys) ? keys.EnumerateArray().Select(rule => new ShapeKeyRule(RequiredString(rule, "rule"))
        {
            Names = Strings(rule, "names"),
            Kinds = Strings(rule, "kinds").Select(Enum.Parse<SymbolKind>).ToArray(),
            Categories = Strings(rule, "categories"),
            Value = OptionalString(rule, "value"),
            Required = rule.TryGetProperty("required", out var required) && required.GetBoolean(),
            Scope = OptionalString(rule, "scope")
        }).ToArray() : [],
        Values = Strings(shape, "values"),
        Element = OptionalString(shape, "element")
    };

    private static string RequiredString(JsonElement element, string name) =>
        element.GetProperty(name).GetString() is { Length: > 0 } value ? value : throw new InvalidDataException($"Campo {name} ausente.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string[] Strings(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value.EnumerateArray().Select(item => item.GetString() ?? throw new InvalidDataException($"Valor inválido em {name}.")).ToArray() : [];

    private static TEnum ParseFlags<TEnum>(JsonElement element, string name, TEnum fallback) where TEnum : struct, Enum =>
        element.TryGetProperty(name, out var value)
            ? (TEnum)Enum.ToObject(typeof(TEnum), value.EnumerateArray().Aggregate(0, (all, item) => all | Convert.ToInt32(Enum.Parse<TEnum>(item.GetString()!), System.Globalization.CultureInfo.InvariantCulture)))
            : fallback;
}
