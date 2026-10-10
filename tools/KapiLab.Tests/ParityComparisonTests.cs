using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class ParityComparisonTests
{
    private static readonly ParityTolerances Tolerances = new(
        ParityComparison.ToleranceSchema, "fixture-1", 0.25, 0.001);

    [Test]
    public void TokenizerAndPromptRequireExactEquality()
    {
        Assert.That(ParityComparison.CompareTokenizer([3, 4], [3, 4]).Status, Is.EqualTo("matched"));
        Assert.That(ParityComparison.CompareTokenizer([3, 4], [3, 5]).Status, Is.EqualTo("mismatched"));
        Assert.That(ParityComparison.ComparePrompt("a\r\nb"u8, "a\nb"u8).Status, Is.EqualTo("mismatched"));
        Assert.That(ParityComparison.ComparePrompt("same"u8, "same"u8).Status, Is.EqualTo("matched"));
    }

    [Test]
    public void EmptyEvidenceIsInconclusiveAndCannotPass()
    {
        Assert.That(ParityComparison.CompareTokenizer([], []).Status, Is.EqualTo("inconclusive"));
        Assert.That(ParityComparison.ComparePrompt([], []).Status, Is.EqualTo("inconclusive"));
        Assert.That(ParityComparison.CompareGreedy([], [], Tolerances).Status, Is.EqualTo("inconclusive"));
        Assert.That(ParityComparison.CompareTeacherForcing([], [], Tolerances).Status, Is.EqualTo("inconclusive"));
    }

    [Test]
    public void GreedyUsesVersionedMismatchRateAndMarksDirectPath()
    {
        var result = ParityComparison.CompareGreedy([1, 2, 3, 4], [1, 9, 3, 4], Tolerances);

        Assert.That(result.Status, Is.EqualTo("matched"));
        Assert.That(result.Observed, Is.EqualTo(0.25));
        Assert.That(result.Path, Is.EqualTo("genai-direct"));
        Assert.That(ParityComparison.CompareGreedy([1, 2], [9, 8], Tolerances).Status, Is.EqualTo("mismatched"));
    }

    [Test]
    public void TeacherForcingComparesObservedFiniteLogitsAgainstVersionedTolerance()
    {
        var result = ParityComparison.CompareTeacherForcing([0.1, 0.2], [0.1005, 0.1995], Tolerances);

        Assert.That(result.Status, Is.EqualTo("matched"));
        Assert.That(result.Path, Is.EqualTo("genai-direct"));
        Assert.That(ParityComparison.CompareTeacherForcing([0.1], [0.102], Tolerances).Status, Is.EqualTo("mismatched"));
        Assert.Throws<InvalidDataException>(() => ParityComparison.CompareTeacherForcing([double.NaN], [0.1], Tolerances));
    }

    [Test]
    public void InvalidUnversionedOrOutOfRangeToleranceIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => ParityComparison.CompareGreedy([1], [1],
            Tolerances with { Schema = "unknown" }));
        Assert.Throws<InvalidDataException>(() => ParityComparison.CompareGreedy([1], [1],
            Tolerances with { GreedyMaximumTokenMismatchRate = 1.1 }));
    }
}
