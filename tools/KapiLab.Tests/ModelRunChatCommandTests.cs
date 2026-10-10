using System.Runtime.CompilerServices;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.KapiLab.Cli;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using NUnit.Framework;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Tests;

[TestFixture]
public sealed class ModelRunChatCommandTests
{
    private static readonly LocalModelDefinition Definition = new("qwen3-test", "Qwen3", "package", "Qwen3")
    {
        PromptFormat = LocalModelPromptFormats.Qwen3ChatMlTools,
        Capabilities = LocalModelCapabilities.Chat,
        ContextLength = 4096,
    };

    private static readonly AutocompleteSettings Settings = new()
    {
        ModelPath = "package", ContextTokens = 4096, MaximumCompletionTokens = 128,
    };

    [Test]
    public void ParserAcceptsOnlyVersionedSingleUserMessageWithBoundedIdAndContent()
    {
        var record = ModelRunChatCommand.ParseLine("{\"schema\":\"kapilab-chat-input-v1\",\"id\":\"turno-1\",\"message\":\"Olá\"}");
        Assert.Multiple(() =>
        {
            Assert.That(record.Id, Is.EqualTo("turno-1"));
            Assert.That(record.Message, Is.EqualTo("Olá"));
        });
        Assert.Throws<InvalidDataException>(() => ModelRunChatCommand.ParseLine("{\"schema\":\"unknown\",\"id\":\"x\",\"message\":\"olá\"}"));
        Assert.Throws<InvalidDataException>(() => ModelRunChatCommand.ParseLine("{\"schema\":\"kapilab-chat-input-v1\",\"id\":\"x\",\"message\":\"olá\",\"tools\":[]}"));
        Assert.Throws<InvalidDataException>(() => ModelRunChatCommand.ParseLine("{\"schema\":\"kapilab-chat-input-v1\",\"id\":\"x\",\"id\":\"y\",\"message\":\"olá\"}"));
        Assert.Throws<InvalidDataException>(() => ModelRunChatCommand.ParseLine(JsonSerializer.Serialize(new
        {
            schema = ModelRunChatCommand.InputSchema, id = "x", message = new string('a', 8193),
        })));
        Assert.Throws<InvalidDataException>(() => ModelRunChatCommand.ParseLine(JsonSerializer.Serialize(new
        {
            schema = ModelRunChatCommand.InputSchema, id = new string('x', 129), message = "olá",
        })));
        Assert.Throws<InvalidDataException>(() => ModelRunChatCommand.ParseLine(new string('x', 16_385)));
        Assert.Throws<UnauthorizedAccessException>(() => ModelRunChatCommand.ParseLine("{\"schema\":\"kapilab-chat-input-v1\",\"id\":\"x\",\"message\":\"password=secret\"}"));
    }

    [Test]
    public void ContextBudgetIsCappedByTheModelAndDefaultsToItsEffectiveWindow()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ModelRunChatCommand.IsContextTokenRequestValid(null), Is.True);
            Assert.That(ModelRunChatCommand.IsContextTokenRequestValid(64), Is.True);
            Assert.That(ModelRunChatCommand.IsContextTokenRequestValid(AutocompleteSettings.AbsoluteContextMaximum), Is.True);
            Assert.That(ModelRunChatCommand.IsContextTokenRequestValid(63), Is.False);
            Assert.That(ModelRunChatCommand.IsContextTokenRequestValid(AutocompleteSettings.AbsoluteContextMaximum + 1), Is.False);
            Assert.That(ModelRunChatCommand.ResolveContextTokens(Definition, null), Is.EqualTo(4096));
            Assert.That(ModelRunChatCommand.ResolveContextTokens(Definition, 1024), Is.EqualTo(1024));
            Assert.That(ModelRunChatCommand.ResolveContextTokens(Definition, 8192), Is.EqualTo(4096));
        });
    }

    [Test]
    public async Task UsesChatRoleAndTypedPromptAndEmitsActualChunksAfterCompleteTurn()
    {
        var fake = new ChatServiceFake { Chunks = ["Olá", " mundo"] };
        using var output = new StringWriter();
        var explicitBudget = Settings with { ContextTokens = 1024 };
        var exit = await ModelRunChatCommand.RunRecordsAsync(fake, Definition, explicitBudget,
            [new("turno-1", "Pergunta")], output);
        var lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        using var first = JsonDocument.Parse(lines[0]);
        using var last = JsonDocument.Parse(lines[^1]);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(0));
            Assert.That(fake.Role, Is.EqualTo(LocalModelRole.Chat));
            Assert.That(fake.Priority, Is.EqualTo(AiRequestPriority.Interactive));
            Assert.That(fake.Request?.ChatPrompt?.Messages.Single().Role, Is.EqualTo("user"));
            Assert.That(fake.Request?.ChatPrompt?.Messages.Single().Content, Is.EqualTo("Pergunta"));
            Assert.That(fake.Request?.ChatPrompt?.ToolsJson, Is.Null);
            Assert.That(fake.Request?.ChatPrompt?.DisableReasoning, Is.False);
            Assert.That(fake.Request?.MaximumTokens, Is.EqualTo(128));
            Assert.That(fake.Request?.ContextTokens, Is.EqualTo(1024));
            Assert.That(first.RootElement.GetProperty("text").GetString(), Is.EqualTo("Olá"));
            Assert.That(last.RootElement.GetProperty("kind").GetString(), Is.EqualTo("final"));
            Assert.That(last.RootElement.GetProperty("contextWindowTokens").GetInt32(), Is.EqualTo(1024));
            Assert.That(last.RootElement.GetProperty("maximumCompletionTokens").GetInt32(), Is.EqualTo(128));
        });
    }

    [Test]
    public async Task SecretAcrossChunkBoundarySuppressesAllText()
    {
        var fake = new ChatServiceFake { Chunks = ["pass", "word=abc"] };
        using var output = new StringWriter();
        var exit = await ModelRunChatCommand.RunRecordsAsync(fake, Definition, Settings,
            [new("x", "Pergunta")], output);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(8));
            Assert.That(output.ToString(), Does.Contain("privacy_output"));
            Assert.That(output.ToString(), Does.Not.Contain("word=abc"));
        });
    }

    [Test]
    public async Task CancelledTurnEmitsOnlyErrorAndExitTwelve()
    {
        var fake = new ChatServiceFake();
        using var output = new StringWriter();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var exit = await ModelRunChatCommand.RunRecordsAsync(fake, Definition, Settings,
            [new("x", "Pergunta")], output, cancellation.Token);
        Assert.Multiple(() =>
        {
            Assert.That(exit, Is.EqualTo(12));
            Assert.That(output.ToString(), Does.Contain("cancelled"));
            Assert.That(output.ToString(), Does.Not.Contain("texto"));
        });
    }

    internal sealed class ChatServiceFake : ILocalAiModelService
    {
        public IReadOnlyList<string> Chunks { get; init; } = ["texto"];
        public bool Complete { get; init; } = true;
        public TimeSpan? FirstToken { get; init; }
        public LocalModelUnavailableReason? UnavailableReason { get; init; }
        public ModelGenerationRequest? Request { get; private set; }
        public LocalModelRole? Role { get; private set; }
        public AiRequestPriority? Priority { get; private set; }
        public string DefaultDirectory => "package";
        public LocalModelStatus Status => new(LocalModelState.Ready, "pronto");
        public LocalModelDefinition? LoadedModel => Definition;
        public event EventHandler? StatusChanged { add { } remove { } }
        public Task<IReadOnlyList<LocalModelValidation>> DiscoverModelsAsync(string? directory = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LocalModelValidation>>([]);
        public Task<LocalModelValidation> ValidateModelAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new LocalModelValidation(Definition, Status));
        public Task<IReadOnlyList<AiHardwareDevice>> GetAvailableHardwareAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AiHardwareDevice>>([]);
        public LocalModelCapabilities GetCapabilities() => Definition.Capabilities;
        public Task<LocalModelDefinition> LoadModelAsync(LocalModelRole role, AutocompleteSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(Definition);
        public Task UnloadModelAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SwitchModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<LocalModelGeneration> GenerateAsync(LocalModelRole role, AutocompleteSettings settings,
            Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority,
            AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public async IAsyncEnumerable<GeneratedChunk> StreamAsync(LocalModelRole role, AutocompleteSettings settings,
            Func<LocalModelDefinition, ModelGenerationRequest> request, AiRequestPriority priority,
            AiModelLoadPolicy load = AiModelLoadPolicy.LoadIfNeeded, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Role = role;
            Priority = priority;
            Request = request(Definition);
            if (UnavailableReason is { } reason)
                throw new LocalModelUnavailableException("recusa tipada") { UnavailableReason = reason };
            var count = 0;
            foreach (var text in Chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return new GeneratedChunk(text, ++count, false) { TokenId = count };
            }
            yield return new GeneratedChunk("", count, true)
            {
                Provider = "cpu", Elapsed = TimeSpan.FromMilliseconds(4), TimeToFirstToken = FirstToken,
                IsComplete = Complete,
            };
        }
        public void CancelGeneration() { }
        public Task<LocalModelTestReport> TestModelAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(new LocalModelTestReport(true, "ok", []));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
