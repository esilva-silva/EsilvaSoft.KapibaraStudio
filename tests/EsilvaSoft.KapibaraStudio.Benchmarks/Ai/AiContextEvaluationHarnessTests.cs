using EsilvaSoft.KapibaraStudio.Application.AiContext;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Facts;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Prova que o harness mede o que diz medir. Usa um recorte pequeno do dataset: o objetivo é a corretude do
/// instrumento, não o número de performance — esse sai da execução manual, com máquina declarada.
/// </summary>
[TestFixture]
public sealed class AiContextEvaluationHarnessTests
{
    private const int SampleCount = 36;
    private static AiEvaluationDataset Dataset() => AiEvaluationDataset.Create(AiEvaluationDataset.SmokeSeed, SampleCount);

    [Test]
    public void HarnessDeclaresTheFrozenContractAndTheModelFreeCounterByDefault()
    {
        var harness = new AiContextEvaluationHarness(Dataset());

        Assert.Multiple(() =>
        {
            Assert.That(harness.ContractId, Is.EqualTo(EditorContextV1Contract.Instance.ContractId));
            Assert.That(harness.TokenCounterName, Is.EqualTo(nameof(DeterministicTokenCounter)));
        });
    }

    [Test]
    public void EveryCaseIsMeasuredAsDeterministic()
    {
        var report = new AiContextEvaluationHarness(Dataset()).Run(TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(report.Distribution.Total, Is.EqualTo(SampleCount));
            Assert.That(report.NonDeterministicCases, Is.Zero);
            Assert.That(report.NonDeterministicCaseIds, Is.Empty);
        });
    }

    [Test]
    public void MeasuringTheSameCaseTwiceGivesTheSamePromptAndFacts()
    {
        var dataset = Dataset();
        var harness = new AiContextEvaluationHarness(dataset);
        var sample = dataset.Cases.First(item => item.HasLookup && item.HasLearnedSchema);

        var first = harness.Measure(sample, TestContext.CurrentContext.CancellationToken);
        var second = harness.Measure(sample, TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(second.PromptTokens, Is.EqualTo(first.PromptTokens));
            Assert.That(second.PromptCharacters, Is.EqualTo(first.PromptCharacters));
            Assert.That(second.FactCount, Is.EqualTo(first.FactCount));
            Assert.That(second.FactTokens, Is.EqualTo(first.FactTokens));
            Assert.That(first.IsDeterministic, Is.True);
            Assert.That(AiContextEvaluationHarness.Prompt(harness.Assemble(sample)),
                Is.EqualTo(AiContextEvaluationHarness.Prompt(harness.Assemble(sample))));
        });
    }

    [Test]
    public void MeasurementCarriesTheCategoriesOfItsCase()
    {
        var dataset = Dataset();
        var harness = new AiContextEvaluationHarness(dataset);

        foreach (var sample in dataset.Cases.Take(6))
        {
            var measurement = harness.Measure(sample, TestContext.CurrentContext.CancellationToken);
            Assert.Multiple(() =>
            {
                Assert.That(measurement.CaseId, Is.EqualTo(sample.Id));
                Assert.That(measurement.Seed, Is.EqualTo(sample.Seed));
                Assert.That(measurement.Shape, Is.EqualTo(sample.Shape));
                Assert.That(measurement.SchemaSize, Is.EqualTo(sample.SchemaSize));
                Assert.That(measurement.HasLookup, Is.EqualTo(sample.HasLookup));
                Assert.That(measurement.HasLearnedSchema, Is.EqualTo(sample.HasLearnedSchema));
                Assert.That(measurement.AvailableTokens, Is.EqualTo(sample.Budget.AvailableTokens));
                Assert.That(measurement.PromptTokens, Is.GreaterThan(0));
                Assert.That(measurement.FactCount, Is.GreaterThan(0));
                Assert.That(measurement.TotalMicroseconds, Is.GreaterThan(0));
            });
        }
    }

    [Test]
    public void OverflowIsDerivedFromTheBudgetOfTheCase()
    {
        var measurement = new AiCaseMeasurement
        {
            CaseId = "x", Seed = 0, Shape = AiEvaluationShape.Filter, SchemaSize = AiEvaluationSchemaSize.Small,
            HasLookup = false, HasLearnedSchema = false, FactCount = 1, PromptTokens = 2100, FactTokens = 1,
            PromptCharacters = 8000, SelectionMicroseconds = 1, AssemblyMicroseconds = 1, AvailableTokens = 2000,
            IsDeterministic = true
        };

        Assert.Multiple(() =>
        {
            Assert.That(measurement.ExceedsBudget, Is.True);
            Assert.That(measurement.OverflowTokens, Is.EqualTo(100));
            Assert.That((measurement with { PromptTokens = 1000 }).ExceedsBudget, Is.False);
            Assert.That((measurement with { PromptTokens = 1000 }).OverflowTokens, Is.Zero);
        });
    }

    [Test]
    public void SelectionNeverLeavesTheTargetOrTheForeignCollectionsOfTheCase()
    {
        var dataset = Dataset();
        var harness = new AiContextEvaluationHarness(dataset);

        Assert.Multiple(() =>
        {
            foreach (var sample in dataset.Cases)
            {
                var allowed = new HashSet<string>(sample.ForeignCollections, StringComparer.Ordinal) { sample.Collection };
                foreach (var fact in harness.Select(sample, TestContext.CurrentContext.CancellationToken)
                             .Where(fact => fact.Scope.Kind == AiFactScopeKind.Collection))
                    Assert.That(allowed, Does.Contain(fact.Scope.Collection),
                        "O catálogo publica 40 coleções; só a alvo e as do $lookup podem virar fato.");
            }
        });
    }

    [Test]
    public void LearnedSchemaFactsAppearOnlyWhenTheCaseDeclaresThem()
    {
        var dataset = Dataset();
        var harness = new AiContextEvaluationHarness(dataset);

        Assert.Multiple(() =>
        {
            foreach (var sample in dataset.Cases)
            {
                var facts = harness.Select(sample, TestContext.CurrentContext.CancellationToken);
                var learned = facts.OfKind(AiFactKind.LearnedFieldSchema);
                if (!sample.HasLearnedSchema)
                {
                    Assert.That(learned, Is.Empty, sample.Id);
                    continue;
                }
                // O seletor ordena schema catalogado antes de schema aprendido: num conjunto truncado pelo teto de
                // fatos os aprendidos somem legitimamente, e exigir o contrário aqui seria exigir outro seletor.
                if (facts.Count < harness.MaximumFacts) Assert.That(learned, Is.Not.Empty, sample.Id);
                foreach (var fact in learned) Assert.That(fact.Origin, Is.EqualTo(AiFactOrigin.LearnedSchema));
            }
        });
    }

    [Test]
    public void RenderedFactsCarryNoDocumentValues()
    {
        var dataset = Dataset();
        var harness = new AiContextEvaluationHarness(dataset);
        var sample = dataset.Cases.First(item => item.HasLearnedSchema);

        var rendered = AiContextEvaluationHarness.RenderFacts(harness.Select(sample, TestContext.CurrentContext.CancellationToken));

        Assert.Multiple(() =>
        {
            Assert.That(rendered, Is.Not.Empty);
            Assert.That(rendered, Does.Not.Contain("ativo"), "\"ativo\" é um literal do editor, não um fato de schema.");
            Assert.That(rendered.Split('\n', StringSplitOptions.RemoveEmptyEntries),
                Has.Length.EqualTo(harness.Select(sample, TestContext.CurrentContext.CancellationToken).Count));
        });
    }

    [Test]
    public void TokenCounterCountsWordsNumbersAndPunctuationAndNothingElse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DeterministicTokenCounter.CountTokens(""), Is.Zero);
            Assert.That(DeterministicTokenCounter.CountTokens("   \n\t  "), Is.Zero);
            Assert.That(DeterministicTokenCounter.CountTokens("db.clientes"), Is.EqualTo(3));
            // '{', '$gte', ':', '10', '}' — '$' faz parte da palavra, o espaço não conta.
            Assert.That(DeterministicTokenCounter.CountTokens("{ $gte: 10 }"), Is.EqualTo(5));
            Assert.That(DeterministicTokenCounter.Instance.Count("db.clientes").IsExact, Is.True);
            Assert.That(DeterministicTokenCounter.Instance.Count("db.clientes").Tokens, Is.EqualTo(3));
        });
    }

    [Test]
    public void RunningASubsetReportsOnlyThatSubset()
    {
        var dataset = Dataset();
        var subset = dataset.Cases.Take(3).ToArray();

        var report = new AiContextEvaluationHarness(dataset).Run(subset, TestContext.CurrentContext.CancellationToken);

        Assert.Multiple(() =>
        {
            Assert.That(report.Distribution.Total, Is.EqualTo(3));
            Assert.That(report.PromptTokens.Count, Is.EqualTo(3));
            Assert.That(report.SelectionMicroseconds.Count, Is.EqualTo(3));
        });
    }
}
