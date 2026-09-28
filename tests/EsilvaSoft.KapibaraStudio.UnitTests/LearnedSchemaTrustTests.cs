using EsilvaSoft.KapibaraStudio.Application.SchemaLearning;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class LearnedSchemaTrustTests
{
    private static readonly Guid GenerationA = Guid.NewGuid();
    private static readonly Guid GenerationB = Guid.NewGuid();

    [Test]
    public void SameGenerationConfirmedSessionIsCurrentConfirmed()
    {
        var trust = LearnedSchemaTrust.Compute(GenerationA, GenerationA, sessionConfirmed: true);
        Assert.Multiple(() =>
        {
            Assert.That(trust.Origin, Is.EqualTo(LearnedSchemaOrigin.Current));
            Assert.That(trust.Session, Is.EqualTo(LearnedSchemaSessionState.Confirmed));
            Assert.That(trust.IsServable, Is.True);
            Assert.That(trust.IsHistorical, Is.False, "Servido como evidência normal, não histórica.");
        });
    }

    [Test]
    public void SameGenerationUnconfirmedSessionIsCurrentUnconfirmedAndStillServable()
    {
        var trust = LearnedSchemaTrust.Compute(GenerationA, GenerationA, sessionConfirmed: false);
        Assert.Multiple(() =>
        {
            Assert.That(trust.Origin, Is.EqualTo(LearnedSchemaOrigin.Current));
            Assert.That(trust.Session, Is.EqualTo(LearnedSchemaSessionState.Unconfirmed));
            Assert.That(trust.IsServable, Is.True, "Current+Unconfirmed ainda é servido, marcado como histórico.");
            Assert.That(trust.IsHistorical, Is.True);
        });
    }

    [Test]
    public void DifferentGenerationIsSupersededAndNeverServable()
    {
        var trust = LearnedSchemaTrust.Compute(GenerationA, GenerationB, sessionConfirmed: true);
        Assert.Multiple(() =>
        {
            Assert.That(trust.Origin, Is.EqualTo(LearnedSchemaOrigin.Superseded));
            Assert.That(trust.IsServable, Is.False);
            Assert.That(trust.IsHistorical, Is.False, "Superseded não é 'histórico servido': não é servido de forma alguma.");
        });
    }

    [Test]
    public void UnknownObservedGenerationIsNotContradictedAndStaysCurrent()
    {
        // A namespace observed before generations existed (L14-a) cannot be proven to describe another origin.
        var trust = LearnedSchemaTrust.Compute(null, GenerationA, sessionConfirmed: false);
        Assert.That(trust.Origin, Is.EqualTo(LearnedSchemaOrigin.Current));
        Assert.That(trust.IsHistorical, Is.True);
    }

    [Test]
    public void UnknownCurrentGenerationIsNotContradictedAndStaysCurrent()
    {
        // A profile never persisted with a generation (pre L14-a) cannot contradict an observed one either.
        var trust = LearnedSchemaTrust.Compute(GenerationA, null, sessionConfirmed: true);
        Assert.That(trust.Origin, Is.EqualTo(LearnedSchemaOrigin.Current));
    }

    [Test]
    public void BothGenerationsUnknownIsCurrent()
    {
        var trust = LearnedSchemaTrust.Compute(null, null, sessionConfirmed: false);
        Assert.That(trust.Origin, Is.EqualTo(LearnedSchemaOrigin.Current));
    }
}
