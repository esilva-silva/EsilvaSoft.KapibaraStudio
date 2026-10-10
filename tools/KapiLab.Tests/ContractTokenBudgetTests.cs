using EsilvaSoft.KapibaraStudio.KapiLab.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class ContractTokenBudgetTests
{
    [Test]
    public void EffectiveContextMatchesIdePackageWindowFormula()
    {
        Assert.That(ContractTokenBudget.EffectiveContextTokens(2048, 32, 2048, 32), Is.EqualTo(2013));
    }

    [Test]
    public void RequestedContextAndPackageCompletionLimitBothConstrainBudget()
    {
        Assert.That(ContractTokenBudget.EffectiveContextTokens(512, 64, 8192, 24), Is.EqualTo(512));
    }

    [Test]
    public void EffectiveContextUsesTheSameMinimumFloorAsTheIde()
    {
        Assert.That(ContractTokenBudget.EffectiveContextTokens(2048, 32, 50, 32), Is.EqualTo(64));
    }

    [Test]
    public void InvalidBudgetsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractTokenBudget.EffectiveContextTokens(63, 32, 2048, 32));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContractTokenBudget.EffectiveContextTokens(2048, 0, 2048, 32));
    }
}
