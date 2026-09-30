using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using System.Reflection;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Desktop.ViewModels;
using EsilvaSoft.KapibaraStudio.Infrastructure;

namespace EsilvaSoft.KapibaraStudio.UnitTests;

[TestFixture]
public sealed class AutocompleteReliabilityTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Test]
    public async Task CacheExpiresAndChangingSuffixRegenerates()
    {
        var clock = new Clock(); var runtime = new CompletionRuntimeFake();
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime, timeProvider: clock);
        var service = new AutocompleteService(ai, timeProvider: clock);
        var request = new AutocompleteRequest("db.", ";");
        await service.GetCompletionAsync(request);
        await service.GetCompletionAsync(request with { Suffix = ";\n}" });
        clock.Now = clock.Now.AddSeconds(31);
        await service.GetCompletionAsync(request);
        Assert.That(runtime.Generations, Is.EqualTo(3));
    }

    [Test]
    public async Task InferencesAreSerializedAndCanceledQueueDoesNotRun()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0; var maximum = 0;
        var runtime = new CompletionRuntimeFake { Handler = async (_, token) =>
        {
            active++; maximum = Math.Max(active, maximum); entered.TrySetResult();
            await release.Task.WaitAsync(token); active--;
            return new("find({})", 4, TimeSpan.Zero, "cpu");
        }};
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        var service = new AutocompleteService(ai);
        var first = service.GetCompletionAsync(new("db.", "1"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        using var cancellation = new CancellationTokenSource();
        var obsolete = service.GetCompletionAsync(new("db.", "2"), cancellation.Token);
        var latest = service.GetCompletionAsync(new("db.", "3"));
        cancellation.Cancel(); release.SetResult();
        await first; await latest;
        Assert.That(async () => await obsolete, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(maximum, Is.EqualTo(1)); Assert.That(runtime.Generations, Is.EqualTo(2));
    }

    [Test]
    public async Task DebounceCoalescesRequestsWithoutCancelingAnotherEditor()
    {
        var calls = 0;
        var service = new CompletionServiceFake { Handler = _ => { calls++; return Task.FromResult<AutocompleteResult?>(new("x", false, "")); } };
        using var first = new CompletionSession(); using var second = new CompletionSession();
        var obsolete = first.RequestAsync(service, new("c", ""));
        var latest = first.RequestAsync(service, new("co", ""));
        var other = second.RequestAsync(service, new("a", ""));
        Assert.That(await obsolete, Is.Null);
        Assert.That(await latest, Is.Not.Null); Assert.That(await other, Is.Not.Null);
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public async Task SaveFailureDoesNotApplyUnsavedSettingsAndRecoveryRetainsModelPath()
    {
        using var context = new WorkspaceTestContext();
        var service = new CompletionServiceFake();
        var failed = new FailingSessionRepository();
        using (var workspace = new WorkspaceViewModel(context.Workspace, failed, service))
        {
            await workspace.InitializeAsync();
            workspace.AutocompletePreferences.Enabled = false;
            await workspace.AutocompletePreferences.ApplyCommand.ExecuteAsync(null);
            Assert.That(workspace.AutocompletePreferences.OperationStatus, Does.Contain("não salvas"));
            Assert.That(service.Settings.Enabled, Is.True);
        }
        var settings = new AutocompleteSettings { ModelPath = "external-model", ContextTokens = 512 };
        await context.Repository.SaveSessionAsync(new() { Preferences = new() { Autocomplete = settings } });
        using var recovered = new WorkspaceViewModel(context.Workspace, context.Repository, service);
        await recovered.InitializeAsync();
        Assert.That(service.Settings, Is.EqualTo(settings));
    }

    [Test]
    public async Task ContextIsBoundedBeforeItReachesRuntime()
    {
        ModelGenerationRequest? received = null;
        var runtime = new CompletionRuntimeFake { Handler = (request, _) => { received = request; return Task.FromResult(new ModelGenerationResult("x", 1, TimeSpan.Zero, "cpu")); } };
        await using var ai = new AiAutocompleteProvider(new CompletionCatalogFake(), () => runtime);
        await new AutocompleteService(ai).GetCompletionAsync(new(new string('a', 100000) + "cursor", "suffix" + new string('b', 100000)));
        Assert.That(received!.Prefix, Has.Length.EqualTo(AutocompleteRequest.MaximumContextCharacters).And.EndsWith("cursor"));
        Assert.That(received.Suffix, Has.Length.EqualTo(AutocompleteRequest.MaximumContextCharacters).And.StartsWith("suffix"));
    }

    [Test]
    public void PrivacyCheckScansWorstCaseContextUnderCpuAndGcPressure()
    {
        // CI regression: the former 50 ms backtracking check timed out on the bounded context of a busy runner.
        var size = AutocompleteRequest.MaximumContextCharacters * 2;
        var inputs = new (string Text, bool Sensitive)[]
        {
            (new string('a', size), false),
            (string.Concat(Enumerable.Repeat("api_", size / 4)), false),
            (string.Concat(Enumerable.Repeat("-----BEGIN ", size / 11)), false),
            (new string('a', size - 12) + "api_key = x;", true),
            (new string(' ', size - 16) + "mongodb://host/", true)
        };
        using var pressure = new CpuAndGcPressure(Environment.ProcessorCount * 2);
        for (var round = 0; round < 10; round++)
            foreach (var (text, sensitive) in inputs)
                Assert.That(CompletionPrivacy.ContainsSensitiveText(text), Is.EqualTo(sensitive), $"rodada {round}, {text.Length} caracteres");
    }

    [Test]
    public void BasicCompletionReadsWordsAcrossTheWholeBoundedContext()
    {
        var request = new AutocompleteRequest(new string('.', AutocompleteRequest.MaximumContextCharacters - 20) + "clienteAtivo = 1;\nclie", "").Bounded();
        Assert.That(BasicAutocompleteProvider.GetCompletion(request)?.Text, Is.EqualTo("nteAtivo"));
    }

    private sealed class CpuAndGcPressure : IDisposable
    {
        private readonly Thread[] _threads;
        private volatile bool _stop;

        public CpuAndGcPressure(int threads)
        {
            _threads = Enumerable.Range(0, threads).Select(_ => new Thread(() =>
            {
                long iteration = 0;
                while (!_stop) if ((++iteration & 0x3FFF) == 0) GC.KeepAlive(new byte[4_000_000]);
            }) { IsBackground = true }).ToArray();
            foreach (var thread in _threads) thread.Start();
        }

        public void Dispose()
        {
            _stop = true;
            foreach (var thread in _threads) thread.Join();
        }
    }
}
