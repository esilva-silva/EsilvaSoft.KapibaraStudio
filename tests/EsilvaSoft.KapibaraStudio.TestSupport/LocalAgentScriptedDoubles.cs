using System.Runtime.CompilerServices;
using EsilvaSoft.KapibaraStudio.Application.Agents;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;

namespace EsilvaSoft.KapibaraStudio.Testing;

/// <summary>Uma geração do roteiro: pedaços entregues em ordem, com falha ou retenção opcionais.</summary>
internal sealed record ScriptedAgentStep(params GeneratedChunk[] Chunks)
{
    public Exception? Failure { get; init; }
    /// <summary>Depois de entregar os pedaços, espera o cancelamento em vez de concluir a geração.</summary>
    public bool HoldAtEnd { get; init; }
}

/// <summary>
/// Runtime local roteirizado para o agente: cada passo do roteiro é uma geração, com ids de token para os delimitadores
/// de raciocínio. Registra os pedidos recebidos; não carrega pesos nem usa rede.
/// </summary>
internal sealed class ScriptedAgentRuntime : ILocalModelRuntime
{
    private readonly Queue<ScriptedAgentStep> _steps = new();
    public List<ModelGenerationRequest> Requests { get; } = [];
    /// <summary>Por pedido: havia sessão de geração e ela ainda estava viva quando o passo começou.</summary>
    public List<bool> SessionOpenAtStart { get; } = [];
    public TaskCompletionSource Held { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public LocalModelRuntimeInfo? RuntimeInfo => null;

    public ScriptedAgentRuntime Then(params GeneratedChunk[] chunks) { _steps.Enqueue(new ScriptedAgentStep(chunks)); return this; }
    public ScriptedAgentRuntime Then(ScriptedAgentStep step) { _steps.Enqueue(step); return this; }

    public Task InitializeAsync(LocalModelDefinition model, AutocompleteSettings settings, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<ModelGenerationResult> GenerateAsync(ModelGenerationRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("O agente local usa streaming.");

    public async IAsyncEnumerable<GeneratedChunk> StreamAsync(ModelGenerationRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        SessionOpenAtStart.Add(request.PrefixCache is { } session && !session.SessionLifetime.IsCancellationRequested);
        if (!_steps.TryDequeue(out var step)) throw new InvalidOperationException("Roteiro esgotado: o provider gerou um passo a mais.");
        if (step.Failure is not null) throw step.Failure;
        var generated = 0;
        foreach (var chunk in step.Chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return chunk with { GeneratedTokens = ++generated };
        }
        if (step.HoldAtEnd)
        {
            Held.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }
        yield return new GeneratedChunk("", generated, true) { Provider = "cpu" };
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Vocabulário dos roteiros: pedaços de texto, tokens de raciocínio, envelope de chamada e o pacote de agente de teste.</summary>
internal static class LocalAgentScript
{
    public const int ThinkStart = 151667;
    public const int ThinkEnd = 151668;
    public const string ForceClose = "\n</think>\n\n";

    public static GeneratedChunk Text(string text) => new(text, 0, false) { TokenId = 1 };
    public static GeneratedChunk Token(int id) => new("", 0, false) { TokenId = id };
    public static string Call(string name, string arguments = "{}") =>
        "<tool_call>{\"name\":\"" + name + "\",\"arguments\":" + arguments + "}</tool_call>";

    /// <summary>Pacote de agente Qwen3 sintético: resposta de 32 tokens, raciocínio 8 por passo e 12 por turno.</summary>
    public static LocalModelDefinition AgentModel(string? contract = LocalAgentPromptBuilder.ContractId, string toggle = "hard",
        int maxToolCalls = 2, int maxResultBytes = 64) =>
        new("agent-test", "Agente de teste", "models", "Qwen3")
        {
            PromptFormat = LocalModelPromptFormats.Qwen3ChatMlTools,
            Capabilities = LocalModelCapabilities.Chat | LocalModelCapabilities.Agent | LocalModelCapabilities.Tools | LocalModelCapabilities.Reasoning,
            Metadata = new LocalModelMetadata
            {
                AgentContract = contract,
                Agent = new("qwen3-chatml-tools-v1", "chat_template.jinja", "qwen-hermes-json", "kapibara-tools-2026-10",
                    maxToolCalls, maxResultBytes, null),
                Reasoning = new(true, "think-tags", ThinkStart, ThinkEnd, DefaultEnabled: true, toggle, "", new(8, 1, 64),
                    TurnBudgetTokens: 12, "force-close", ForceClose, "strip", new string('0', 64)),
                AgentGeneration = new(32, new(0.6, 0.95, 20), new(0.7, 0.8, 20)),
            },
        };
}
