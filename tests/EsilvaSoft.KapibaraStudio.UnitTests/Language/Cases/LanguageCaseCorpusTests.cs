using System.Text.Json;
using System.Text.Json.Nodes;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests.Language.Cases;

[TestFixture]
public sealed class LanguageCaseCorpusTests
{
    private static readonly IReadOnlyList<LanguageCaseFixture> Cases = LanguageCaseFixture.LoadAll();
    private static readonly ConnectionProfile FixtureProfile = new(Guid.Empty, "fixture", "mongodb://fixture");

    public static IEnumerable<TestCaseData> AllCases => Cases.Select(@case => new TestCaseData(@case).SetName("CorpusParse_" + @case.Id));
    public static IEnumerable<TestCaseData> RoleCases => Cases.Where(@case => @case.HasExpectation("role"))
        .Select(@case => new TestCaseData(@case).SetName("CorpusRole_" + @case.Id));
    public static IEnumerable<TestCaseData> ShapeCases => Cases.Where(@case => @case.HasExpectation("shape"))
        .Select(@case => new TestCaseData(@case).SetName("CorpusShape_" + @case.Id));
    public static IEnumerable<TestCaseData> KindCases => Cases.Where(@case => @case.HasExpectation("kinds"))
        .Select(@case => new TestCaseData(@case).SetName("CorpusKinds_" + @case.Id));
    public static IEnumerable<TestCaseData> QuoteCases => Cases.Where(@case => @case.HasExpectation("quote"))
        .Select(@case => new TestCaseData(@case).SetName("CorpusQuote_" + @case.Id));
    public static IEnumerable<TestCaseData> ReplaceCases => Cases.Where(@case => @case.HasExpectation("replace"))
        .Select(@case => new TestCaseData(@case).SetName("CorpusReplace_" + @case.Id));
    public static IEnumerable<TestCaseData> ValueTypeCases => Cases.Where(@case => @case.HasExpectation("valueTypes"))
        .Select(@case => new TestCaseData(@case).SetName("CorpusValueTypes_" + @case.Id));
    public static IEnumerable<TestCaseData> InsertCases => Cases.Where(@case => @case.HasExpectation("insert") && SupportsLanguageEdit(@case))
        .Select(@case => new TestCaseData(@case).SetName("CorpusInsert_" + @case.Id));
    public static IEnumerable<TestCaseData> AppliedCases => Cases.Where(@case => @case.HasExpectation("applied") && SupportsLanguageEdit(@case))
        .Select(@case => new TestCaseData(@case).SetName("CorpusApplied_" + @case.Id));
    public static IEnumerable<TestCaseData> LanguageRankingCases => Cases
        .Where(@case => @case.HasExpectation("best") && IsLocallyRankable(@case))
        .Select(@case => new TestCaseData(@case).SetName("CorpusRank_" + @case.Id));

    [TestCaseSource(nameof(AllCases))]
    public void EveryFixtureFollowsThePublishedFileContract(LanguageCaseFixture @case)
    {
        // A body containing only the cursor marker is a valid statement-start fixture; after parsing its text is empty.
        Assert.That(@case.Text, Is.Not.Null);
        Assert.That(@case.Caret, Is.InRange(0, @case.Text.Length));
        Assert.That(@case.Id, Is.EqualTo(Path.GetFileNameWithoutExtension(@case.SourcePath)));
    }

    [TestCaseSource(nameof(RoleCases))]
    public void ContextEngineMatchesTheIndependentCursorRoleExpectation(LanguageCaseFixture @case)
    {
        var analysis = CompletionContextEngine.Analyze(new ContextRequest(new StringTextSnapshot(@case.Text), @case.Caret,
            Dialect(@case.Mode), TabScope(@case.Target)));

        Assert.That(analysis.Role.ToString(), Is.EqualTo(@case.Expectation("role")), @case.SourcePath);
    }

    [TestCaseSource(nameof(ShapeCases))]
    public void ShapeWalkerMatchesPublishedShapeExpectation(LanguageCaseFixture @case)
    {
        var analysis = CompletionContextEngine.Analyze(new ContextRequest(new StringTextSnapshot(@case.Text), @case.Caret,
            Dialect(@case.Mode), TabScope(@case.Target)));

        Assert.That(analysis.Context.ShapeId ?? "", Is.EqualTo(@case.Expectation("shape")), @case.SourcePath);
    }

    [TestCaseSource(nameof(KindCases))]
    public void ContextEngineMatchesPublishedExpectedKinds(LanguageCaseFixture @case)
    {
        var analysis = CompletionContextEngine.Analyze(new ContextRequest(new StringTextSnapshot(@case.Text), @case.Caret,
            Dialect(@case.Mode), TabScope(@case.Target)));

        var expected = ParseKinds(@case.Expectation("kinds"));
        Assert.That(analysis.Context.ExpectedKinds & expected, Is.EqualTo(expected), @case.SourcePath);
        if (@case.HasExpectation("kinds.not"))
        {
            var forbidden = ParseKinds(@case.Expectation("kinds.not"));
            Assert.That(analysis.Context.ExpectedKinds & forbidden, Is.EqualTo(SymbolKinds.None), @case.SourcePath);
        }
    }

    [TestCaseSource(nameof(QuoteCases))]
    public void ContextEngineMatchesPublishedQuoteExpectation(LanguageCaseFixture @case)
    {
        var analysis = Analyze(@case);
        var expected = @case.Expectation("quote") switch
        {
            "Double" => '"',
            "Single" => '\'',
            "None" or "-" => (char?)null,
            var value => throw new InvalidDataException($"Quote desconhecido: {value}.")
        };

        Assert.That(analysis.Quote, Is.EqualTo(expected), @case.SourcePath);
    }

    [TestCaseSource(nameof(ValueTypeCases))]
    public void ContextEngineMatchesPublishedFieldValueTypes(LanguageCaseFixture @case)
    {
        var analysis = CompletionContextEngine.Analyze(new ContextRequest(new StringTextSnapshot(@case.Text), @case.Caret,
            Dialect(@case.Mode), TabScope(@case.Target)) { InputSchema = LoadSchema(@case) });

        var expected = @case.Expectation("valueTypes") is "-" or ""
            ? new HashSet<string>(StringComparer.Ordinal)
            : @case.Expectation("valueTypes").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.Ordinal);
        Assert.That(analysis.Context.ValueTypes, Is.EquivalentTo(expected), @case.SourcePath);
    }

    [TestCaseSource(nameof(LanguageRankingCases))]
    public async Task CompletionServiceMatchesPublishedLanguageRankingExpectations(LanguageCaseFixture @case)
    {
        var schemas = LoadSchemas(@case);
        var schema = schemas.Input;
        var analysis = CompletionContextEngine.Analyze(new ContextRequest(new StringTextSnapshot(@case.Text), @case.Caret,
            Dialect(@case.Mode), TabScope(@case.Target)) { InputSchema = schema, ForeignSchemas = schemas.Foreign });
        var localSchemas = analysis.Context.LocalSchemas.Count == 0 && schema.NodeCount > 0
            ? [schema]
            : analysis.Context.LocalSchemas;
        var context = analysis.Context with
        {
            LocalSchemas = localSchemas,
            RestrictFieldsToLocalSchemas = localSchemas.Count > 0,
            MaximumCandidates = 1000,
            MaximumItems = 1000
        };
        using var metadata = new MetadataCache(UnavailableMetadataSource.Instance);
        var service = NewCompletionService(metadata);
        var result = await service.CompleteAsync(context);
        var best = @case.Expectation("best");
        Assert.That(result.Items.Any(item => Matches(item, best) && item == result.Items[0]), Is.True,
            $"Top-1 esperado {best}; recebido {string.Join(", ", result.Items.Take(5).Select(Describe))}; total={result.Items.Count}; campos={string.Join(", ", result.Items.Where(item => item.CatalogKind == SymbolKind.Field).Select(Describe))}; role={analysis.Role}, kinds={analysis.Context.ExpectedKinds}, shape={analysis.Context.ShapeId}, prefix={analysis.Context.Prefix}, parent={analysis.Context.ParentPath}, local={localSchemas.Count}/{schema.NodeCount}, restrict={context.RestrictFieldsToLocalSchemas}, caret={@case.Caret}, text={@case.Text}. Caso: {@case.SourcePath}");

        foreach (var acceptable in @case.Expectation("acceptable").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.That(result.Items.Take(5).Any(item => Matches(item, acceptable)), Is.True,
                $"Aceitável {acceptable} ausente no top-5; recebido {string.Join(", ", result.Items.Take(5).Select(Describe))}; kinds={analysis.Context.ExpectedKinds}, shape={analysis.Context.ShapeId}. Caso: {@case.SourcePath}");
        if (@case.HasExpectation("invalidTop5"))
            foreach (var invalid in @case.Expectation("invalidTop5").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                Assert.That(result.Items.Take(5).Any(item => Matches(item, invalid)), Is.False,
                    $"Item inválido {invalid} apareceu no top-5. Caso: {@case.SourcePath}");
    }

    [Test]
    public async Task PublishedRankingCorpusHasRecordedPerfectMrrTop1AndTop5()
    {
        var ranked = new List<(LanguageCaseFixture Case, int Position)>();
        foreach (var @case in Cases.Where(@case => @case.HasExpectation("best") && IsLocallyRankable(@case)))
        {
            var result = await Complete(@case);
            ranked.Add((@case, result.Items.ToList().FindIndex(item => Matches(item, @case.Expectation("best"))) + 1));
        }

        Assert.That(ranked, Is.Not.Empty);
        var mrr = ranked.Average(entry => entry.Position == 0 ? 0d : 1d / entry.Position);
        var top1 = ranked.Count(entry => entry.Position == 1);
        var top5 = ranked.Count(entry => entry.Position is > 0 and <= 5);
        TestContext.WriteLine($"Ranking corpus: cases={ranked.Count}; MRR={mrr:0.000}; top-1={top1}/{ranked.Count}; top-5={top5}/{ranked.Count}.");
        Assert.Multiple(() =>
        {
            Assert.That(mrr, Is.EqualTo(1d));
            Assert.That(top1, Is.EqualTo(ranked.Count));
            Assert.That(top5, Is.EqualTo(ranked.Count));
        });
    }

    [TestCaseSource(nameof(InsertCases))]
    public async Task CompletionServiceMatchesPublishedInsertExpectations(LanguageCaseFixture @case)
    {
        var result = await Complete(@case);
        foreach (var declaration in @case.ExpectationValues("insert"))
        {
            var (key, kind, expectedText) = ParseInsert(@case, declaration);
            var item = result.Items.FirstOrDefault(candidate => Matches(candidate, key) && candidate.Edit.IsSnippet == (kind == "snippet"));
            Assert.That(item, Is.Not.Null, $"Item de inserção não encontrado: {key}. Recebidos: {string.Join(", ", result.Items.Take(12).Select(Describe))}. Caso: {@case.SourcePath}");
            var equivalentOpenQuoteEdit = expectedText.Length > 1 && expectedText[0] is '\'' or '"'
                && item!.Edit.NewText == expectedText[1..];
            Assert.That(item!.Edit.NewText == expectedText || equivalentOpenQuoteEdit, Is.True,
                $"Texto de inserção divergente. Caso: {@case.SourcePath}");
            Assert.That(item.Edit.IsSnippet, Is.EqualTo(kind == "snippet"), $"Tipo de edição divergente. Caso: {@case.SourcePath}");
        }
    }

    [TestCaseSource(nameof(AppliedCases))]
    public async Task CompletionServiceAppliesPublishedEditsAndSnippetCursor(LanguageCaseFixture @case)
    {
        var result = await Complete(@case);
        foreach (var declaration in @case.ExpectationValues("applied"))
        {
            var separator = declaration.IndexOf(" => ", StringComparison.Ordinal);
            Assert.That(separator, Is.GreaterThan(0), $"expect.applied inválido: {declaration}");
            var key = declaration[..separator].Trim();
            var expectedLine = Unquote(declaration[(separator + 4)..], @case, "applied");
            var item = result.Items.FirstOrDefault(candidate => Matches(candidate, key));
            Assert.That(item, Is.Not.Null, $"Item aplicado não encontrado: {key}. Caso: {@case.SourcePath}");

            var inserted = item!.Edit.IsSnippet ? SnippetTemplate.Parse(item.Edit.NewText).Expand() : null;
            var replacement = inserted?.Text ?? item.Edit.NewText;
            var cursor = item.Edit.ReplaceRange.Start + replacement.Length;
            if (inserted is { Placeholders.Count: > 0 })
            {
                var placeholder = inserted.Placeholders.OrderBy(value => value.Index).ThenBy(value => value.Span.Start).First();
                cursor = item.Edit.ReplaceRange.Start + placeholder.Span.Start;
            }

            var applied = @case.Text[..item.Edit.ReplaceRange.Start] + replacement + @case.Text[item.Edit.ReplaceRange.End..];
            var lineStart = applied.LastIndexOf('\n', Math.Max(0, cursor - 1)) + 1;
            var lineEnd = applied.IndexOf('\n', cursor);
            if (lineEnd < 0) lineEnd = applied.Length;
            var actualLine = expectedLine.Contains('|', StringComparison.Ordinal)
                ? applied[lineStart..cursor] + "|" + applied[cursor..lineEnd]
                : applied[lineStart..lineEnd];
            Assert.That(actualLine, Is.EqualTo(expectedLine), $"Aplicação divergente. Caso: {@case.SourcePath}");
        }
    }

    [TestCaseSource(nameof(ReplaceCases))]
    public void ContextEngineMatchesPublishedReplaceExpectation(LanguageCaseFixture @case)
    {
        var analysis = Analyze(@case);
        var expected = @case.Expectation("replace");
        expected = expected.StartsWith('«') && expected.EndsWith('»') ? expected[1..^1] : expected;
        var actual = analysis.Context.ReplaceSpan.Length == 0
            ? string.Empty
            : @case.Text.Substring(analysis.Context.ReplaceSpan.Start, analysis.Context.ReplaceSpan.Length);

        Assert.That(actual, Is.EqualTo(expected), @case.SourcePath);
    }

    private static EditorDialects Dialect(string mode) => mode switch
    {
        "Console" => EditorDialects.Console,
        "Script" => EditorDialects.MongoshScript,
        "Agregação" => EditorDialects.AggregationJson,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Modo de fixture desconhecido.")
    };

    private static CompletionContextAnalysis Analyze(LanguageCaseFixture @case) => CompletionContextEngine.Analyze(
        new ContextRequest(new StringTextSnapshot(@case.Text), @case.Caret, Dialect(@case.Mode), TabScope(@case.Target)));

    private static CollectionSchema LoadSchema(LanguageCaseFixture @case) => LoadSchemas(@case).Input;

    private static (CollectionSchema Input, IReadOnlyDictionary<string, CollectionSchema> Foreign) LoadSchemas(LanguageCaseFixture @case)
    {
        var loaded = new List<(string Name, CollectionSchema Schema)>();
        foreach (var reference in @case.HeaderValues("schema").SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            var root = FindSchemaFile(@case.SourcePath, reference);
            var builder = new SchemaBuilder();
            using var document = JsonDocument.Parse(EmbeddedTestData.ReadText(root));
            foreach (var field in document.RootElement.GetProperty("fields").EnumerateArray())
            {
                var segments = field.TryGetProperty("segments", out var declaredSegments)
                    ? declaredSegments.EnumerateArray().Select(segment => segment.GetString()!).ToArray()
                    : field.GetProperty("path").GetString()!.Split('.');
                foreach (var type in FieldTypes(field.GetProperty("types")))
                    builder.AddDocuments([BuildDocument(segments, type, field.TryGetProperty("array", out var array) && array.GetBoolean())]);
            }
            loaded.Add((Path.GetFileNameWithoutExtension(root), builder.Build()));
        }

        var target = TabScope(@case.Target)?.Collection;
        var input = target is null
            ? new SchemaBuilder().Build()
            : loaded.FirstOrDefault(entry => string.Equals(entry.Name, target, StringComparison.OrdinalIgnoreCase)).Schema
                ?? new SchemaBuilder().Build();
        var foreign = loaded.Where(entry => !ReferenceEquals(entry.Schema, input))
            .ToDictionary(entry => entry.Name, entry => entry.Schema, StringComparer.OrdinalIgnoreCase);
        return (input, foreign);
    }

    private static string FindSchemaFile(string casePath, string reference)
    {
        var normalized = casePath.Replace('\\', '/');
        var directory = normalized[..normalized.LastIndexOf('/')];
        while (directory.Length > 0)
        {
            var candidate = directory + "/" + reference.Replace('\\', '/');
            if (EmbeddedTestData.Contains(candidate)) return candidate;
            var separator = directory.LastIndexOf('/');
            directory = separator < 0 ? string.Empty : directory[..separator];
        }
        throw new InvalidDataException($"Schema do corpus não encontrado: {reference}");
    }

    private static IEnumerable<string> FieldTypes(JsonElement types) => types.ValueKind switch
    {
        JsonValueKind.Array => types.EnumerateArray().Select(value => value.GetString()!),
        JsonValueKind.Object => types.EnumerateObject().Select(property => property.Name),
        _ => throw new InvalidDataException("types inválido no schema do corpus.")
    };

    private static string BuildDocument(string[] segments, string type, bool isArray)
    {
        var root = new JsonObject();
        var current = root;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            var child = new JsonObject();
            current[segments[index]] = child;
            current = child;
        }
        current[segments[^1]] = Value(type, isArray);
        return root.ToJsonString();
    }

    private static JsonNode? Value(string type, bool isArray) => isArray ? new JsonArray(Value(type, false)) : type switch
    {
        "string" => JsonValue.Create("valor")!,
        "int" => JsonNode.Parse("{\"$numberInt\":\"1\"}")!,
        "long" => JsonNode.Parse("{\"$numberLong\":\"1\"}")!,
        "double" => JsonNode.Parse("{\"$numberDouble\":\"1.5\"}")!,
        "decimal" => JsonNode.Parse("{\"$numberDecimal\":\"1.5\"}")!,
        "bool" => JsonValue.Create(true)!,
        "null" => null,
        "uuid" => JsonNode.Parse("{\"$binary\":{\"base64\":\"\",\"subType\":\"04\"}}")!,
        "objectId" => JsonNode.Parse("{\"$oid\":\"000000000000000000000000\"}")!,
        "date" => JsonNode.Parse("{\"$date\":\"2020-01-01T00:00:00Z\"}")!,
        "array" => new JsonArray(),
        "object" or "document" => new JsonObject(),
        _ => JsonValue.Create("valor")!
    };

    private static SymbolKinds ParseKinds(string value) => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Aggregate(SymbolKinds.None, (kinds, name) => kinds | Enum.Parse<SymbolKinds>(name, ignoreCase: false));

    private static bool IsLocallyRankable(LanguageCaseFixture @case)
    {
        var best = @case.Expectation("best");
        return !best.StartsWith("Collection:", StringComparison.Ordinal)
            && !best.StartsWith("Database:", StringComparison.Ordinal)
            && !best.StartsWith("Connection:", StringComparison.Ordinal)
            && !best.Contains('@', StringComparison.Ordinal);
    }

    private static async Task<CompletionList> Complete(LanguageCaseFixture @case)
    {
        var schemas = LoadSchemas(@case);
        var schema = schemas.Input;
        var analysis = CompletionContextEngine.Analyze(new ContextRequest(new StringTextSnapshot(@case.Text), @case.Caret,
            Dialect(@case.Mode), TabScope(@case.Target)) { InputSchema = schema, ForeignSchemas = schemas.Foreign });
        var localSchemas = analysis.Context.LocalSchemas.Count == 0 && schema.NodeCount > 0 ? [schema] : analysis.Context.LocalSchemas;
        var context = analysis.Context with
        {
            LocalSchemas = localSchemas,
            RestrictFieldsToLocalSchemas = localSchemas.Count > 0,
            MaximumCandidates = 1000,
            MaximumItems = 1000
        };
        using var metadata = new MetadataCache(UnavailableMetadataSource.Instance);
        var service = NewCompletionService(metadata);
        var result = await service.CompleteAsync(context);
        if (result.Items.Count == 0)
            throw new InvalidOperationException($"Corpus sem candidatos: role={analysis.Role}, expected={analysis.Context.ExpectedKinds}, shape={analysis.Context.ShapeId}, prefix={analysis.Context.Prefix}, parent={analysis.Context.ParentPath}, queryTarget={analysis.Target}, local={localSchemas.Count}/{schema.NodeCount}, restrict={context.RestrictFieldsToLocalSchemas}, caso={@case.SourcePath}");
        return result;
    }

    private static (string Key, string Kind, string Text) ParseInsert(LanguageCaseFixture @case, string declaration)
    {
        var separator = declaration.IndexOf(" => ", StringComparison.Ordinal);
        Assert.That(separator, Is.GreaterThan(0), $"expect.insert inválido: {declaration}");
        var key = declaration[..separator].Trim();
        var payload = declaration[(separator + 4)..].Trim();
        var space = payload.IndexOf(' ');
        Assert.That(space, Is.GreaterThan(0), $"expect.insert sem tipo: {declaration}");
        var kind = payload[..space];
        Assert.That(kind, Is.EqualTo("text").Or.EqualTo("snippet"), $"Tipo de inserção inválido: {declaration}");
        return (key, kind, Unquote(payload[(space + 1)..], @case, "insert"));
    }

    private static string Unquote(string value, LanguageCaseFixture @case, string key)
    {
        value = value.Trim();
        if (value.Length < 2 || value[0] != '«' || value[^1] != '»')
            throw new InvalidDataException($"expect.{key} sem delimitadores «»: {@case.SourcePath}");
        return value[1..^1];
    }

    private static bool Matches(CompletionItem item, string expectation)
    {
        var separator = expectation.IndexOf(':');
        if (separator < 0) return item.Label == expectation || item.SymbolId == expectation;
        var kind = expectation[..separator];
        var value = expectation[(separator + 1)..];
        if (kind == "Field")
        {
            if (value.Length >= 4 && value.StartsWith("[\"", StringComparison.Ordinal) && value.EndsWith("\"]", StringComparison.Ordinal))
                value = value[2..^2];
            var at = value.IndexOf('@');
            var path = at < 0 ? value : value[..at];
            var collection = at < 0 ? null : value[(at + 1)..];
            return item.CatalogKind == SymbolKind.Field && item.FieldPath == path
                && (collection is null || string.Equals(item.Scope?.Collection, collection, StringComparison.Ordinal));
        }
        if (kind == "Snippet") return item.CatalogKind == SymbolKind.Snippet && item.SymbolId.EndsWith(value, StringComparison.Ordinal);
        return Enum.TryParse<SymbolKind>(kind, out var expectedKind)
            && item.CatalogKind == expectedKind && item.Label == value;
    }

    private static string Describe(CompletionItem item) => item.CatalogKind is { } kind ? $"{kind}:{item.Label}" : item.Label;

    private static CompletionService NewCompletionService(MetadataCache metadata) => new(
        new KnowledgeCatalog([new LanguageCatalogSource(), new MetadataCatalogSource(metadata)]),
        profiles: new FixtureProfileResolver());

    private sealed class FixtureProfileResolver : ICompletionProfileResolver
    {
        public ConnectionProfile? Resolve(Guid profileId) => profileId == FixtureProfile.Id ? FixtureProfile : null;
    }

    private static bool SupportsLanguageEdit(LanguageCaseFixture @case) =>
        @case.ExpectationValues(@case.HasExpectation("insert") ? "insert" : "applied")
            .All(value => !value.StartsWith("Collection:", StringComparison.Ordinal)
                && !value.StartsWith("Database:", StringComparison.Ordinal)
                && !value.StartsWith("Connection:", StringComparison.Ordinal));

    private static CatalogScope? TabScope(string target)
    {
        if (target == "-") return null;
        var segments = target.Split('/');
        if (segments.Length is < 1 or > 3 || string.IsNullOrWhiteSpace(segments[0]))
            throw new InvalidDataException($"Target de fixture inválido: {target}.");
        return new CatalogScope(new ConnectionIdentity(Guid.Empty, null, "fixture"),
            segments.Length > 1 ? segments[1] : "", segments.Length > 2 ? segments[2] : "");
    }

}
