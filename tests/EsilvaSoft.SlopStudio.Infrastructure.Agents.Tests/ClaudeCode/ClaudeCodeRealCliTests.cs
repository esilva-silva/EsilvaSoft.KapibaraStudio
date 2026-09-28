using EsilvaSoft.SlopStudio.Application.Agents;
using EsilvaSoft.SlopStudio.Core.Agents;
using EsilvaSoft.SlopStudio.Infrastructure.Agents.ClaudeCode;
using NUnit.Framework;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Tests.ClaudeCode;

/// <summary>
/// Verificação opcional com o Claude Code REAL instalado e já logado pelo próprio usuário (nunca em CI). Faz no máximo
/// uma chamada ao modelo <c>haiku</c> com prompt sintético; consome a cota da assinatura. Não substitui a homologação
/// manual C-01..C-36 (GCL-8). Precisa de um ambiente sem as variáveis bloqueantes (ex.: fora de outra sessão Claude Code).
/// </summary>
[TestFixture]
[Explicit("Usa o Claude Code real e a assinatura do usuário (1 chamada haiku); somente execução manual autorizada.")]
[Category("ClaudeCodeReal")]
[CancelAfter(180_000)]
public sealed class ClaudeCodeRealCliTests
{
    [Test]
    public async Task RealCliAnswersOneSyntheticTurnThroughTheSubscription()
    {
        var provider = new ClaudeCodeAgentProvider(new ClaudeCodeAgentProviderOptions
        {
            AllowedModelIds = ["haiku"],
            DefaultModel = "haiku",
            MaxTurns = 1,
            ProbeTimeout = TimeSpan.FromSeconds(20),
        });
        var status = await provider.GetStatusAsync(CancellationToken.None);
        Assume.That(status.IsAvailable, Is.True, "Claude Code ausente, não logado por assinatura ou ambiente bloqueado: " + status.UnavailableCode);

        await using var session = (ClaudeCodeAgentSession)await provider.CreateSessionAsync(
            new AgentSessionOptions(ClaudeCodeAgentProvider.Id), CancellationToken.None);
        var events = await ClaudeCodeFixture.RunAsync(session, "Teste sintético do KapibaraStudio. Responda apenas: ok");

        Assert.Multiple(() =>
        {
            Assert.That(ClaudeCodeFixture.Error(events), Is.Null);
            Assert.That(ClaudeCodeFixture.Text(events).ToLowerInvariant(), Does.Contain("ok"));
            Assert.That(session.LastTurn!.Outcome, Is.EqualTo(AgentTurnOutcome.Completed));
        });
    }
}
