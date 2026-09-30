using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure;
using Jint;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class LocalAutocompleteTests
{
    private static readonly int[] PrefixMarker = [100001];
    [Test]
    public async Task AutomaticUsesAiAndBoundedCacheAvoidsRepeatedInference()
    {
        var runtime = new CompletionRuntimeFake();
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        var request = new AutocompleteRequest("db.", "", "javascript");
        Assert.That((await service.GetCompletionAsync(request))?.IsAi, Is.True);
        Assert.That((await service.GetCompletionAsync(request))?.Text, Is.EqualTo("collection.find({})"));
        Assert.That(runtime.Generations, Is.EqualTo(1));
        for (var i = 0; i < 65; i++) await service.GetCompletionAsync(request with { Suffix = i.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        await service.GetCompletionAsync(request);
        Assert.That(runtime.Generations, Is.EqualTo(67));
        Assert.That(runtime.Initializations, Is.EqualTo(1));
    }

    [TestCase(AutocompleteMode.Automatic)]
    [TestCase(AutocompleteMode.Ai)]
    public async Task RuntimeFailureFallsBackAndUsesCooldown(AutocompleteMode mode)
    {
        var runtime = new CompletionRuntimeFake { Handler = (_, _) => throw new InvalidDataException("private prompt must not escape") };
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        await service.ConfigureAsync(new() { Mode = mode });
        Assert.That(await service.GetCompletionAsync(new("db.", "")), Is.Null);
        await service.GetCompletionAsync(new("db.customers.", ""));
        var result = await service.GetCompletionAsync(new("const customer = 1; cust", ""));
        Assert.That(result, Is.EqualTo(new AutocompleteResult("omer", false, "Autocomplete básico local")));
        await service.GetCompletionAsync(new("cons", ""));
        Assert.That(runtime.Generations, Is.EqualTo(1));
        Assert.That(runtime.Disposed, Is.True);
        Assert.That(service.Status.Message, Does.Not.Contain("private prompt"));
    }

    [TestCase(false, AutocompleteMode.Automatic)]
    [TestCase(true, AutocompleteMode.Basic)]
    public async Task DisabledOrBasicNeverLoadsAi(bool enabled, AutocompleteMode mode)
    {
        var runtime = new CompletionRuntimeFake();
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        await service.ConfigureAsync(new() { Enabled = enabled, Mode = mode });
        var result = await service.GetCompletionAsync(new("cons", ""));
        Assert.That(runtime.Initializations, Is.Zero);
        Assert.That(result is not null, Is.EqualTo(enabled));
    }

    [TestCase("mongodb://user:password@host", "")]
    [TestCase("const password = 'abc';", "")]
    [TestCase("db.find({", "api_key: 'secret'})")]
    public async Task RecognizableSecretsPreventInference(string prefix, string suffix)
    {
        var runtime = new CompletionRuntimeFake();
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        await new AutocompleteService(ai).GetCompletionAsync(new(prefix, suffix));
        Assert.That(runtime.Initializations, Is.Zero);
    }

    [Test]
    public void FimPreservesNearCursorPrefixAndSuffixWithinTokenBudget()
    {
        var tokens = new QwenFimPromptBuilder().Build("abcdefghijklmnopqrstuvwxyz", "123456789", 15, new CompletionTokenizerFake());
        Assert.That(tokens, Is.EqualTo(PrefixMarker.Concat("rstuvwxyz".Select(c => (int)c)).Concat([100002]).Concat("123".Select(c => (int)c)).Concat([100003])));
        Assert.That(tokens, Has.Count.EqualTo(15));
    }

    [Test]
    public async Task CancellationDoesNotFallBackAndOtherEditorCanContinue()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CompletionRuntimeFake { Handler = async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return null!; } };
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        using var cancellation = new CancellationTokenSource();
        var first = service.GetCompletionAsync(new("db.", ""), cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        Assert.That(async () => await first, Throws.InstanceOf<OperationCanceledException>());
        runtime.Handler = (_, _) => Task.FromResult(new ModelGenerationResult("t value = 1;", 4, TimeSpan.Zero, "cpu"));
        Assert.That((await service.GetCompletionAsync(new("db.", "")))?.IsAi, Is.True);
        Assert.That(runtime.Initializations, Is.EqualTo(1));
    }

    [Test]
    public async Task ChangingSettingsCancelsInferenceAndUnloadsSession()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runtime = new CompletionRuntimeFake { Handler = async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return null!; } };
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        var first = service.GetCompletionAsync(new("db.", ""));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.ConfigureAsync(new() { Enabled = false }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(async () => await first, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(runtime.Disposed, Is.True);
        Assert.That(await service.GetCompletionAsync(new("cons", "")), Is.Null);
    }

    [Test]
    public async Task SessionRejectsLateResponseEvenIfProviderIgnoresCancellation()
    {
        var pending = new TaskCompletionSource<AutocompleteResult?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new CompletionServiceFake { Handler = _ => pending.Task };
        using var editor = new CompletionSession();
        var first = editor.RequestAsync(service, new("db.", ""), immediate: true);
        editor.Invalidate();
        pending.SetResult(new("old", true, ""));
        Assert.That(await first, Is.Null);
    }

    [TestCase(0, 32, 150)] [TestCase(2048, 0, 150)] [TestCase(2048, 32, 0)]
    public void InvalidBudgetsAreRejected(int context, int generated, int delay) =>
        Assert.Throws<ArgumentException>(() => new AutocompleteSettings { ContextTokens = context, MaximumCompletionTokens = generated, DelayMilliseconds = delay }.Validate());

}
