using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Separação autocomplete normal × autocomplete com IA: o ghost determinístico segue só <c>Enabled</c>, a IA segue só
/// <c>AiCompletionEnabled</c> (atalho explícito) e as flags legadas <c>Inline*</c> são lidas, preservadas e ignoradas.
/// Campo ausente e false explícito continuam fatos diferentes, distinguidos pela presença no JSON.
/// </summary>
[TestFixture]
public sealed class InlineCompletionSettingsTests
{
    private static AutocompleteSettings Read(string json) =>
        JsonSerializer.Deserialize<AutocompleteSettings>(json) ?? throw new InvalidDataException("json inválido");

    [Test]
    public void NewInstallationHasTheGhostAndTheExplicitAi()
    {
        var settings = new AutocompleteSettings();

        Assert.Multiple(() =>
        {
            Assert.That(InlineCompletionPolicy.TraditionalInline(settings), Is.True);
            Assert.That(InlineCompletionPolicy.AiCompletion(settings), Is.True);
            Assert.That(settings.AiCompletionEnabledValue, Is.Null, "Nada é materializado enquanto o usuário não escolhe.");
        });
    }

    [TestCase(true, true, true, true)]
    [TestCase(true, false, true, false)]
    [TestCase(false, true, false, true)]
    [TestCase(false, false, false, false)]
    public void NormalAndAiAutocompleteAreIndependent(bool enabled, bool aiEnabled, bool ghost, bool ai)
    {
        var settings = new AutocompleteSettings { Enabled = enabled, AiCompletionEnabled = aiEnabled };

        Assert.Multiple(() =>
        {
            Assert.That(InlineCompletionPolicy.TraditionalInline(settings), Is.EqualTo(ghost));
            Assert.That(InlineCompletionPolicy.AnyInline(settings), Is.EqualTo(ghost));
            Assert.That(InlineCompletionPolicy.AiCompletion(settings), Is.EqualTo(ai));
        });
    }

    [Test]
    public void LegacyInlineFlagsNoLongerControlTheGhost()
    {
        var settings = Read("""{"Version":1,"UseDictionary":false,"InlineEnabled":false,"InlineUseTraditional":false,"InlineUseAi":true}""");

        Assert.Multiple(() =>
        {
            Assert.That(InlineCompletionPolicy.TraditionalInline(settings), Is.True, "O ghost do autocomplete normal segue só Enabled.");
            Assert.That((settings.InlineEnabledValue, settings.InlineUseTraditionalValue, settings.InlineUseAiValue),
                Is.EqualTo(((bool?)false, (bool?)false, (bool?)true)), "Os valores legados são preservados para compatibilidade de sessão.");
        });
    }

    [TestCase(AutocompleteMode.Automatic, true)]
    [TestCase(AutocompleteMode.Ai, true)]
    [TestCase(AutocompleteMode.Basic, false)]
    public void AbsentAiFlagMigratesFromTheLegacyMode(AutocompleteMode mode, bool expected)
    {
        var settings = Read("{\"Version\":1,\"Mode\":" + (int)mode + "}");

        Assert.Multiple(() =>
        {
            Assert.That(settings.AiCompletionEnabledValue, Is.Null, "A ausência é preservada como ausência.");
            Assert.That(settings.AiCompletionEnabled, Is.EqualTo(expected));
            Assert.That(settings.LocalAgentEnabled, Is.EqualTo(expected), "Básico desligava também o agente.");
        });
        settings.Validate();
    }

    [Test]
    public void ExplicitAiFlagWinsOverTheLegacyMode()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Read("""{"Version":1,"Mode":1,"AiCompletionEnabled":true}""").AiCompletionEnabled, Is.True);
            Assert.That(Read("""{"Version":1,"Mode":0,"AiCompletionEnabled":false}""").AiCompletionEnabled, Is.False);
        });
    }

    [TestCase(true, true, true)]
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    [TestCase(false, false, false)]
    public void TheModelStaysInUseWhileTheAiOrTheAgentNeedsIt(bool ai, bool agent, bool expected)
    {
        var settings = new AutocompleteSettings { AiCompletionEnabled = ai, ChatEnabled = agent };

        Assert.That(settings.UsesLocalModel, Is.EqualTo(expected));
    }

    [Test]
    public void RoundTripKeepsAbsenceAbsentAndChoicesExplicit()
    {
        var absent = JsonSerializer.Serialize(new AutocompleteSettings());
        Assert.That(absent, Does.Not.Contain("AiCompletionEnabled").And.Not.Contain("InlineUseAi"), "Nada é gravado enquanto o usuário não escolhe.");
        Assert.That(Read(absent), Is.EqualTo(new AutocompleteSettings()));

        var chosen = new AutocompleteSettings { AiCompletionEnabled = false, InlineUseAi = true };
        var json = JsonSerializer.Serialize(chosen);
        Assert.That(json, Does.Contain("\"AiCompletionEnabled\":false").And.Contain("\"InlineUseAi\":true"));
        Assert.That(Read(json), Is.EqualTo(chosen));
    }

    [Test]
    public void InvalidConfigurationStillFailsVisiblyInsteadOfBecomingAnEmptyOne()
    {
        var invalid = Read("""{"Version":1,"DelayMilliseconds":10,"AiCompletionEnabled":true}""");

        Assert.That(() => invalid.Validate(), Throws.ArgumentException);
    }
}
