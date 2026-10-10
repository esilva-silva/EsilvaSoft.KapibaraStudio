using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Syntax;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Builds lab-only query examples from a seed and index, then checks them with the editor's real parser.</summary>
internal static class SyntheticSampleGenerator
{
    internal const int MaximumCount = 10_000;
    internal const int MaximumOutputBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] NumericQueryOperators = ["$gt", "$gte", "$lt", "$lte", "$eq", "$ne"];
    private static readonly string[] CandidateStages = ["$project", "$sort", "$limit", "$skip", "$addFields", "$set"];

    public static IReadOnlyList<string> Generate(int seed, int count, int? onlyIndex, CancellationToken cancellationToken = default)
    {
        if (count is < 1 or > MaximumCount) throw new ArgumentOutOfRangeException(nameof(count), $"count deve estar entre 1 e {MaximumCount}.");
        if (onlyIndex is < 0) throw new ArgumentOutOfRangeException(nameof(onlyIndex), "index não pode ser negativo.");
        if (onlyIndex.HasValue && count != 1) throw new ArgumentException("Use --count 1 junto com --index.");

        var firstIndex = onlyIndex ?? 0;
        if ((long)firstIndex + count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(onlyIndex), "intervalo de índices excede o limite.");
        var results = new List<string>(count);
        var totalBytes = 0L;
        for (var offset = 0; offset < count; offset++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = firstIndex + offset;
            var sample = Build(seed, index, cancellationToken);
            var line = JsonSerializer.Serialize(sample, JsonOptions);
            totalBytes = checked(totalBytes + Encoding.UTF8.GetByteCount(line) + 1);
            if (totalBytes > MaximumOutputBytes) throw new InvalidDataException("A saída excede o limite de 64 MiB.");
            results.Add(line);
        }
        return results;
    }

    private static SyntheticSample Build(int rootSeed, int index, CancellationToken cancellationToken)
    {
        var random = new StableRandom(unchecked((uint)rootSeed * 397u + (uint)index));
        var collection = $"lab_collection_{index % 1000:D3}";
        var fields = Enumerable.Range(0, 8).Select(field => new SyntheticField(
            $"field_{index % 1000:D3}_{field:D2}", field % 2 == 0 ? "string" : "number")).ToArray();
        var selectedField = fields[random.Next(fields.Length)];
        var field = selectedField.Name;
        var form = index % 2 == 0 ? "find" : "aggregate";
        var operatorName = "";
        string statement;
        int caret;
        IReadOnlyList<ParsedStage> parsedStages;

        if (form == "find")
        {
            var availableOperators = NumericQueryOperators.Where(MongoSyntaxVocabulary.Operators.Contains).ToArray();
            if (availableOperators.Length == 0) throw new InvalidDataException("O catálogo do editor não contém operadores de comparação compatíveis.");
            operatorName = availableOperators[random.Next(availableOperators.Length)];
            RequireCatalog(MongoSyntaxVocabulary.Operators, operatorName);
            var comparisonValue = selectedField.Type == "number" ? "10" : "\"synthetic\"";
            statement = $"db.{collection}.find({{ {field}: {{ {operatorName}: {comparisonValue} }} }});";
            caret = statement.IndexOf(field, statement.IndexOf("find(", StringComparison.Ordinal), StringComparison.Ordinal);
            parsedStages = [];
        }
        else
        {
            RequireCatalog(MongoSyntaxVocabulary.AggregationStages, "$match");
            var availableStages = CandidateStages.Where(MongoSyntaxVocabulary.AggregationStages.Contains).ToArray();
            if (availableStages.Length == 0) throw new InvalidDataException("O catálogo do editor não contém estágios compatíveis com as amostras.");
            var stage = availableStages[random.Next(availableStages.Length)];
            operatorName = "$gte";
            var matchValue = selectedField.Type == "number" ? "10" : "\"synthetic\"";
            var stageBody = stage switch
            {
                "$project" => $"{{ {field}: 1 }}",
                "$sort" => $"{{ {field}: {(random.Next(2) == 0 ? 1 : -1)} }}",
                "$limit" => (1 + random.Next(100)).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "$skip" => random.Next(100).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "$addFields" or "$set" => $"{{ {field}_derived: {{ $literal: \"synthetic\" }} }}",
                _ => throw new InvalidDataException("Estágio sintético sem construção definida.")
            };
            statement = $"db.{collection}.aggregate([{{ $match: {{ {field}: {{ $gte: {matchValue} }} }} }}, {{ {stage}: {stageBody} }}]);";
            RequireCatalog(MongoSyntaxVocabulary.Operators, "$gte");
            RequireCatalog(MongoSyntaxVocabulary.AggregationStages, stage);
            // Place the cursor on the later stage key; the preceding stage is complete and parseable.
            caret = statement.IndexOf(stage, StringComparison.Ordinal);
            var tokens = new List<MongoToken>();
            MongoLexer.Tokenize(statement.AsSpan(), tokens, mode: MongoLexerMode.Script, cancellationToken: cancellationToken);
            var pipelineRoot = PipelineStageReader.FindPipelineRoot(tokens, statement, caret, EditorDialects.MongoshScript);
            if (pipelineRoot < 0) throw new InvalidDataException("O editor não localizou o pipeline sintético.");
            parsedStages = PipelineStageReader.ReadStagesBefore(tokens, statement, pipelineRoot, caret,
                    cancellationToken: cancellationToken)
                .Select(stage => new ParsedStage(stage.Name, stage.Properties.Select(property =>
                    new ParsedStageProperty(property.Name, property.Value.Kind.ToString())).ToArray())).ToArray();
        }

        if (caret < 0 || caret > statement.Length) throw new InvalidDataException("Cursor sintético fora do statement.");
        var validation = new EsilvaSoft.KapibaraStudio.Infrastructure.MongoCodeValidator()
            .ValidateAsync(statement, aggregation: false, cancellationToken).GetAwaiter().GetResult();
        if (!validation.IsValid) throw new InvalidDataException("O parser local recusou uma amostra gerada.");

        var context = CompletionContextEngine.Analyze(new ContextRequest(
            new StringTextSnapshot(statement), caret, EditorDialects.MongoshScript, TabScope: null), cancellationToken);
        if (context.Role is CompletionCursorRole.NonCompletable)
            throw new InvalidDataException("O editor não classificou o cursor sintético.");

        return new("kapilab-synthetic-samples-v1", $"lab-{index:D6}", rootSeed, index, form, collection, statement, caret,
            context.Role.ToString(), new("lab", "synthetic", "skipped", "samples contain no real documents; not executed"),
            fields, parsedStages, operatorName.Length == 0 ? [] : [operatorName],
            "local-mongosh-symbol-catalog; syntax checked by IDE MongoCodeValidator; no server semantics claimed");
    }

    private static void RequireCatalog(IEnumerable<string> names, string required)
    {
        if (!names.Contains(required, StringComparer.Ordinal))
            throw new InvalidDataException("O catálogo de sintaxe do editor não contém o símbolo necessário ao fixture.");
    }

    internal sealed record SyntheticSample(string Schema, string Id, int Seed, int Index, string Form, string Collection,
        string Statement, int CaretUtf16, string CursorRole, SampleProvenance Provenance,
        IReadOnlyList<SyntheticField> SchemaFields, IReadOnlyList<ParsedStage> StagesBeforeCursor,
        IReadOnlyList<string> Operators, string ValidationScope);
    internal sealed record SampleProvenance(string Split, string Origin, string Execution, string ExecutionReason);
    internal sealed record SyntheticField(string Name, string Type);
    internal sealed record ParsedStage(string Name, IReadOnlyList<ParsedStageProperty> Properties);
    internal sealed record ParsedStageProperty(string Name, string ValueKind);

    private struct StableRandom(uint state)
    {
        public int Next(int upperBound)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            return (int)((state >> 8) % (uint)upperBound);
        }
    }
}
