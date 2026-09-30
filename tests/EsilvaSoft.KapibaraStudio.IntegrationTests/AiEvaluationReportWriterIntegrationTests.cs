using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests;

[TestFixture]
[Category("Integration")]
public sealed class AiEvaluationReportWriterIntegrationTests
{
    [Test]
    public async Task WritesJsonAndMarkdownInsideTheProvidedSyntheticDirectory()
    {
        using var directory = new SyntheticDirectory();
        var report = CreateReport();

        var files = await AiEvaluationReportWriter.WriteAsync(report, directory.Path,
            TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(files, Has.Count.EqualTo(2));
            Assert.That(files[0], Is.EqualTo(Path.Combine(directory.Path, AiEvaluationReportWriter.FileBaseName + ".json")));
            Assert.That(files[1], Is.EqualTo(Path.Combine(directory.Path, AiEvaluationReportWriter.FileBaseName + ".md")));
            Assert.That(File.Exists(files[0]), Is.True);
            Assert.That(File.Exists(files[1]), Is.True);
        });

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(files[0],
            TestContext.CurrentContext.CancellationToken));
        Assert.That(document.RootElement.GetProperty("tokenCounter").GetString(), Is.EqualTo("IntegrationCounter"));
        Assert.That(await File.ReadAllTextAsync(files[1], TestContext.CurrentContext.CancellationToken),
            Does.Contain("# Avaliação de contexto para IA"));
    }

    [Test]
    public void ResolvesDefaultOutputDirectoryToTheBenchmarksProject()
    {
        var directory = AiEvaluationReportWriter.DefaultDirectory();

        Assert.That(directory, Does.EndWith(Path.Combine("tests", "EsilvaSoft.KapibaraStudio.Benchmarks", "Ai", "output")));
    }

    private static AiEvaluationReport CreateReport()
    {
        var measurement = new AiCaseMeasurement
        {
            CaseId = "integration-case",
            Seed = 1,
            Shape = AiEvaluationShape.Filter,
            SchemaSize = AiEvaluationSchemaSize.Small,
            HasLookup = false,
            HasLearnedSchema = false,
            FactCount = 1,
            PromptTokens = 3,
            FactTokens = 2,
            PromptCharacters = 12,
            SelectionMicroseconds = 10,
            AssemblyMicroseconds = 5,
            AvailableTokens = 100,
            IsDeterministic = true
        };

        return AiEvaluationReport.Aggregate("integration-contract", "IntegrationCounter", 42, 2, 1,
            AiEvaluationDistribution.Of(AiEvaluationDataset.Create(AiEvaluationDataset.SmokeSeed, 1).Cases), [measurement],
            new AiEvaluationEnvironment
            {
                OperatingSystem = "Integration host", Architecture = "x64", LogicalProcessors = 1,
                Runtime = ".NET integration", ServerGarbageCollection = false, DebuggerAttached = false,
                Configuration = "Integration"
            });
    }
}
