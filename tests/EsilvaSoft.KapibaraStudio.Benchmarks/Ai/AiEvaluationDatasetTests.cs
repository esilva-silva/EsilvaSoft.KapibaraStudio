using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.Ai;

/// <summary>
/// Prova que o dataset sintético é determinístico e que a distribuição documentada em
/// <see cref="AiEvaluationDataset"/> é a que o gerador realmente produz.
/// </summary>
[TestFixture]
public sealed class AiEvaluationDatasetTests
{
    /// <summary>Dez períodos completos do esquema de quotas; as proporções são exatas neste tamanho.</summary>
    private const int PeriodicCount = 180;

    [Test]
    public void GeneratingTwiceProducesIdenticalCases()
    {
        var first = AiEvaluationDataset.Create(AiEvaluationDataset.SmokeSeed, PeriodicCount).Cases;
        var second = AiEvaluationDataset.Create(AiEvaluationDataset.SmokeSeed, PeriodicCount).Cases;

        Assert.That(second, Has.Count.EqualTo(first.Count));
        for (var index = 0; index < first.Count; index++)
        {
            var (left, right) = (first[index], second[index]);
            Assert.Multiple(() =>
            {
                Assert.That(right.Id, Is.EqualTo(left.Id));
                Assert.That(right.Seed, Is.EqualTo(left.Seed));
                Assert.That(right.EditorText, Is.EqualTo(left.EditorText));
                Assert.That(right.Caret, Is.EqualTo(left.Caret));
                Assert.That(right.Shape, Is.EqualTo(left.Shape));
                Assert.That(right.SchemaSize, Is.EqualTo(left.SchemaSize));
                Assert.That(right.HasLearnedSchema, Is.EqualTo(left.HasLearnedSchema));
                Assert.That(right.ForeignCollections, Is.EqualTo(left.ForeignCollections));
                Assert.That(right.Pipeline.Select(stage => stage.Name), Is.EqualTo(left.Pipeline.Select(stage => stage.Name)));
                Assert.That(right.KnownNames, Is.EqualTo(left.KnownNames));
            });
        }
    }

    [Test]
    public void ACaseDoesNotDependOnHowManyCasesWereAskedFor()
    {
        var small = AiEvaluationDataset.Create(AiEvaluationDataset.RootSeed, 7).Cases[6];
        var large = AiEvaluationDataset.Create(AiEvaluationDataset.RootSeed, PeriodicCount).Cases[6];

        Assert.Multiple(() =>
        {
            Assert.That(large.Id, Is.EqualTo(small.Id));
            Assert.That(large.EditorText, Is.EqualTo(small.EditorText));
            Assert.That(large.Caret, Is.EqualTo(small.Caret));
        });
    }

    [Test]
    public void DifferentSeedsProduceDifferentText()
    {
        var registered = AiEvaluationDataset.Create(AiEvaluationDataset.RootSeed, 12).Cases;
        var smoke = AiEvaluationDataset.Create(AiEvaluationDataset.SmokeSeed, 12).Cases;

        Assert.That(smoke.Select(item => item.EditorText), Is.Not.EqualTo(registered.Select(item => item.EditorText)));
        Assert.That(smoke.Select(item => item.Shape), Is.EqualTo(registered.Select(item => item.Shape)),
            "A forma vem da quota, não da semente: trocar a semente não pode desbalancear o dataset.");
    }

    [Test]
    public void DistributionOverAWholeNumberOfPeriodsIsExact()
    {
        var distribution = AiEvaluationDataset.Create(AiEvaluationDataset.RootSeed, PeriodicCount).Distribution;

        Assert.Multiple(() =>
        {
            Assert.That(distribution.Total, Is.EqualTo(180));
            Assert.That(distribution.ByShape[AiEvaluationShape.Filter], Is.EqualTo(60));
            Assert.That(distribution.ByShape[AiEvaluationShape.Aggregation], Is.EqualTo(60));
            Assert.That(distribution.ByShape[AiEvaluationShape.Update], Is.EqualTo(60));
            Assert.That(distribution.BySchemaSize[AiEvaluationSchemaSize.Small], Is.EqualTo(60));
            Assert.That(distribution.BySchemaSize[AiEvaluationSchemaSize.Medium], Is.EqualTo(60));
            Assert.That(distribution.BySchemaSize[AiEvaluationSchemaSize.Large], Is.EqualTo(60));
            Assert.That(distribution.WithLookup, Is.EqualTo(30), "Metade das agregações cita coleção estrangeira.");
            Assert.That(distribution.WithoutLookup, Is.EqualTo(150));
            Assert.That(distribution.WithLearnedSchema, Is.EqualTo(90));
            Assert.That(distribution.WithoutLearnedSchema, Is.EqualTo(90));
            Assert.That(distribution.DistinctCollections, Is.EqualTo(AiEvaluationDataset.CollectionCount));
        });
    }

    [Test]
    public void DefaultDatasetHasTheDocumentedDistribution()
    {
        var dataset = AiEvaluationDataset.CreateDefault();
        var distribution = dataset.Distribution;

        Assert.Multiple(() =>
        {
            Assert.That(dataset.RootSeedUsed, Is.EqualTo(AiEvaluationDataset.RootSeed));
            Assert.That(distribution.Total, Is.EqualTo(AiEvaluationDataset.DefaultCaseCount));
            // 10 000 não é múltiplo do período 18; estes são os números reais, e mudar a regra de geração sem
            // atualizar a documentação quebra aqui.
            Assert.That(distribution.ByShape[AiEvaluationShape.Filter], Is.EqualTo(3334));
            Assert.That(distribution.ByShape[AiEvaluationShape.Aggregation], Is.EqualTo(3333));
            Assert.That(distribution.ByShape[AiEvaluationShape.Update], Is.EqualTo(3333));
            Assert.That(distribution.BySchemaSize[AiEvaluationSchemaSize.Small], Is.EqualTo(3334));
            Assert.That(distribution.BySchemaSize[AiEvaluationSchemaSize.Medium], Is.EqualTo(3333));
            Assert.That(distribution.BySchemaSize[AiEvaluationSchemaSize.Large], Is.EqualTo(3333));
            Assert.That(distribution.WithLookup, Is.EqualTo(1667));
            Assert.That(distribution.WithLearnedSchema, Is.EqualTo(5004));
            Assert.That(distribution.DistinctCollections, Is.EqualTo(AiEvaluationDataset.CollectionCount));
        });
    }

    [Test]
    public void EveryCaseIsWellFormed()
    {
        var dataset = AiEvaluationDataset.Create(AiEvaluationDataset.RootSeed, PeriodicCount);

        Assert.Multiple(() =>
        {
            Assert.That(dataset.Cases.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(dataset.Cases.Count), "Identificadores de caso são únicos.");
            foreach (var item in dataset.Cases)
            {
                Assert.That(item.Caret, Is.InRange(0, item.EditorText.Length));
                Assert.That(item.EditorText, Does.Contain(item.Collection));
                Assert.That(item.ForeignCollections, Does.Not.Contain(item.Collection));
                Assert.That(dataset.Schemas.ContainsKey(item.Collection), Is.True);
                Assert.That(item.HasLookup, Is.EqualTo(item.Shape == AiEvaluationShape.Aggregation && item.ForeignCollections.Count > 0));
            }
        });
    }

    [Test]
    public void OnlyAggregationsDeclareAPipeline()
    {
        var dataset = AiEvaluationDataset.Create(AiEvaluationDataset.RootSeed, PeriodicCount);

        Assert.Multiple(() =>
        {
            foreach (var item in dataset.Cases.Where(item => item.Shape != AiEvaluationShape.Aggregation))
                Assert.That(item.Pipeline, Is.Empty);
            foreach (var item in dataset.Cases.Where(item => item.Shape == AiEvaluationShape.Aggregation))
                Assert.That(item.Pipeline.Select(stage => stage.Name), Does.Contain("$match"));
            foreach (var item in dataset.Cases.Where(item => item.HasLookup))
                Assert.That(item.Pipeline.Count(stage => stage.Name == "$lookup"), Is.EqualTo(item.ForeignCollections.Count));
        });
    }
}
