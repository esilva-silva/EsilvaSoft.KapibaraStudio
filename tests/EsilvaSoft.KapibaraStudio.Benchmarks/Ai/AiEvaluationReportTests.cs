using System.Text.Json;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Agregação e serialização do relatório, provadas contra medições fabricadas e pequenas — sem rodar o dataset, para
/// que uma regressão de estatística apareça como falha de aritmética e não como ruído de máquina.
/// </summary>
[TestFixture]
public sealed class AiEvaluationReportTests
{
    private static readonly string[] ExpectedFailures = ["d"];
    private static readonly string[] ExpectedDimensions = ["shape", "schemaSize", "lookup", "learnedSchema"];

    private static AiCaseMeasurement Measurement(string id, int promptTokens, bool deterministic = true,
        AiEvaluationShape shape = AiEvaluationShape.Filter, bool hasLookup = false) => new()
        {
            CaseId = id,
            Seed = 1,
            Shape = shape,
            SchemaSize = AiEvaluationSchemaSize.Small,
            HasLookup = hasLookup,
            HasLearnedSchema = false,
            FactCount = 10,
            PromptTokens = promptTokens,
            FactTokens = 40,
            PromptCharacters = promptTokens * 4,
            SelectionMicroseconds = 10,
            AssemblyMicroseconds = 5,
            AvailableTokens = 250,
            IsDeterministic = deterministic
        };

    private static AiEvaluationReport Report(params AiCaseMeasurement[] measurements) =>
        AiEvaluationReport.Aggregate("editor-context-v1", "FakeCounter", 42, 3, 1,
            AiEvaluationDistribution.Of(AiEvaluationDataset.Create(AiEvaluationDataset.SmokeSeed, measurements.Length).Cases),
            measurements);

    [Test]
    public void SummaryComputesMeanDeviationAndPercentiles()
    {
        var summary = AiMetricSummary.Of([100, 200, 300, 400]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Count, Is.EqualTo(4));
            Assert.That(summary.Mean, Is.EqualTo(250).Within(1e-9));
            Assert.That(summary.StandardDeviation, Is.EqualTo(Math.Sqrt(50000d / 3)).Within(1e-9));
            Assert.That(summary.Minimum, Is.EqualTo(100).Within(1e-9));
            Assert.That(summary.Median, Is.EqualTo(250).Within(1e-9));
            Assert.That(summary.Percentile95, Is.EqualTo(385).Within(1e-9));
            Assert.That(summary.Maximum, Is.EqualTo(400).Within(1e-9));
        });
    }

    [Test]
    public void SummaryOfASingleValueHasNoDeviation()
    {
        var summary = AiMetricSummary.Of([7]);

        Assert.Multiple(() =>
        {
            Assert.That(summary.Mean, Is.EqualTo(7).Within(1e-9));
            Assert.That(summary.StandardDeviation, Is.Zero);
            Assert.That(summary.Percentile95, Is.EqualTo(7).Within(1e-9));
        });
    }

    [Test]
    public void SummaryOfNothingIsEmpty() => Assert.That(AiMetricSummary.Of(Array.Empty<int>()), Is.SameAs(AiMetricSummary.Empty));

    [Test]
    public void AggregationCountsOverflowDeterminismAndTokens()
    {
        var report = Report(
            Measurement("a", 100),
            Measurement("b", 200),
            Measurement("c", 300),
            Measurement("d", 400, deterministic: false));

        Assert.Multiple(() =>
        {
            Assert.That(report.Version, Is.EqualTo(AiEvaluationReport.FormatVersion));
            Assert.That(report.ContractId, Is.EqualTo("editor-context-v1"));
            Assert.That(report.TokenCounter, Is.EqualTo("FakeCounter"));
            Assert.That(report.RootSeed, Is.EqualTo(42));
            Assert.That(report.PromptTokens.Mean, Is.EqualTo(250).Within(1e-9));
            Assert.That(report.TotalMicroseconds.Mean, Is.EqualTo(15).Within(1e-9));
            Assert.That(report.BudgetOverflowCases, Is.EqualTo(2), "300 e 400 passam dos 250 disponíveis.");
            Assert.That(report.MaximumOverflowTokens, Is.EqualTo(150));
            Assert.That(report.BudgetOverflowRate, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(report.NonDeterministicCases, Is.EqualTo(1));
            Assert.That(report.NonDeterministicCaseIds, Is.EqualTo(ExpectedFailures));
            Assert.That(report.Environment.LogicalProcessors, Is.GreaterThan(0));
            Assert.That(report.Environment.Runtime, Is.Not.Empty);
        });
    }

    [Test]
    public void AggregationOfNothingIsAllZero()
    {
        var report = AiEvaluationReport.Aggregate("editor-context-v1", "FakeCounter", 0, 2, 0,
            AiEvaluationDistribution.Of([]), []);

        Assert.Multiple(() =>
        {
            Assert.That(report.Distribution.Total, Is.Zero);
            Assert.That(report.BudgetOverflowCases, Is.Zero);
            Assert.That(report.BudgetOverflowRate, Is.Zero);
            Assert.That(report.MaximumOverflowTokens, Is.Zero);
            Assert.That(report.Segments, Is.Empty);
            Assert.That(report.PromptTokens, Is.SameAs(AiMetricSummary.Empty));
        });
    }

    [Test]
    public void SegmentsSplitEveryDocumentedDimension()
    {
        var report = Report(
            Measurement("a", 100, shape: AiEvaluationShape.Filter),
            Measurement("b", 300, shape: AiEvaluationShape.Aggregation, hasLookup: true),
            Measurement("c", 500, shape: AiEvaluationShape.Aggregation, hasLookup: true));

        var dimensions = report.Segments.Select(segment => segment.Dimension).Distinct(StringComparer.Ordinal).ToArray();
        var lookup = report.Segments.Single(segment => segment.Dimension == "lookup" && segment.Value == "com");

        Assert.Multiple(() =>
        {
            Assert.That(dimensions, Is.EquivalentTo(ExpectedDimensions));
            Assert.That(lookup.Cases, Is.EqualTo(2));
            Assert.That(lookup.PromptTokens.Mean, Is.EqualTo(400).Within(1e-9));
            Assert.That(lookup.BudgetOverflowCases, Is.EqualTo(2));
            Assert.That(report.Segments.Sum(segment => segment.Cases), Is.EqualTo(3 * dimensions.Length));
        });
    }

    [Test]
    public void AggregationRefusesANullMeasurement() => Assert.That(
        () => AiEvaluationReport.Aggregate("editor-context-v1", "FakeCounter", 0, 2, 0, AiEvaluationDistribution.Of([]), [null!]),
        Throws.ArgumentException);

    [Test]
    public void JsonOutputIsParseableAndCarriesTheAggregate()
    {
        var json = AiEvaluationReportWriter.ToJson(Report(Measurement("a", 100), Measurement("b", 300)));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("version").GetInt32(), Is.EqualTo(AiEvaluationReport.FormatVersion));
            Assert.That(root.GetProperty("contractId").GetString(), Is.EqualTo("editor-context-v1"));
            Assert.That(root.GetProperty("budgetOverflowCases").GetInt32(), Is.EqualTo(1));
            Assert.That(root.GetProperty("promptTokens").GetProperty("standardDeviation").GetDouble(), Is.GreaterThan(0));
            Assert.That(root.GetProperty("environment").GetProperty("runtime").GetString(), Is.Not.Empty);
            Assert.That(root.GetProperty("distribution").GetProperty("byShape").GetProperty("Filter").GetInt32(), Is.GreaterThanOrEqualTo(0));
            Assert.That(root.GetProperty("segments").GetArrayLength(), Is.GreaterThan(0));
        });
    }

    [Test]
    public void MarkdownOutputHasEverySectionAndTheDeviationColumn()
    {
        var markdown = AiEvaluationReportWriter.ToMarkdown(Report(Measurement("a", 100), Measurement("b", 300)));

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.StartWith("# Avaliação de contexto para IA"));
            foreach (var section in AiEvaluationReportWriter.MarkdownSections)
                Assert.That(markdown, Does.Contain(section));
            Assert.That(markdown, Does.Contain("Desvio padrão"), "Latência sem dispersão não é medição.");
            Assert.That(markdown, Does.Contain("tokens do prompt"));
            Assert.That(markdown, Does.Contain("Casos não determinísticos: 0"));
        });
    }

    [Test]
    public async Task WriterProducesBothFilesInTheGivenDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "slop-ai-report-" + Guid.NewGuid().ToString("N"));
        try
        {
            var files = await AiEvaluationReportWriter.WriteAsync(Report(Measurement("a", 100)), directory, TestContext.CurrentContext.CancellationToken);

            Assert.Multiple(() =>
            {
                Assert.That(files, Has.Count.EqualTo(2));
                Assert.That(files[0], Does.EndWith(AiEvaluationReportWriter.FileBaseName + ".json"));
                Assert.That(files[1], Does.EndWith(AiEvaluationReportWriter.FileBaseName + ".md"));
                Assert.That(File.Exists(files[0]), Is.True);
                Assert.That(File.Exists(files[1]), Is.True);
            });
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(files[0], TestContext.CurrentContext.CancellationToken));
            Assert.That(document.RootElement.GetProperty("tokenCounter").GetString(), Is.EqualTo("FakeCounter"));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void DefaultOutputDirectoryEndsInsideTheBenchmarksProject()
    {
        var directory = AiEvaluationReportWriter.DefaultDirectory();

        Assert.That(directory, Does.EndWith(Path.Combine("Ai", "output")));
    }
}
