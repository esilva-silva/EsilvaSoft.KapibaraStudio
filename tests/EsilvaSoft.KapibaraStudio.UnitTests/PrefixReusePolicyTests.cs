using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

/// <summary>
/// KS-US03: gate de reuso de KV por sessão. A decisão é pura, então é verificada sem runtime nativo; a equivalência
/// numérica da geração continua exigindo prova com modelo real por pacote.
/// </summary>
[TestFixture]
public sealed class PrefixReusePolicyTests
{
    private static readonly PrefixCacheState Session = new() { SessionKey = "agent-turn:abc", Mode = PrefixCacheMode.Reuse };
    private static readonly ModelChatPrompt Chat = new([new("system", "s"), new("user", "u")]);

    private static ModelGenerationRequest Autocomplete(double temperature = 0) =>
        new("db.", "", 512, 8) { Temperature = temperature, PrefixCache = Session };

    private static ModelGenerationRequest AgentStep(double temperature = 0.6, double? topP = 0.95, int? topK = 20, int? seed = null) =>
        new("", "", 8192, 64) { Temperature = temperature, TopP = topP, TopK = topK, Seed = seed, ChatPrompt = Chat, PrefixCache = Session };

    private static bool Eligible(ModelGenerationRequest request, AiAccelerationMode provider = AiAccelerationMode.Cpu,
        PrefixCacheMode? settings = PrefixCacheMode.Reuse, bool validated = true, bool lab = false) =>
        PrefixReusePolicy.IsEligible(provider, request, settings, validated, lab);

    [Test]
    public void AgentStepsMayReuseWhileSamplingButAutocompleteOnlyWhenGreedy()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Eligible(AgentStep()), Is.True, "Amostragem só afeta o que vem depois do prefixo reaproveitado.");
            Assert.That(Eligible(AgentStep(temperature: 0, topP: null, topK: null)), Is.True);
            Assert.That(Eligible(Autocomplete()), Is.True);
            Assert.That(Eligible(Autocomplete(temperature: 0.2)), Is.False, "O autocomplete continua restrito à geração gulosa.");
        });
    }

    [Test]
    public void TheProductGateIsTheSameForEveryUse()
    {
        Assert.Multiple(() =>
        {
            foreach (var request in new[] { AgentStep(), Autocomplete() })
            {
                Assert.That(Eligible(request, provider: AiAccelerationMode.Gpu), Is.False, "Só CPU.");
                Assert.That(Eligible(request, provider: AiAccelerationMode.Auto), Is.False);
                Assert.That(Eligible(request, settings: PrefixCacheMode.Disabled), Is.False, "Exige opt-in nas preferências.");
                Assert.That(Eligible(request, settings: null), Is.False);
                Assert.That(Eligible(request, validated: false), Is.False, "Exige homologação declarada no pacote.");
                Assert.That(Eligible(request, validated: false, lab: true), Is.True, "Só o laboratório avalia pacote não homologado.");
                Assert.That(Eligible(request with { PrefixCache = null }), Is.False);
                Assert.That(Eligible(request with { PrefixCache = Session with { Mode = PrefixCacheMode.Disabled } }), Is.False);
                Assert.That(Eligible(request with { PrefixCache = Session with { SessionKey = "" } }), Is.False);
            }
        });
    }

    [Test]
    public void AgentStepsWithDifferentSamplingNeverShareAGenerator()
    {
        var thinking = PrefixReusePolicy.SessionKey(AgentStep());
        Assert.Multiple(() =>
        {
            Assert.That(PrefixReusePolicy.SessionKey(AgentStep()), Is.EqualTo(thinking), "Mesmo turno e mesma amostragem: mesmo gerador.");
            Assert.That(PrefixReusePolicy.SessionKey(AgentStep(temperature: 0.7, topP: 0.8)), Is.Not.EqualTo(thinking),
                "O gerador conserva a amostragem com que nasceu; o passo sem raciocínio usa outro.");
            Assert.That(PrefixReusePolicy.SessionKey(AgentStep(topK: 40)), Is.Not.EqualTo(thinking));
            Assert.That(PrefixReusePolicy.SessionKey(AgentStep(seed: 7)), Is.Not.EqualTo(thinking));
            Assert.That(thinking, Does.StartWith(Session.SessionKey));
            Assert.That(PrefixReusePolicy.SessionKey(Autocomplete()), Is.EqualTo(Session.SessionKey),
                "A chave do autocomplete não muda: sessões existentes continuam válidas.");
            Assert.That(PrefixReusePolicy.SessionKey(Autocomplete() with { PrefixCache = null }), Is.Empty);
        });
    }
}
