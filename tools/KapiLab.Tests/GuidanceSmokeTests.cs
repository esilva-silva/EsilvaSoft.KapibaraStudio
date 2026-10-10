using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class GuidanceSmokeTests
{
    [TestCase(true, 1, "supported")]
    [TestCase(false, null, "not_supported")]
    [TestCase(false, 0, "inconclusive")]
    public void StatusRequiresObservedOutputBeforeCallingGuidanceUnsupported(bool supported, int? tokens, string expected)
    {
        Assert.That(GuidanceSmoke.ClassifyStatus(supported, tokens), Is.EqualTo(expected));
    }

    [Test]
    public void UnsupportedCapabilityHasNoGrammarOrOverheadResult()
    {
        var result = Measure(() => new(false, "Guidance indisponível.", true,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(15), 5));

        Assert.Multiple(() =>
        {
            Assert.That(result.Supported, Is.False);
            Assert.That(result.Error, Is.EqualTo("Guidance indisponível."));
            Assert.That(result.GrammarOk, Is.Null);
            Assert.That(result.OverheadUsPerToken, Is.Null);
        });
    }

    [Test]
    public void SuccessfulMeasurementPreservesIndependentGrammarClassificationAndComputesOverhead()
    {
        var result = Measure(() => new(true, null, false,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(12), 4));

        Assert.Multiple(() =>
        {
            Assert.That(result.Supported, Is.True);
            Assert.That(result.Error, Is.Null);
            Assert.That(result.GrammarOk, Is.False);
            Assert.That(result.OverheadUsPerToken, Is.EqualTo(500).Within(0.001));
        });
    }

    [Test]
    public void ExceptionsAreSanitizedAndDoNotExposeInputOrNativeMessage()
    {
        var result = Measure(() => throw new GuidanceUnavailableException("secret prompt and grammar"));

        Assert.Multiple(() =>
        {
            Assert.That(result.Supported, Is.False);
            Assert.That(result.Error, Is.EqualTo("Falha no guidance (detalhes omitidos)."));
            Assert.That(result.Error, Does.Not.Contain("secret"));
            Assert.That(result.GrammarOk, Is.Null);
            Assert.That(result.OverheadUsPerToken, Is.Null);
        });
    }

    [Test]
    public void RuntimeFailureAndCancellationAreNotMisclassifiedAsMissingGuidance()
    {
        Assert.Multiple(() =>
        {
            Assert.Throws<InvalidOperationException>(() => Measure(() => throw new InvalidOperationException("native runtime")));
            Assert.Throws<OperationCanceledException>(() => Measure(() => throw new OperationCanceledException()));
        });
    }

    [Test]
    public void MissingComparableTokenCountLeavesOverheadUnknown()
    {
        var result = Measure(() => new(true, null, null,
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(15), 0));

        Assert.That(result.OverheadUsPerToken, Is.Null);
    }

    private static GuidanceSmokeResult Measure(Func<GuidanceSmokeMeasurement> observation) =>
        GuidanceSmoke.Measure("caller prompt", "caller lark grammar", (prompt, grammar) =>
        {
            Assert.That(prompt, Is.EqualTo("caller prompt"));
            Assert.That(grammar, Is.EqualTo("caller lark grammar"));
            return observation();
        });
}
