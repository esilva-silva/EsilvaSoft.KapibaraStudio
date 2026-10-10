using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Context;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.SyntaxHighlighting;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Text;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class SyntheticSampleGeneratorTests
{
    [Test]
    public void SeedAndIndexReproduceTheSameRecordAsTheBatch()
    {
        var batch = SyntheticSampleGenerator.Generate(421, 20, onlyIndex: null);
        var isolated = SyntheticSampleGenerator.Generate(421, 1, onlyIndex: 7);

        Assert.That(isolated.Single(), Is.EqualTo(batch[7]));
        Assert.That(SyntheticSampleGenerator.Generate(421, 20, onlyIndex: null), Is.EqualTo(batch));
        Assert.That(SyntheticSampleGenerator.Generate(422, 20, onlyIndex: null), Is.Not.EqualTo(batch));
    }

    [Test]
    public void GeneratedSamplesCarryLabProvenanceAndNoExecutionClaim()
    {
        foreach (var line in SyntheticSampleGenerator.Generate(2026, 12, onlyIndex: null))
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            Assert.Multiple(() =>
            {
                Assert.That(root.GetProperty("schema").GetString(), Is.EqualTo("kapilab-synthetic-samples-v1"));
                Assert.That(root.GetProperty("provenance").GetProperty("split").GetString(), Is.EqualTo("lab"));
                Assert.That(root.GetProperty("provenance").GetProperty("origin").GetString(), Is.EqualTo("synthetic"));
                Assert.That(root.GetProperty("provenance").GetProperty("execution").GetString(), Is.EqualTo("skipped"));
                Assert.That(root.GetProperty("statement").GetString(), Does.Contain("lab_collection_"));
                Assert.That(root.GetProperty("schemaFields").GetArrayLength(), Is.EqualTo(8));
            });
        }
    }

    [Test]
    public void StatementsPassTheIdeSyntaxValidatorAndCursorParser()
    {
        foreach (var line in SyntheticSampleGenerator.Generate(99, 30, onlyIndex: null))
        {
            using var json = JsonDocument.Parse(line);
            var statement = json.RootElement.GetProperty("statement").GetString()!;
            var caret = json.RootElement.GetProperty("caretUtf16").GetInt32();
            var validation = new EsilvaSoft.KapibaraStudio.Infrastructure.MongoCodeValidator()
                .ValidateAsync(statement, aggregation: false).GetAwaiter().GetResult();
            Assert.That(validation.IsValid, Is.True, validation.Message);
            Assert.That(caret, Is.InRange(0, statement.Length));
            var context = CompletionContextEngine.Analyze(new ContextRequest(new StringTextSnapshot(statement), caret,
                EditorDialects.MongoshScript, TabScope: null));
            Assert.That(context.Role, Is.Not.EqualTo(CompletionCursorRole.NonCompletable));
            if (json.RootElement.GetProperty("form").GetString() == "aggregate")
                Assert.That(json.RootElement.GetProperty("stagesBeforeCursor").GetArrayLength(), Is.EqualTo(1));
        }
    }

    [Test]
    public void OperatorAndStageSymbolsComeFromTheRealEditorCatalog()
    {
        var lines = SyntheticSampleGenerator.Generate(10, 2, onlyIndex: null);
        using var find = JsonDocument.Parse(lines[0]);
        using var aggregate = JsonDocument.Parse(lines[1]);
        Assert.That(MongoSyntaxVocabulary.Operators, Does.Contain(find.RootElement.GetProperty("operators")[0].GetString()));
        Assert.That(MongoSyntaxVocabulary.AggregationStages, Does.Contain("$match"));
        Assert.That(MongoSyntaxVocabulary.AggregationStages, Does.Contain("$project"));
        Assert.That(aggregate.RootElement.GetProperty("validationScope").GetString(), Does.Contain("no server semantics"));
    }

    [Test]
    public void FixedSeedProducesDiverseCatalogBackedQueriesAndStages()
    {
        var lines = SyntheticSampleGenerator.Generate(73011, 400, onlyIndex: null);
        var operators = new HashSet<string>(StringComparer.Ordinal);
        var stages = new HashSet<string>(StringComparer.Ordinal);
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            using var json = JsonDocument.Parse(line);
            var root = json.RootElement;
            Assert.That(root.GetProperty("provenance").GetProperty("split").GetString(), Is.EqualTo("lab"));
            Assert.That(root.GetProperty("provenance").GetProperty("origin").GetString(), Is.EqualTo("synthetic"));
            Assert.That(root.GetProperty("provenance").GetProperty("execution").GetString(), Is.EqualTo("skipped"));
            Assert.That(root.GetProperty("id").GetString(), Does.StartWith("lab-"));
            var statement = root.GetProperty("statement").GetString()!;
            var caret = root.GetProperty("caretUtf16").GetInt32();
            Assert.That(caret, Is.InRange(0, statement.Length));
            var syntax = new EsilvaSoft.KapibaraStudio.Infrastructure.MongoCodeValidator()
                .ValidateAsync(statement, aggregation: false).GetAwaiter().GetResult();
            Assert.That(syntax.IsValid, Is.True, syntax.Message);
            var context = CompletionContextEngine.Analyze(new ContextRequest(new StringTextSnapshot(statement), caret,
                EditorDialects.MongoshScript, TabScope: null));
            Assert.That(context.Role, Is.Not.EqualTo(CompletionCursorRole.NonCompletable));
            foreach (var op in root.GetProperty("operators").EnumerateArray())
            {
                var name = op.GetString()!;
                Assert.That(MongoSyntaxVocabulary.Operators, Does.Contain(name));
                operators.Add(name);
            }
            foreach (var field in root.GetProperty("schemaFields").EnumerateArray()) fields.Add(field.GetProperty("name").GetString()!);
            if (root.GetProperty("form").GetString() != "aggregate") continue;
            var stageName = statement[(statement.IndexOf("}, { ", StringComparison.Ordinal) + 5)..].Split(':', 2)[0].Trim();
            Assert.That(MongoSyntaxVocabulary.AggregationStages, Does.Contain(stageName));
            stages.Add(stageName);
            Assert.That(root.GetProperty("stagesBeforeCursor").GetArrayLength(), Is.EqualTo(1));
            Assert.That(caret, Is.EqualTo(statement.IndexOf(stageName, StringComparison.Ordinal)));
        }

        Assert.Multiple(() =>
        {
            Assert.That(operators.Count, Is.GreaterThanOrEqualTo(3));
            Assert.That(stages.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(fields.Count, Is.GreaterThanOrEqualTo(8));
        });
        Assert.That(SyntheticSampleGenerator.Generate(73011, 400, onlyIndex: null), Is.EqualTo(lines));
    }

    [TestCase(0, null)]
    [TestCase(10_001, null)]
    [TestCase(2, 3)]
    [TestCase(1, -1)]
    public void InvalidCountAndIndexAreRejected(int count, int? index)
    {
        Assert.That(() => SyntheticSampleGenerator.Generate(1, count, index), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public async Task CliWritesJsonlAtomicallyOnlyInsideTheLabWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "kapilab-samples-" + Guid.NewGuid().ToString("N"));
        var destination = Path.Combine("data", "lab", "samples.jsonl");
        var originalOut = Console.Out;
        var capture = new StringWriter();
        Directory.CreateDirectory(workspace);
        Console.SetOut(capture);
        try
        {
            var exit = await SamplesGenerateCommand.RunAsync(123, 4, null, destination, workspace);
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(capture.ToString(), Is.Empty);
            var fullPath = Path.Combine(workspace, destination);
            var bytes = await File.ReadAllBytesAsync(fullPath);
            Assert.That(bytes.Take(3), Is.Not.EqualTo(Encoding.UTF8.Preamble.ToArray()));
            Assert.That(File.ReadAllLines(fullPath), Has.Length.EqualTo(4));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(fullPath)!), Has.Length.EqualTo(1));
        }
        finally
        {
            Console.SetOut(originalOut);
            capture.Dispose();
            if (Directory.Exists(workspace)) Directory.Delete(workspace, recursive: true);
        }
    }

    [Test]
    public void OutputPathOutsideWorkspaceIsRejected()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "kapilab-samples-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        try
        {
            Assert.That(() => LabWorkspace.ResolveOutput(workspace, "../outside.jsonl"), Throws.TypeOf<UnauthorizedAccessException>());
        }
        finally
        {
            Directory.Delete(workspace);
        }
    }
}
