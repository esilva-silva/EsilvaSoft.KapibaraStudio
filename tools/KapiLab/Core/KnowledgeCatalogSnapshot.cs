using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal static class KnowledgeCatalogSnapshot
{
    internal const string Schema = "kapilab-knowledge-catalog-v1";
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    // Select sources before querying: metadata must never be consulted, even if it claims language kinds.
    internal static Snapshot Export(IEnumerable<ICatalogSource> sources, string dialect, Source source)
    {
        var mask = dialect switch
        {
            "all" => EditorDialects.All,
            "mql" => EditorDialects.Mql,
            "console" => EditorDialects.Console | EditorDialects.Mql,
            "dsl" => EditorDialects.Console,
            _ => throw new ArgumentException("Dialeto inválido: use mql, console, dsl ou all.")
        };
        var catalog = new KnowledgeCatalog(sources.OfType<LanguageCatalogSource>());
        var kinds = Enum.GetValues<SymbolKinds>().Aggregate(SymbolKinds.None, (all, kind) => all | kind);
        var symbols = catalog.Query(new CatalogQuery(kinds, EditorDialects.All) { MaximumCandidates = 100_000 })
            .Candidates.Select(candidate => candidate.Symbol)
            .Where(symbol => (symbol.Kind.ToFlag() & (SymbolKinds.Namespaces | SymbolKinds.Field | SymbolKinds.Index | SymbolKinds.LocalVariable | SymbolKinds.EnvironmentKey)) == 0
                && symbol.Scope is null && symbol.Field is null)
            .Where(symbol => (symbol.Dialects & mask) != 0 &&
                (dialect != "dsl" || symbol.Category is "dsl" or "connection-pool"))
            .OrderBy(symbol => symbol.Kind.ToString(), StringComparer.Ordinal)
            .ThenBy(symbol => symbol.Name, StringComparer.Ordinal).ThenBy(symbol => symbol.Id, StringComparer.Ordinal)
            .Select(symbol => new Symbol(symbol.Id, symbol.Kind.ToString(), symbol.Name, symbol.Category,
                Enum.GetValues<EditorDialects>().Where(value => (value is EditorDialects.Console or EditorDialects.MongoshScript or EditorDialects.AggregationJson or EditorDialects.Mql) && symbol.Dialects.HasFlag(value)).Select(value => value.ToString()).ToArray(),
                symbol.Detail, symbol.ValueShape, symbol.Parameters.ToArray(), symbol.Returns, symbol.Snippet,
                symbol.ApplicableTypes.ToArray(), symbol.Since,
                Enum.GetValues<SymbolTraits>().Where(value => value != SymbolTraits.None && symbol.Flags.HasFlag(value)).Select(value => value.ToString()).ToArray()))
            .ToArray();
        var selectedNames = symbols.Select(symbol => symbol.Name).ToHashSet(StringComparer.Ordinal);
        string[] Names(IEnumerable<string> values, bool visualAliases = false) => values
            .Where(value => dialect == "all" || selectedNames.Contains(value) || visualAliases && dialect is "console" or "dsl")
            .Order(StringComparer.Ordinal).ToArray();
        var vocabulary = new Vocabulary(Names(MongoSyntaxVocabulary.Functions), Names(MongoSyntaxVocabulary.Operators),
            Names(MongoSyntaxVocabulary.AggregationStages), Names(MongoSyntaxVocabulary.AtlasSearchOperators),
            Names(MongoSyntaxVocabulary.ExtendedJsonTypes), Names(MongoSyntaxVocabulary.Keywords),
            Names(MongoSyntaxVocabulary.DslFunctions, true), Names(MongoSyntaxVocabulary.DslRoots, true));
        var counts = new Counts(symbols.Length, symbols.GroupBy(symbol => symbol.Kind).ToDictionary(group => group.Key, group => group.Count()));
        var payload = new { schema = Schema, source, symbols, vocabulary, counts };
        return new(Schema, source, symbols, vocabulary, counts, Hash(JsonSerializer.Serialize(payload, Options)));
    }

    internal static Source EmbeddedSource()
    {
        using var stream = typeof(LanguageDefinition).Assembly.GetManifestResourceStream(LanguageDefinition.ResourceName)
            ?? throw new InvalidDataException("Recurso de linguagem ausente.");
        var environment = EnvironmentReport.Create();
        return new("mongodb-language.v1.json", Convert.ToHexStringLower(SHA256.HashData(stream)), environment.IdeCommit, environment.IdeDirty);
    }

    internal static string Serialize(Snapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);
    internal static Snapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        JsonContractValidation.RequireUniqueProperties(document.RootElement);
        var snapshot = JsonSerializer.Deserialize<Snapshot>(json, Options) ?? throw new InvalidDataException("Snapshot ausente.");
        if (snapshot.Schema != Schema || snapshot.Symbols is null || snapshot.Source is null || snapshot.Vocabulary is null || snapshot.Counts is null ||
            snapshot.Symbols.Any(symbol => symbol is null || string.IsNullOrWhiteSpace(symbol.Id)) ||
            snapshot.Symbols.Select(symbol => symbol.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Symbols.Length)
            throw new InvalidDataException("Snapshot de conhecimento inválido.");
        var payload = new { schema = snapshot.Schema, source = snapshot.Source, symbols = snapshot.Symbols, vocabulary = snapshot.Vocabulary, counts = snapshot.Counts };
        if (Hash(JsonSerializer.Serialize(payload, Options)) != snapshot.SnapshotSha256)
            throw new InvalidDataException("Hash do snapshot inválido.");
        return snapshot;
    }

    internal static string[] Compare(Snapshot expected, Snapshot actual)
    {
        var left = expected.Symbols.ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);
        var right = actual.Symbols.ToDictionary(symbol => symbol.Id, StringComparer.Ordinal);
        var changes = left.Keys.Union(right.Keys).Order(StringComparer.Ordinal).Select(id =>
            !left.TryGetValue(id, out var before) ? "added:" + id : !right.TryGetValue(id, out var after) ? "removed:" + id :
            JsonSerializer.Serialize(before, Options) != JsonSerializer.Serialize(after, Options) ? "changed:" + id : null)
            .Where(change => change is not null).Cast<string>().ToList();
        if (JsonSerializer.Serialize(expected.Vocabulary, Options) != JsonSerializer.Serialize(actual.Vocabulary, Options)) changes.Add("vocabulary_changed");
        return changes.ToArray();
    }

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    internal sealed record Source(string LanguageFile, string LanguageSha256, string? IdeCommit, bool? IdeDirty);
    internal sealed record Symbol(string Id, string Kind, string Name, string Category, string[] Dialects, string Detail,
        string? ValueShape, string[] Parameters, string? Returns, string? Snippet, string[] ApplicableTypes, string? Since, string[] Flags);
    internal sealed record Vocabulary(string[] Functions, string[] Operators, string[] AggregationStages, string[] AtlasSearchOperators,
        string[] ExtendedJsonTypes, string[] Keywords, string[] DslFunctions, string[] DslRoots);
    internal sealed record Counts(int Symbols, Dictionary<string, int> ByKind);
    internal sealed record Snapshot(string Schema, Source Source, Symbol[] Symbols, Vocabulary Vocabulary, Counts Counts, string SnapshotSha256);
}
