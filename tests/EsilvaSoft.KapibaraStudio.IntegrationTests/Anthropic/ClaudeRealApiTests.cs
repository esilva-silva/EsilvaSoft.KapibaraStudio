using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.Anthropic;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.Anthropic;

/// <summary>
/// Homologação real opcional. Só roda explicitamente e com <c>SLOP_ANTHROPIC_API_KEY</c> definida por quem autorizou
/// a conta; sem a variável o teste é ignorado. A chave vai direto ao provider por um credential provider em memória,
/// nunca ao cofre, disco ou saída do teste. Gera custo na conta do usuário.
/// </summary>
[TestFixture]
[Explicit("Chamada real à Claude API; exige SLOP_ANTHROPIC_API_KEY autorizada.")]
[Category("ClaudeReal")]
[Category("Integration")]
[Category("ExternalService")]
public sealed class ClaudeRealApiTests
{
    private sealed class EnvironmentCredential(string key) : IAgentCredentialProvider
    {
        public Task<SecretStoreResult<string>> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Success(key));
    }

    [Test]
    [CancelAfter(120_000)]
    public async Task RealStreamingTurnCompletes()
    {
        var key = Environment.GetEnvironmentVariable("SLOP_ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Assert.Ignore("SLOP_ANTHROPIC_API_KEY ausente: homologação real pendente.");
        }

        var model = Environment.GetEnvironmentVariable("SLOP_ANTHROPIC_MODEL") ?? ClaudeAgentProviderOptions.DefaultModelId;
        var options = new ClaudeAgentProviderOptions
        {
            ApiKeyReference = new SecretReference(Guid.NewGuid()),
            AllowedModelIds = [model],
            DefaultModel = model,
            Budget = ClaudeAgentBudget.Default with { MaxOutputTokensPerRequest = 256, MaxRequestsPerTurn = 1 },
        };
        using var provider = new ClaudeAgentProvider(new EnvironmentCredential(key!), options);
        await using var session = await provider.CreateSessionAsync(new AgentSessionOptions(ClaudeAgentProvider.Id), CancellationToken.None);
        var events = new List<AgentProviderEvent>();

        await foreach (var item in session.RunTurnAsync(new AgentTurnRequest(AgentTurnId.New(),
                           "Responda apenas com a palavra: pronto", "tab-real", 1), CancellationToken.None))
        {
            events.Add(item);
        }

        Assert.That(events.Where(static e => e.Kind == AgentEventKind.AgentError).Select(static e => e.Text), Is.Empty);
        Assert.That(events.Count(static e => e.Kind == AgentEventKind.MessageDelta), Is.GreaterThan(0));
    }
}
