using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core.Completion;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class DeepSeekIntegrationTests
{
    [TestCase("<|EOT|>")]
    public async Task ReservedMarkersNeverReachEditor(string marker)
    {
        var runtime = new CompletionRuntimeFake { Handler = (_, _) => Task.FromResult(new ModelGenerationResult(marker, 1, TimeSpan.Zero, "cpu")) };
        await using var provider = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        Assert.That(await provider.GetCompletionAsync(new("db.", ""), new(), cancellationToken: CancellationToken.None), Is.Null);
        Assert.That(await provider.GetCompletionAsync(new(marker, ""), new(), cancellationToken: CancellationToken.None), Is.Null);
        Assert.That(runtime.Generations, Is.EqualTo(1));
    }

    [Test]
    public async Task CancelingChatDoesNotCancelQueuedAutocomplete()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var runtime = new CompletionRuntimeFake { Handler = async (_, token) =>
        {
            if (Interlocked.Increment(ref calls) == 1) { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
            return new("find({})", 4, TimeSpan.Zero, "cpu");
        } };
        await using var provider = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var autocomplete = new AutocompleteService(provider);
        await autocomplete.ConfigureAsync(new() { ModelPath = "model" });
        using var cancellation = new CancellationTokenSource();
        // Pedido explícito do papel Chat, como o do LocalAgentProvider: prioridade interativa e token próprio.
        var pending = provider.Models.GenerateAsync(LocalModelRole.Chat, autocomplete.Settings,
            _ => new ModelGenerationRequest("/* Rewrite */ db.find({})", "", 2048, 64, RequireFullContext: true),
            AiRequestPriority.Interactive, cancellationToken: cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completion = autocomplete.GetCompletionAsync(new("db.", ""));
        cancellation.Cancel();
        Assert.That(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
        Assert.That((await completion.WaitAsync(TimeSpan.FromSeconds(5)))!.Text, Is.EqualTo("find({})"));
        Assert.That(runtime.Initializations, Is.EqualTo(1));
    }

}
