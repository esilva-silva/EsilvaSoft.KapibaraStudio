using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.Infrastructure.Agents.OpenAi;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.IntegrationTests.OpenAi;

/// <summary>
/// Real-service homologation, never part of the ordinary run. Requires an authorized key in
/// <c>SLOP_OPENAI_API_KEY</c> and a model in <c>SLOP_OPENAI_MODEL</c>; sends only a synthetic prompt, no tools, no
/// MongoDB data. The key is read in-process and is never printed. Without both variables the test is ignored.
/// </summary>
[TestFixture]
[Explicit("Chamada paga à OpenAI API; somente com credencial autorizada (SLOP_OPENAI_API_KEY, SLOP_OPENAI_MODEL).")]
[Category("OpenAiReal")]
[Category("Integration")]
[Category("ExternalService")]
public sealed class OpenAiRealServiceTests
{
    [Test]
    public async Task SyntheticPromptStreamsTextFromTheRealService()
    {
        var key = Environment.GetEnvironmentVariable("SLOP_OPENAI_API_KEY");
        var model = Environment.GetEnvironmentVariable("SLOP_OPENAI_MODEL");
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(model))
        {
            Assert.Ignore("Homologação real pendente: SLOP_OPENAI_API_KEY/SLOP_OPENAI_MODEL ausentes.");
        }

        var provider = new OpenAiAgentProvider(new EnvironmentCredential(key!),
            new OpenAiAgentProviderOptions { MaxOutputTokensPerRequest = 64, MaxTokensPerTurn = 2_000 });
        await using var session = await provider.CreateSessionAsync(new(OpenAiAgentProvider.Id, model), CancellationToken.None);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));

        var events = await CollectAsync(session.RunTurnAsync(
            Turn("Responda apenas com a palavra: pronto"), timeout.Token), cancellationToken: timeout.Token);

        Assert.That(events.Where(static item => item.Kind == AgentEventKind.AgentError).Select(static item => item.Text), Is.Empty);
        Assert.That(Text(events), Is.Not.Empty);
        Assert.That(events.Any(item => (item.Text ?? string.Empty).Contains(key!, StringComparison.Ordinal)), Is.False);
    }

    private sealed class EnvironmentCredential(string key) : IAgentCredentialProvider
    {
        public Task<SecretStoreResult<string>> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult(SecretStoreResults.Success(key));
    }

    private static AgentTurnRequest Turn(string message = "Quantas coleções existem?", string? context = null) =>
        new(AgentTurnId.New(), message, "tab-1", 1, context);

    private static async Task<List<AgentProviderEvent>> CollectAsync(
        IAsyncEnumerable<AgentProviderEvent> stream, Func<AgentProviderEvent, Task>? onEvent = null,
        CancellationToken cancellationToken = default)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in stream.WithCancellation(cancellationToken))
        {
            events.Add(item);
            if (onEvent is not null)
            {
                await onEvent(item);
            }
        }

        return events;
    }

    private static string Text(IEnumerable<AgentProviderEvent> events) =>
        string.Concat(events.Where(static item => item.Kind == AgentEventKind.MessageDelta).Select(static item => item.Text));
}
