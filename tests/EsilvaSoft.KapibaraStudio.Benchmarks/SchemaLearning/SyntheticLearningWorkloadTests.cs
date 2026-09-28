using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.Benchmarks.SchemaLearning;

/// <summary>
/// Prova que o gerador sintético produz exatamente a forma que os benchmarks afirmam medir: se o documento "grande"
/// não tivesse profundidade 12 nem chegasse perto dos 10 000 nós, ou se o "que estoura" não estourasse, os números
/// publicados em <c>docs/auto-complite/performance.md</c> descreveriam outro cenário.
/// </summary>
[TestFixture]
public sealed class SyntheticLearningWorkloadTests
{
    private static readonly Guid ProfileId = new("6f2f6d3a-2d6a-4f0e-9d5a-1b2c3d4e5f6f");

    private static SchemaObservationDelta Analyze(LearningDocumentShape shape)
    {
        var analyzer = new BackgroundSchemaAnalyzer(TimeProvider.System);
        var profile = SyntheticLearningWorkload.Profile(shape);
        return analyzer.Analyze(SyntheticLearningWorkload.Envelope(SyntheticLearningWorkload.Key(ProfileId), profile, Guid.NewGuid()));
    }

    [TestCase(LearningDocumentShape.Small)]
    [TestCase(LearningDocumentShape.Medium)]
    [TestCase(LearningDocumentShape.Large)]
    public void AnalyzedPathCountMatchesTheAnnouncedShape(LearningDocumentShape shape)
    {
        var profile = SyntheticLearningWorkload.Profile(shape);
        var delta = Analyze(shape);

        Assert.Multiple(() =>
        {
            Assert.That(delta.Fields, Has.Count.EqualTo(profile.DistinctPathsPerBatch), "O gerador precisa produzir os caminhos que anuncia.");
            Assert.That(delta.Fields.Max(field => field.Path.Depth), Is.EqualTo(profile.Depth), "A profundidade máxima observada é a anunciada.");
            Assert.That(delta.CompleteDocumentObservations, Is.EqualTo(profile.Documents));
            Assert.That(delta.SkippedDocuments, Is.Zero);
            Assert.That(delta.IsTruncated, Is.False, "Nenhuma das formas dentro do orçamento pode truncar.");
        });
    }

    [Test]
    public void LargeShapeSitsExactlyAtTheDepthLimitAndBelowTheNodeLimit()
    {
        var profile = SyntheticLearningWorkload.Profile(LearningDocumentShape.Large);
        var delta = Analyze(LearningDocumentShape.Large);

        Assert.Multiple(() =>
        {
            Assert.That(profile.Depth, Is.EqualTo(BackgroundSchemaAnalyzer.DefaultMaximumDepth));
            Assert.That(profile.Documents, Is.LessThanOrEqualTo(32), "schema-learning.md limita a amostra a 32 documentos por lote.");
            Assert.That(delta.Fields, Has.Count.LessThan(BackgroundSchemaAnalyzer.DefaultMaximumTotalNodes));
            Assert.That(delta.Fields.Count, Is.GreaterThan(BackgroundSchemaAnalyzer.DefaultMaximumTotalNodes * 9 / 10),
                "O caso 'grande' só é grande se encostar nos 10 000 nós.");
        });
    }

    [Test]
    public void EveryGeneratedDocumentStaysUnderTheSixtyFourKibibyteBudget()
    {
        foreach (var shape in Enum.GetValues<LearningDocumentShape>())
        {
            var profile = SyntheticLearningWorkload.Profile(shape);
            for (var seed = 0; seed < profile.Documents; seed++)
            {
                var document = SyntheticLearningWorkload.Document(seed, profile);
                Assert.That(document.Length, Is.LessThan(BackgroundSchemaAnalyzer.DefaultMaximumDocumentJsonLength),
                    $"{shape}/{seed} passaria dos 64 KiB e seria contado como descartado, não analisado.");
            }
        }
    }

    [Test]
    public void OverBudgetShapeTripsBothStructuralBudgets()
    {
        var profile = SyntheticLearningWorkload.Profile(LearningDocumentShape.OverBudget);
        var delta = Analyze(LearningDocumentShape.OverBudget);

        Assert.Multiple(() =>
        {
            Assert.That(profile.Depth, Is.GreaterThan(BackgroundSchemaAnalyzer.DefaultMaximumDepth), "A forma precisa passar da profundidade 12.");
            Assert.That(profile.DistinctPathsPerBatch, Is.GreaterThan(BackgroundSchemaAnalyzer.DefaultMaximumTotalNodes), "E também dos 10 000 nós.");
            Assert.That(delta.IsTruncated, Is.True, "O analisador precisa sinalizar o truncamento em vez de falhar o lote.");
            Assert.That(delta.Fields, Has.Count.LessThanOrEqualTo(BackgroundSchemaAnalyzer.DefaultMaximumTotalNodes));
            Assert.That(delta.Fields.Max(field => field.Path.Depth), Is.LessThanOrEqualTo(BackgroundSchemaAnalyzer.DefaultMaximumDepth));
            Assert.That(delta.SkippedDocuments, Is.Zero, "Estourar orçamento estrutural não é documento descartado.");
        });
    }

    [Test]
    public void GeneratingTheSameDocumentTwiceProducesIdenticalText()
    {
        var profile = SyntheticLearningWorkload.Profile(LearningDocumentShape.Medium);

        Assert.That(SyntheticLearningWorkload.Document(7, profile), Is.EqualTo(SyntheticLearningWorkload.Document(7, profile)));
    }

    [Test]
    public void DeltaAndSnapshotCarryTheRequestedNumberOfPaths()
    {
        var key = SyntheticLearningWorkload.Key(ProfileId);
        var delta = SyntheticLearningWorkload.Delta(key, 64, Guid.NewGuid());
        var snapshot = SyntheticLearningWorkload.Snapshot(key, 64, null);

        Assert.Multiple(() =>
        {
            Assert.That(delta.Fields, Has.Count.EqualTo(64));
            Assert.That(delta.Fields.Select(field => field.Path).Distinct().ToArray(), Has.Length.EqualTo(64));
            Assert.That(snapshot.Fields, Has.Count.EqualTo(64));
            Assert.That(snapshot.Fields.Count(field => field.Path.Depth == 2), Is.EqualTo(8), "Um caminho aninhado a cada oito.");
        });
    }
}
