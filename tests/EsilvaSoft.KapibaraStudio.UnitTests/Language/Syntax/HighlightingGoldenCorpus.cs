using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Syntax;

/// <summary>
/// Deterministic highlighting corpus. The golden file was captured from the highlighter as it was before the
/// MongoLexer extraction (checkout b082d4a); every case must keep producing the same tokens, brackets, line starts
/// and incremental reuse counts. No real data; large documents use a fixed-seed generator. Texts use explicit
/// escapes so the corpus does not depend on the line endings of this source file.
/// </summary>
internal static class HighlightingGoldenCorpus
{
    public const int FullListingTokenLimit = 600;
    public static SyntaxContext Names { get; } = new([
        new("Production", "Connect", "Projects", "AccountId_CreatedOn"),
        new("Other", "Logs", "Events")], "Production", "Connect", "Projects");

    public sealed record Step(string Text, SyntaxLanguage Language, SyntaxContext Context, bool UsePrevious = true);
    public sealed record Case(string Id, IReadOnlyList<Step> Steps);

    private static readonly Lazy<IReadOnlyList<Case>> LazyCases = new(Build);
    public static IReadOnlyList<Case> Cases => LazyCases.Value;

    /// <summary>Canonical rendering of a case. Small results list every token; large ones keep counts and a SHA-256 of the full dump.</summary>
    public static string Render(ISyntaxHighlightingService service, Case @case)
    {
        var builder = new StringBuilder();
        builder.Append("== ").Append(@case.Id).Append('\n');
        SyntaxSnapshot? previous = null;
        for (var index = 0; index < @case.Steps.Count; index++)
        {
            var step = @case.Steps[index];
            var snapshot = service.Highlight(step.Text, step.Language, step.Context, step.UsePrevious ? previous : null);
            var dump = Dump(snapshot);
            builder.Append(CultureInfo.InvariantCulture,
                $"step {index} language={step.Language} context={ContextName(step.Context)} chars={step.Text.Length} text={Hash(step.Text)} tokens={snapshot.Tokens.Count} brackets={snapshot.Brackets.Count} lines={snapshot.LineStarts.Count} tokenized={snapshot.TokenizedLines} dump={Hash(dump)}\n");
            if (snapshot.Tokens.Count <= FullListingTokenLimit) builder.Append(dump);
            previous = snapshot;
        }
        return builder.ToString();
    }

    private static string Dump(SyntaxSnapshot snapshot)
    {
        var builder = new StringBuilder();
        foreach (var token in snapshot.Tokens) builder.Append(CultureInfo.InvariantCulture, $"t {token.Start} {token.Length} {token.Type}\n");
        foreach (var pair in snapshot.Brackets.OrderBy(pair => pair.Key)) builder.Append(CultureInfo.InvariantCulture, $"b {pair.Key} {pair.Value}\n");
        builder.Append('l');
        foreach (var start in snapshot.LineStarts) builder.Append(' ').Append(start.ToString(CultureInfo.InvariantCulture));
        return builder.Append('\n').ToString();
    }

    private static string ContextName(SyntaxContext context) =>
        ReferenceEquals(context, Names) ? "names" : ReferenceEquals(context, SyntaxContext.Empty) ? "empty" : "custom";

    // Exact UTF-16 code units, including lone surrogates.
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(value.AsSpan())))[..32];

    private static string Lines(params string[] lines) => string.Join('\n', lines);

    private static List<Case> Build()
    {
        var cases = new List<Case>();
        void Single(string id, string text, SyntaxLanguage language, SyntaxContext? context = null) =>
            cases.Add(new(id, [new(text, language, context ?? SyntaxContext.Empty)]));
        // Small scripts are rendered in every language, with and without namespace metadata.
        void Matrix(string id, string text)
        {
            foreach (var language in Enum.GetValues<SyntaxLanguage>())
            {
                Single($"{id}/{language}/names", text, language, Names);
                Single($"{id}/{language}/empty", text, language);
            }
        }

        foreach (var (id, text) in SmallScripts) Matrix(id, text);
        foreach (var (id, text) in SmallScripts) Matrix(id + "-crlf", text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));

        Single("deep-nesting/aggregation", new string('[', 600) + "{$set:1}" + new string(']', 600), SyntaxLanguage.Aggregation);
        Single("deep-nesting/search", string.Concat(Enumerable.Repeat("{$search:", 520)) + "{equals:1}" + new string('}', 521), SyntaxLanguage.MongoScript);
        Single("long-line/string", "{\"payload\":\"" + new string('x', 100_000) + "END\"}", SyntaxLanguage.Json);
        Single("long-line/tokens", "[" + string.Join(",", Enumerable.Range(0, 20_000).Select(i => i.ToString(CultureInfo.InvariantCulture))) + "]", SyntaxLanguage.Json);
        Single("long-line/unterminated", "db.Projects.find({ Name: \"" + new string('y', 70_000), SyntaxLanguage.MongoScript, Names);
        Single("json/lines-4000", string.Join('\n', Enumerable.Repeat("{\"Name\":\"Project\",\"Version\":10,\"Active\":true},", 4000)), SyntaxLanguage.Json);
        Single("workload/script-64k", WorkloadScript(64 * 1024), SyntaxLanguage.MongoScript, Names);

        foreach (var (size, seed) in new[] { (16 * 1024, 11), (64 * 1024, 23), (256 * 1024, 37), (1024 * 1024, 41) })
        {
            var text = RandomScript(size, seed);
            Single($"random-script/{size}/seed-{seed}/names", text, SyntaxLanguage.MongoScript, Names);
            if (size <= 64 * 1024)
                foreach (var language in new[] { SyntaxLanguage.Json, SyntaxLanguage.Aggregation, SyntaxLanguage.AtlasSearch })
                    Single($"random-script/{size}/seed-{seed}/{language}", text, language);
        }
        foreach (var seed in new[] { 3, 5, 7 })
        {
            var text = Noise(32 * 1024, seed);
            Single($"noise/seed-{seed}/script", text, SyntaxLanguage.MongoScript, Names);
            Single($"noise/seed-{seed}/json", text, SyntaxLanguage.Json);
        }

        cases.Add(JsonSession());
        cases.Add(ScriptSession());
        cases.Add(LanguageAndContextSession());
        cases.Add(UncachedLinesSession());
        return cases;
    }

    private static readonly (string Id, string Text)[] SmallScripts =
    [
        ("console-find", "db.Projects.find({\"Active\":true}).sort({CreatedOn:-1}).limit(10);"),
        ("json-extended", "{\"Name\":\"Project\", \"n\":[10,10.5,-100,1.2e-10], \"Active\":true, \"gone\":false, \"empty\":null, \"id\":{\"$oid\":\"64d1\"}, \"date\":ISODate(\"2026-09-12\"), \"count\":NumberLong(9223372036854775807), \"dec\":{\"$numberDecimal\":\"1.50\"}}"),
        ("mongosh-script", Lines(
            "// Consulta MongoDB \u00B7 tipos BSON e pipeline",
            "const id = ObjectId(\"64b000000000000000000001\");",
            "async function run(limit) {",
            "  const rows = await db.Projects.find({ _id: id, count: { $gte: -10, $lt: 1e3 } }).limit(limit).toArray();",
            "  return rows.map(r => r.Name);",
            "}",
            "let ratio = total / 2 / count;",
            "const re = /^project[\\/]\\d+$/gi, other = (a) / 2;",
            "if (!/x/.test(name)) { print(`nome ${name}`); }",
            "x => /y/;")),
        ("aggregation", Lines(
            "db.Projects.aggregate([",
            "  { $match: { 'System.Id': '122', Active: true } },",
            "  { $group: { _id: '$Account.Id', count: { $sum: 1 } } },",
            "  { $set: { Name: 'New name' } }, { $unset: \"Legacy\" },",
            "  { $lookup: { from: \"Events\", let: { id: \"$_id\" }, pipeline: [ { $match: { $expr: { $eq: [\"$$id\", \"$ref\"] } } } ], as: \"events\" } },",
            "  { $facet: { total: [ { $count: \"n\" } ], page: [ { $skip: 0 }, { $limit: 20 } ] } }",
            "]);",
            "db.Projects.updateOne({}, { $set: { Name: 'x' } });")),
        ("aggregation-array", "[{$match:{Active:true}},{$set:{Name:'x'}},{$unset:['a','b']},{$project:{_id:0,Name:1}}]"),
        ("atlas-search", Lines(
            "db.Projects.aggregate([{ $search: { index: 'default', compound: { must: [ { text: { query: 'mongo', path: 'Name' } } ], filter: [ { equals: { path: 'System.Id', value: '122' } } ] } } },",
            "  { $searchMeta: { facet: { operator: { exists: { path: 'Name' } } } } }]);",
            "{text:'normal field',filter:[],equals:1}")),
        ("dsl-namespaces", Lines(
            "ConnectionPool.Production.Connect.Projects.find({});",
            "ConnectionPull.Production.Connect.Projects.find({});",
            "getConnection('Production').getDatabase('Connect').getCollection('Projects').find({});",
            "getConnection(\"Other\").GetDatabase(\"Logs\").GetCollection(\"Events\").find({});",
            "db.getSiblingDB('Connect').getCollection(\"Projects\").dropIndex('AccountId_CreatedOn');",
            "db.Projects.find({}).hint(\"AccountId_CreatedOn\"); db.Projects.createIndex({ a: 1 }, { name: 'AccountId_CreatedOn' });",
            "db.getDB(\"Logs\").Events; Production.Connect; db.Unknown.find();")),
        ("unterminated-strings", "db.Projects.find({ Name: \"abc\ndef\", Other: 'open\n}) // still open'\nconst t = `line 1\nline 2 ${x}`;\nconst e = \"barra no fim\\\ncontinua\"; const k = \"esc \\\" aspas\";\n\"aberta no fim"),
        ("comments", "/* ObjectId( */\n// db.find() /* nao abre\nconst x = 'find'; /* multi\nlinha { [ ( \n*/ db.Projects.find({}) /* a */ /* b\n\n   \n*/\n/*/ ainda comentario */ x;\n/* sem fim"),
        ("regex-division", "a = b / c / d;\nconst r = /=\\s*/g;\nif (/x/.test(y)) {}\n[/a/, /b[/]c/]\n{k: /v/i}\nreturn /r/m;\ny = (-1) - 1 -1 a-1 1e-5 0x1F 1_000 .5 -.5;\nz = x\n/c/g.exec(d)\n!/x/.test(s); s => /y/"),
        ("unicode", "db.Clientes.find({ A\u00E7\u00E3o: \"S\u00E3o Paulo\", \"\u540D\u524D\": '\u30C6\u30B9\u30C8', emoji: \"\uD83D\uDE00\", e: \uD83D\uDE00x, \u0663: 1, \u00A0nbsp:\u00A0true, zw\u200Bj: null, sep\u2028line: 2, \uD800lone: 1 })\n// \uD83D\uDE00 coment\u00E1rio\n`\uD83D\uDE00 ${'\u540D\u524D'}`"),
        ("brackets", "find({x:'[', r:/[()]/, y:[1]}) // {\n]\n)))\n{[}]\n({[\n"),
        ("mixed-crlf", "db.Projects.find({\r\n  \"Name\": \"a\r\nb\",\r\n  /* c\r\n */ x: 1\r\n})\r\n"),
    ];

    private static string WorkloadScript(int characters)
    {
        var builder = new StringBuilder(characters + 128);
        for (var index = 0; builder.Length < characters; index++)
            builder.Append(CultureInfo.InvariantCulture,
                $"db.clientes.find({{ \"Cliente.Id\": UUID(\"00000000-0000-0000-0000-{index:D12}\"), Status: {{ $in: [\"ativo\", \"novo\"] }} }}).sort({{ CriadoEm: -1 }}).limit(20);\n");
        return builder.Append("db.clientes.find({\n    Cliente\n})").ToString();
    }

    private static readonly string[] Fragments =
    [
        "db.Projects.find({ Active: true, count: { $gte: 10 } })", "db.getSiblingDB('Connect').Projects.aggregate([{ $match: { 'System.Id': '122' } }, { $group: { _id: '$Account.Id', total: { $sum: 1 } } }])",
        "getConnection(\"Production\").getDatabase(\"Connect\").getCollection(\"Projects\").find({})", "ConnectionPool.Production.Connect.Projects.countDocuments({})",
        "const r = /^a[\\/]b/gi;", "let x = a / b / 2;", "return /x/.test(y);", "x => /y/", "/* bloco", "fim */", "// coment\u00E1rio {", "`template ${x}", "linha`",
        "\"aberta", "'aberta", "\"esc\\\\\"", "\"barra no fim\\", "{ $search: { compound: { filter: [{ equals: { path: 'a', value: 1 } }] } } }", "{ $set: { Name: 'x' } }",
        "[", "]", "{", "}", "(", ")", ",", ";", ":", "-1", "1.5e-3", "0x1F", "1_000", "ObjectId(\"64b000000000000000000001\")", "ISODate('2026-09-12')",
        "NumberDecimal(\"1.5\")", "{ \"$oid\": \"64d1\" }", "S\u00E3o Paulo", "\u540D\u524D", "\uD83D\uDE00", "\u00A0", "\t", " ", "\r\n", "\n", "\u2028", "db.Projects.dropIndex('AccountId_CreatedOn')",
        "hint(\"AccountId_CreatedOn\")", "{ name: 'AccountId_CreatedOn' }", "async function f() { await db.Projects.findOne({}); }", "if (!/x/.test(s)) { }", "a = b\n/c/g.exec(d)",
        "$searchMeta", "aggregate", "db", ".", "Projects", "Connect", "Production", "\"Name\": ", "'k': ", "true", "null", "undefined", "=", "!", "*", "/"
    ];
    private static readonly string[] Separators = [" ", "", "\n", "\r\n", "\t", " ", "\n"];

    private static string RandomScript(int characters, int seed)
    {
        var random = new SplitMix64((ulong)seed);
        var builder = new StringBuilder(characters + 256);
        while (builder.Length < characters)
            builder.Append(Fragments[random.Next(Fragments.Length)]).Append(Separators[random.Next(Separators.Length)]);
        return builder.ToString(0, characters);
    }

    private static string Noise(int characters, int seed)
    {
        const string alphabet = "{}[]()\"'`/\\*:,;.$-+=!<>0123456789eExab_ \n\r\t\u00E7\u540D";
        var random = new SplitMix64((ulong)seed);
        var buffer = new char[characters];
        for (var index = 0; index < buffer.Length; index++) buffer[index] = alphabet[random.Next(alphabet.Length)];
        return new string(buffer);
    }

    private static Case JsonSession()
    {
        var text = string.Join('\n', Enumerable.Repeat("{\"Name\":\"Project\"},", 5000));
        var changed = "{\"Name\":\"Updated\"},\n" + text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..];
        var middle = text.IndexOf('\n', text.Length / 2);
        var inserted = text[..middle] + "\n{\"Inserted\": [1, 2, 3]}," + text[middle..];
        var deleted = inserted[..inserted.LastIndexOf('\n')];
        return new("session/json", [
            new(text, SyntaxLanguage.Json, SyntaxContext.Empty),
            new(changed, SyntaxLanguage.Json, SyntaxContext.Empty),
            new("/*\n" + text + "\n*/", SyntaxLanguage.Json, SyntaxContext.Empty),
            new(changed, SyntaxLanguage.Json, SyntaxContext.Empty),
            new(inserted, SyntaxLanguage.Json, SyntaxContext.Empty),
            new(deleted, SyntaxLanguage.Json, SyntaxContext.Empty),
            new(deleted, SyntaxLanguage.Json, SyntaxContext.Empty)]);
    }

    private static Case ScriptSession()
    {
        var lines = RandomScript(24 * 1024, 101).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        string Join() => string.Join('\n', lines);
        var steps = new List<Step> { new(Join(), SyntaxLanguage.MongoScript, Names) };
        void Edit(Action change) { change(); steps.Add(new(Join(), SyntaxLanguage.MongoScript, Names)); }
        Edit(() => lines[10] = "/* abre comentario " + lines[10]);
        Edit(() => lines[50] += " */ db.Projects.find({})");
        Edit(() => lines[20] = "\"" + lines[20]);
        Edit(() => lines[20] = lines[20][1..]);
        Edit(() => lines.Insert(0, "const alias = db.Projects;"));
        Edit(() => lines[30] += "\r");
        Edit(() => lines.RemoveAt(lines.Count / 2));
        Edit(() => lines[^1] += " `template aberto");
        Edit(() => lines.Insert(5, "db.Projects.aggregate([{ $set: { a: 1 } }]); { $search: { equals: {} } }"));
        var same = steps[^1].Text;
        steps.Add(new(same, SyntaxLanguage.MongoScript, Names));
        return new("session/script", steps);
    }

    private static Case LanguageAndContextSession()
    {
        const string text = "db.Projects.aggregate([{ $set: { Name: 'x' } }]);\n{$set:{Name:'x'}}\n{equals:{path:'a'}}\ngetConnection('Production').getDatabase('Connect').getCollection('Projects')";
        var copy = new SyntaxContext(Names.Names.ToList(), Names.Connection, Names.Database, Names.Collection);
        return new("session/language-context", [
            new(text, SyntaxLanguage.MongoScript, Names),
            new(text, SyntaxLanguage.Aggregation, Names),
            new(text, SyntaxLanguage.AtlasSearch, Names),
            new(text, SyntaxLanguage.AtlasSearch, SyntaxContext.Empty),
            new(text, SyntaxLanguage.AtlasSearch, copy),
            new(text + "\n", SyntaxLanguage.AtlasSearch, copy),
            new(text + "\n", SyntaxLanguage.AtlasSearch, copy, UsePrevious: false)]);
    }

    private static Case UncachedLinesSession()
    {
        var text = string.Concat(Enumerable.Repeat("x\n", SyntaxHighlightingOptions.MaximumCachedLines + 1));
        return new("session/uncached-lines", [
            new(text, SyntaxLanguage.MongoScript, SyntaxContext.Empty),
            new("y" + text[1..], SyntaxLanguage.MongoScript, SyntaxContext.Empty)]);
    }

    /// <summary>Stable across runtimes, unlike System.Random.</summary>
    internal sealed class SplitMix64(ulong seed)
    {
        private ulong _state = seed;
        public int Next(int maximum)
        {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (int)((z ^ (z >> 31)) % (ulong)maximum);
        }
    }
}
