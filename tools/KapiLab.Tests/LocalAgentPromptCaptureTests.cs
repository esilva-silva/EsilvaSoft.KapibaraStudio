using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.Core.Agents;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class LocalAgentPromptCaptureTests
{
    private static LocalModelDefinition Model() => new("fixture", "fixture", Path.GetFullPath("tmp/synthetic-agent-model"), "Qwen3")
    {
        PromptFormat = LocalModelPromptFormats.Qwen3ChatMlTools,
        Capabilities = LocalModelCapabilities.Chat | LocalModelCapabilities.Agent | LocalModelCapabilities.Tools | LocalModelCapabilities.Reasoning,
        Metadata = new()
        {
            Agent = new(LocalModelPromptFormats.Qwen3ChatMlTools, "chat_template.jinja", "qwen-hermes-json", "kapibara-tools-2026-10", 12, 6144, null),
            Reasoning = new(true, "qwen3-think", 151667, 151668, true, "hard", Qwen3ChatMlFallbackRenderer.DisabledReasoningSuffix,
                new(1, 1, 128), 128, "force-close", LocalAgentPromptBuilder.ReferenceForceCloseText, "strip-previous-turns", "fixture"),
        },
    };
    private static LocalAgentProvider Provider(LocalModelDefinition model, RecordingModelService recorder) => new(recorder,
        new RecordingAutocompleteService(new() { ModelPath = model.Path, Mode = AutocompleteMode.Ai, ChatEnabled = true }));
    private static AgentTurnRequest Turn(string message) => new(AgentTurnId.New(), message, "fixture-tab", 1)
    {
        Permissions = new() { ProviderId = "local" }, Plan = new(AgentOperationMode.Planning, [], [], [], [], AgentProposalHandling.Disabled, false, AgentConfirmationCategories.None),
    };
    private static async Task<List<AgentProviderEvent>> Run(IAgentSession session, string message, CancellationToken token = default)
    {
        var events = new List<AgentProviderEvent>();
        await foreach (var item in session.RunTurnAsync(Turn(message), token)) events.Add(item);
        return events;
    }

    [Test]
    public async Task CapturesRealProviderStepsAndNextTurnHasNoPreviousReasoning()
    {
        var model = Model();
        var recorder = new RecordingModelService(model)
        {
            Chunks = index => index == 0 ? [new("<think>", 1, false) { TokenId = 151667 },
                new("TRANSIENT_CANARY", 2, false) { TokenId = 42 }] : [new("Resposta sintética.", 1, true)],
        };
        await using var session = await Provider(model, recorder).CreateSessionAsync(new("local"), CancellationToken.None);
        var first = await Run(session, "Primeiro turno");
        var second = await Run(session, "Segundo turno");
        Assert.Multiple(() =>
        {
            Assert.That(first.Any(e => e.Kind == AgentEventKind.AgentError), Is.False);
            Assert.That(second.Any(e => e.Kind == AgentEventKind.AgentError), Is.False);
            Assert.That(recorder.Captured, Has.Count.EqualTo(3));
            Assert.That(recorder.Captured[1].Messages.Any(m => m.Content.Contains("TRANSIENT_CANARY", StringComparison.Ordinal)), Is.True);
            Assert.That(recorder.Captured[1].DisableReasoning, Is.True);
            Assert.That(recorder.Captured[2].Messages.Any(m => m.Content.Contains("TRANSIENT_CANARY", StringComparison.Ordinal)), Is.False);
            Assert.That(recorder.Captured[2].Messages.Any(m => m.Content.Contains("<think>", StringComparison.Ordinal)), Is.False);
        });
        var expected = LocalAgentPromptBuilder.Build(new(null, "Segundo turno", null, [], [],
            recorder.Captured[2].Messages.Skip(1).ToArray(), true));
        Assert.That(expected.Messages, Is.EqualTo(recorder.Captured[2].Messages));
    }

    [Test]
    public async Task FailedTurnIsRemovedBeforeRecovery()
    {
        var model = Model();
        var recorder = new RecordingModelService(model)
        { BeforeStream = (index, _) => index == 0 ? Task.FromException(new InvalidOperationException("fixture")) : Task.CompletedTask };
        await using var session = await Provider(model, recorder).CreateSessionAsync(new("local"), CancellationToken.None);
        var failed = await Run(session, "FAILED_TURN_CANARY");
        var recovered = await Run(session, "Recuperação");
        Assert.That(failed.Any(e => e.Kind == AgentEventKind.AgentError), Is.True);
        Assert.That(recovered.Any(e => e.Kind == AgentEventKind.AgentError), Is.False);
        Assert.That(recorder.Captured[1].Messages.Any(m => m.Content.Contains("FAILED_TURN_CANARY", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public async Task ConcurrentTurnsAreSerializedAndCancellationDoesNotPoisonNextTurn()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = Model();
        var recorder = new RecordingModelService(model)
        { BeforeStream = async (index, token) => { if (index == 0) { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); } } };
        await using var session = await Provider(model, recorder).CreateSessionAsync(new("local"), CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        var first = Run(session, "CANCELLED_TURN_CANARY", cancelled.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Run(session, "Turno concorrente");
        Assert.That(recorder.Captured, Has.Count.EqualTo(1));
        cancelled.Cancel();
        await first.WaitAsync(TimeSpan.FromSeconds(5));
        var events = await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.That(events.Any(e => e.Kind == AgentEventKind.AgentError), Is.False);
        Assert.That(recorder.Captured, Has.Count.EqualTo(2));
        Assert.That(recorder.Captured[1].Messages.Any(m => m.Content.Contains("CANCELLED_TURN_CANARY", StringComparison.Ordinal)), Is.False);
    }

    [Test]
    public async Task ProviderToolStepUsesSharedDescriptionAndPreservesRawAssistantAndBoundedResult()
    {
        var model = Model();
        var recorder = new RecordingModelService(model)
        { Chunks = index => index == 0 ? [new("Antes <tool_call>{\"name\":\"get_indexes\",\"arguments\":{}}</tool_call>", 1, true)]
            : [new("Resposta sintética.", 1, true)] };
        var provider = new LocalAgentProvider(recorder, new RecordingAutocompleteService(new()
        { ModelPath = model.Path, Mode = AutocompleteMode.Ai, ChatEnabled = true }), new RegistryStub());
        await using var session = await provider.CreateSessionAsync(new("local"), CancellationToken.None);
        var request = Turn("Índices sintéticos");
        request = request with { Plan = request.Plan! with { ProductTools = ["get_indexes"] } };
        await foreach (var item in session.RunTurnAsync(request, CancellationToken.None))
        {
            Assert.That(item.Kind, Is.Not.EqualTo(AgentEventKind.AgentError));
            if (item.Kind == AgentEventKind.ToolRequested)
                await session.SubmitToolResultAsync(new(default, request.TurnId, item.ToolCallId!.Value,
                    AgentToolResultStatus.Succeeded, "Fixture metadata", Truncated: true), CancellationToken.None);
        }
        Assert.That(recorder.Captured, Has.Count.EqualTo(2));
        using var tools = System.Text.Json.JsonDocument.Parse(recorder.Captured[0].ToolsJson!);
        Assert.That(tools.RootElement[0].GetProperty("function").GetProperty("description").GetString(),
            Is.EqualTo(AgentToolDescriptions.Get("get_indexes")));
        Assert.That(recorder.Captured[1].Messages.Single(m => m.Role == "assistant").Content,
            Is.EqualTo("Antes <tool_call>{\"name\":\"get_indexes\",\"arguments\":{}}</tool_call>"));
        Assert.That(recorder.Captured[1].Messages.Single(m => m.Role == "tool").Content, Does.Contain("\"truncated\":true"));
        Assert.That(recorder.Captured[1].Messages[^1].Role, Is.EqualTo("user"));
    }

    private sealed class RegistryStub : IAgentToolRegistry
    {
        public IReadOnlyList<AgentToolDescriptor> GetDescriptors() => GetInProcessDescriptors("local");
        public IReadOnlyList<AgentToolDescriptor> GetInProcessDescriptors(string providerId) =>
            providerId == "local" ? [new("get_indexes", 1, AgentToolRisk.ReadOnly, [AgentPermission.ReadMetadata])] : [];
        public IReadOnlyList<AgentToolDescriptor> GetSessionChannelDescriptors(string providerId) => [];
        public AgentToolDescriptor? FindDescriptor(string? name) => GetDescriptors().FirstOrDefault(t => t.Name == name);
        public string? GetInputSchemaJson(string? name) => name == "get_indexes" ? "{\"type\":\"object\",\"properties\":{}}" : null;
        public string? GetInProcessInputSchemaJson(string providerId, string? name) => providerId == "local" ? GetInputSchemaJson(name) : null;
        public string? GetOutputSchemaJson(string? name) => null;
        public string? GetSessionChannelInputSchemaJson(string providerId, string? name) => null;
        public string? GetSessionChannelOutputSchemaJson(string providerId, string? name) => null;
        public Task<AgentToolInvocationResult> InvokeAsync(AgentPrincipal? principal, AgentInvocationContext? invocationContext,
            AgentOutputDestination? destination, AgentOutputDataScope? outputDataScope, string? name, string? argumentsJson,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingAutocompleteService(AutocompleteSettings settings) : IAutocompleteService
    {
        public AutocompleteSettings Settings { get; } = settings;
        public LocalModelStatus Status { get; } = new(LocalModelState.Available, "fixture");
        public event EventHandler? SettingsChanged { add { } remove { } }
        public Task ConfigureAsync(AutocompleteSettings value, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult<AutocompleteResult?>(null);
        public Task<LocalModelStatus> TestModelAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
    }

    private sealed class RecordingModelService(LocalModelDefinition model) : ILocalAiModelService
    {
        public string DefaultDirectory => Path.GetDirectoryName(model.Path)!;
        public LocalModelStatus Status { get; } = new(LocalModelState.Available, "fixture");
        public LocalModelDefinition? LoadedModel => model;
        public List<ModelChatPrompt> Captured { get; } = [];
        public Func<int, CancellationToken, Task> BeforeStream { get; set; } = (_, _) => Task.CompletedTask;
        public Func<int, IEnumerable<GeneratedChunk>> Chunks { get; set; } = _ => [new("Resposta sintética.", 1, true)];
        public event EventHandler? StatusChanged { add { } remove { } }
        public Task<IReadOnlyList<LocalModelValidation>> DiscoverModelsAsync(string? directory = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LocalModelValidation>>([]);
        public Task<LocalModelValidation> ValidateModelAsync(string path, CancellationToken cancellationToken = default) =>
            Task.FromResult(PathEquals(path, model.Path)
                ? new LocalModelValidation(model, new LocalModelStatus(LocalModelState.Available, "fixture"))
                : new LocalModelValidation(null, new LocalModelStatus(LocalModelState.NotInstalled, "fixture")));
        public Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AiHardwareDevice>>([]);
        public LocalModelCapabilities GetCapabilities() => model.Capabilities;
        public Task<LocalModelDefinition> LoadModelAsync(LocalModelRole role, AutocompleteSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(model);
        public Task UnloadModelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SwitchModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalModelGeneration> GenerateAsync(LocalModelRole role, AutocompleteSettings settings,
            Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority,
            AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The recorder captures StreamAsync only; inference is never performed.");
        public async IAsyncEnumerable<GeneratedChunk> StreamAsync(LocalModelRole role, AutocompleteSettings settings,
            Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority,
            AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = Captured.Count;
            Captured.Add(request(model).ChatPrompt!);
            await BeforeStream(index, cancellationToken);
            await Task.Yield();
            foreach (var chunk in Chunks(index)) yield return chunk;
        }
        public void CancelGeneration() { }
        public Task<LocalModelTestReport> TestModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LocalModelTestReport(false, "recorder fixture; no inference", []));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static bool PathEquals(string left, string right) => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
