using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AiHardwareTierTests
{
    private static readonly string[] TierLabels = ["Sem acelerador", "Básico", "Intermediário", "Avançado", "Alto", "Muito alto", "Workstation"];
    [TestCase(2, AiHardwareTier.NoAccelerator)]
    [TestCase(4, AiHardwareTier.Basic)]
    [TestCase(8, AiHardwareTier.Intermediate)]
    [TestCase(12, AiHardwareTier.Advanced)]
    [TestCase(16, AiHardwareTier.High)]
    [TestCase(24, AiHardwareTier.VeryHigh)]
    [TestCase(32, AiHardwareTier.Workstation)]
    public void ExactUpperBoundsUseTheExpectedTier(double gib, AiHardwareTier expected)
    {
        var bytes = (long)(gib * 1024 * 1024 * 1024);
        Assert.That(AiHardwareTiers.For(bytes), Is.EqualTo(expected));
    }

    [Test]
    public void SameGpuFamilyCanFallIntoDifferentTiersByVram()
    {
        Assert.That(AiHardwareTiers.For(6L * 1024 * 1024 * 1024), Is.EqualTo(AiHardwareTier.Intermediate));
        Assert.That(AiHardwareTiers.For(12L * 1024 * 1024 * 1024), Is.EqualTo(AiHardwareTier.Advanced));
    }

    [Test]
    public void ProfileDisplayIncludesMemoryAndTier()
    {
        var profile = new AiHardwareProfileSettings("AMD", "Radeon RX 7800 XT", 16L * 1024 * 1024 * 1024);
        Assert.That(profile.ToString(), Does.Contain("16 GiB").And.Contain("Alto"));
    }

    [Test]
    public void BuiltInProfilesCoverEveryTierWithRecommendedBudgets()
    {
        Assert.That(AiHardwareTiers.Presets.Select(profile => profile.TierLabel), Is.EqualTo(TierLabels));
        Assert.That(AiHardwareTiers.Recommendation(AiHardwareTier.High), Is.EqualTo((4096, 128)));
        Assert.That(AiHardwareTiers.Recommendation(AiHardwareTier.Workstation), Is.EqualTo((8192, 256)));
    }
}
