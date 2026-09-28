using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// Flags aditivas do autocompletar automático: precedência, padrões e migração da versão 1. Campo ausente e false
/// explícito são fatos diferentes, distinguidos pela presença no JSON e não pelo inicializador da propriedade.
/// </summary>
[TestFixture]
public sealed class InlineCompletionSettingsTests
{
    private static AutocompleteSettings Read(string json) =>
        JsonSerializer.Deserialize<AutocompleteSettings>(json) ?? throw new InvalidDataException("json inválido");

    [Test]
    public void NewInstallationKeepsAutomaticAiOff()
    {
        var settings = new AutocompleteSettings();

        Assert.Multiple(() =>
        {
            Assert.That(settings.InlineEnabled, Is.True);
            Assert.That(settings.InlineUseTraditional, Is.True);
            Assert.That(settings.InlineUseAi, Is.False, "Processamento automático de IA é opt-in explícito.");
            Assert.That(InlineCompletionPolicy.TraditionalInline(settings), Is.True);
            Assert.That(InlineCompletionPolicy.AiInline(settings), Is.False);
        });
    }

    // InlineEnabled, InlineUseTraditional, InlineUseAi -> tradicional automático, IA automática
    [TestCase(true, true, true, true, true)]
    [TestCase(true, true, false, true, false)]
    [TestCase(true, false, true, false, true)]
    [TestCase(true, false, false, false, false)]
    [TestCase(false, true, true, false, false)]
    public void PrecedenceFollowsTheConfiguredFlags(bool inlineEnabled, bool useTraditional, bool useAi, bool traditional, bool ai)
    {
        var settings = new AutocompleteSettings
        {
            InlineEnabled = inlineEnabled,
            InlineUseTraditional = useTraditional,
            InlineUseAi = useAi
        };

        Assert.Multiple(() =>
        {
            Assert.That(InlineCompletionPolicy.TraditionalInline(settings), Is.EqualTo(traditional));
            Assert.That(InlineCompletionPolicy.AiInline(settings), Is.EqualTo(ai));
        });
    }

    [Test]
    public void TurningEverythingOffAtTheTopSuspendsBothGenerators()
    {
        var settings = new AutocompleteSettings { Enabled = false, InlineUseAi = true };

        Assert.Multiple(() =>
        {
            Assert.That(InlineCompletionPolicy.TraditionalInline(settings), Is.False);
            Assert.That(InlineCompletionPolicy.AiInline(settings), Is.False);
            Assert.That(InlineCompletionPolicy.AnyInline(settings), Is.False);
        });
    }

    [Test]
    public void BasicModeKeepsTheDeterministicGhostAndRefusesAutomaticInference()
    {
        var settings = new AutocompleteSettings { Mode = AutocompleteMode.Basic, InlineUseAi = true };

        Assert.Multiple(() =>
        {
            Assert.That(InlineCompletionPolicy.TraditionalInline(settings), Is.True);
            Assert.That(InlineCompletionPolicy.AiInline(settings), Is.False);
        });
    }

    [Test]
    public void LegacyVersionOneDocumentWithoutTheFlagsIsMigratedFromTheStoredIntent()
    {
        var settings = Read("""{"Version":1,"Enabled":true,"Mode":2,"UseDictionary":true,"DelayMilliseconds":150}""");

        Assert.Multiple(() =>
        {
            Assert.That(settings.InlineEnabledValue, Is.Null, "A ausência é preservada como ausência.");
            Assert.That(settings.InlineUseTraditionalValue, Is.Null);
            Assert.That(settings.InlineUseAiValue, Is.Null);
            Assert.That(settings.InlineEnabled, Is.True);
            Assert.That(settings.InlineUseTraditional, Is.True);
            Assert.That(settings.InlineUseAi, Is.False, "Documento antigo não ganha inferência automática sem opt-in.");
            Assert.That(settings.Enabled, Is.True);
            Assert.That(settings.Mode, Is.EqualTo(AutocompleteMode.Ai));
        });
        settings.Validate();
    }

    [Test]
    public void LegacyDocumentWithoutDictionaryDerivesTheTraditionalGhostFromIt()
    {
        var settings = Read("""{"Version":1,"UseDictionary":false}""");

        Assert.Multiple(() =>
        {
            Assert.That(settings.InlineUseTraditionalValue, Is.Null);
            Assert.That(settings.InlineUseTraditional, Is.False);
            Assert.That(InlineCompletionPolicy.TraditionalInline(settings), Is.False);
            Assert.That(settings.InlineEnabled, Is.True, "Desligar o dicionário legado não desliga o preemptivo geral.");
        });
    }

    [Test]
    public void ExplicitFalseIsObeyedAndNeverConfusedWithAnAbsentField()
    {
        var settings = Read("""{"Version":1,"UseDictionary":true,"InlineEnabled":false,"InlineUseTraditional":false,"InlineUseAi":false}""");

        Assert.Multiple(() =>
        {
            Assert.That(settings.InlineEnabledValue, Is.False);
            Assert.That(settings.InlineUseTraditionalValue, Is.False);
            Assert.That(settings.InlineUseAiValue, Is.False);
            Assert.That(settings.InlineEnabled, Is.False);
            Assert.That(settings.InlineUseTraditional, Is.False, "False explícito vence o dicionário ligado.");
            Assert.That(InlineCompletionPolicy.AnyInline(settings), Is.False);
        });
    }

    [Test]
    public void ExplicitTrueSurvivesTheDictionaryBeingOff()
    {
        var settings = Read("""{"Version":1,"UseDictionary":false,"InlineUseTraditional":true}""");

        Assert.That(settings.InlineUseTraditional, Is.True);
        Assert.That(InlineCompletionPolicy.TraditionalInline(settings), Is.True);
    }

    [Test]
    public void RoundTripKeepsAbsenceAbsentAndChoicesExplicit()
    {
        var absent = JsonSerializer.Serialize(new AutocompleteSettings());
        Assert.That(absent, Does.Not.Contain("InlineUseAi"), "Nada é gravado enquanto o usuário não escolhe.");
        Assert.That(Read(absent), Is.EqualTo(new AutocompleteSettings()));

        var chosen = new AutocompleteSettings { InlineUseAi = true, InlineUseTraditional = false };
        var json = JsonSerializer.Serialize(chosen);
        Assert.That(json, Does.Contain("\"InlineUseAi\":true"));
        Assert.That(Read(json), Is.EqualTo(chosen));
        Assert.That(Read(json).InlineUseTraditionalValue, Is.False);
    }

    [Test]
    public void InvalidConfigurationStillFailsVisiblyInsteadOfBecomingAnEmptyOne()
    {
        var invalid = Read("""{"Version":1,"DelayMilliseconds":10,"InlineEnabled":true}""");

        Assert.That(() => invalid.Validate(), Throws.ArgumentException);
    }
}
