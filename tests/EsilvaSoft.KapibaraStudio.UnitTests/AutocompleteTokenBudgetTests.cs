using EsilvaSoft.KapibaraStudio.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

public sealed class AutocompleteTokenBudgetTests
{
    [TestCase(2048, 128, 4096, 512, 2048)]
    [TestCase(4096, 1024, 4096, 512, 3581)]
    [TestCase(4096, 2048, 1024, 512, 509)]
    [TestCase(4096, 128, 80, 512, 64)]
    [Test]
    public void EffectiveContextTokensUsesRequestedAndModelLimits(int requestedContext, int requestedCompletion,
        int effectiveWindow, int modelCompletionMaximum, int expected)
    {
        var actual = AutocompleteTokenBudget.EffectiveContextTokens(requestedContext, requestedCompletion,
            effectiveWindow, modelCompletionMaximum);

        Assert.That(actual, Is.EqualTo(expected));
    }
}
