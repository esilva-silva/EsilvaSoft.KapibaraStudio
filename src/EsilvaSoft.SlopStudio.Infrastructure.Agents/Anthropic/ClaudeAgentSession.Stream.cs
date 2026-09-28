using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Anthropic;
using Anthropic.Core;
using Anthropic.Exceptions;
using Anthropic.Models.Messages;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Infrastructure.Agents.Anthropic;

internal sealed partial class ClaudeAgentSession
{
    private enum RoundStop
    {
        None,
        EndTurn,
        ToolUse,
        MaxTokens,
        Refusal,
        ContextWindow,
        Other,
    }

    private enum BlockKind
    {
        Text,
        ToolUse,
        Thinking,
        RedactedThinking,
    }

    /// <summary>Bloco nativo em montagem. Argumentos de tool só são interpretados depois do bloco e da mensagem fecharem.</summary>
    private sealed class BlockState(BlockKind kind)
    {
        public BlockKind Kind { get; } = kind;
        public StringBuilder Text { get; } = new();
        public string? NativeId { get; init; }
        public string? Name { get; init; }
        public string? Signature { get; set; }
        public string? RedactedData { get; init; }
        public bool Closed { get; set; }
        public bool Overflow { get; set; }
    }

    /// <summary>Chamada completa, pronta para o runtime, ou recusada localmente com código seguro.</summary>
    private sealed record ToolCallState(
        string NativeId, string Name, string? ArgumentsJson, IReadOnlyDictionary<string, JsonElement> Input, string? LocalError);

    private sealed record RoundResult(
        RoundStop Stop,
        List<ContentBlockParam> AssistantContent,
        int AssistantChars,
        IReadOnlyList<ToolCallState> ToolCalls,
        long Tokens,
        string? Error);

    private async Task<RoundResult> StreamRoundAsync(
        AnthropicClient client, List<MessageParam> messages, TurnContext turn, ChannelWriter<AgentProviderEvent> writer,
        TurnUsage usage)
    {
        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = _budget.MaxOutputTokensPerRequest,
            System = SystemPrompt,
            Messages = messages,
        };
        if (_tools.Count > 0)
        {
            parameters = parameters with { Tools = [.. _tools.Select(ToSdkTool)] };
        }

        var blocks = new SortedDictionary<long, BlockState>();
        AgentMessageId? messageId = null;
        var stop = RoundStop.None;
        long inputTokens = 0, outputTokens = 0;
        var sawStop = false;
        string? error = null;
        using var watchdog = CancellationTokenSource.CreateLinkedTokenSource(turn.WorkToken);
        watchdog.CancelAfter(_budget.FirstEventTimeout);
        try
        {
            await foreach (var item in client.Messages.CreateStreaming(parameters, cancellationToken: watchdog.Token)
                               .WithCancellation(watchdog.Token).ConfigureAwait(false))
            {
                watchdog.CancelAfter(_budget.StreamIdleTimeout);
                if (item.TryPickStart(out var start))
                {
                    inputTokens = TotalInput(start.Message.Usage);
                    outputTokens = Math.Max(outputTokens, start.Message.Usage.OutputTokens);
                }
                else if (item.TryPickContentBlockStart(out var blockStart))
                {
                    if (blocks.ContainsKey(blockStart.Index) || StartBlock(blockStart.ContentBlock) is not { } state)
                    {
                        error = ClaudeErrorCodes.ProviderProtocolViolation;
                        break;
                    }

                    blocks[blockStart.Index] = state;
                    if (state.Kind == BlockKind.Text && state.Text.Length > 0)
                    {
                        messageId ??= await StartMessageAsync(writer, turn).ConfigureAwait(false);
                        if (!await EmitTextAsync(writer, turn, messageId.Value, state.Text.ToString(), usage).ConfigureAwait(false))
                        {
                            error = ClaudeErrorCodes.OutputBudgetExceeded;
                            break;
                        }
                    }
                }
                else if (item.TryPickContentBlockDelta(out var delta))
                {
                    if (!blocks.TryGetValue(delta.Index, out var state) || state.Closed)
                    {
                        error = ClaudeErrorCodes.ProviderProtocolViolation;
                        break;
                    }

                    if (delta.Delta.TryPickText(out var text))
                    {
                        if (state.Kind != BlockKind.Text)
                        {
                            error = ClaudeErrorCodes.ProviderProtocolViolation;
                            break;
                        }

                        state.Text.Append(text.Text);
                        messageId ??= await StartMessageAsync(writer, turn).ConfigureAwait(false);
                        if (!await EmitTextAsync(writer, turn, messageId.Value, text.Text, usage).ConfigureAwait(false))
                        {
                            error = ClaudeErrorCodes.OutputBudgetExceeded;
                            break;
                        }
                    }
                    else if (delta.Delta.TryPickInputJson(out var json))
                    {
                        if (state.Kind != BlockKind.ToolUse)
                        {
                            error = ClaudeErrorCodes.ProviderProtocolViolation;
                            break;
                        }

                        // Fragmento guardado, nunca interpretado isoladamente; excesso marca a chamada como recusada.
                        if (!state.Overflow && state.Text.Length + json.PartialJson.Length <= _budget.MaxToolArgumentsChars)
                        {
                            state.Text.Append(json.PartialJson);
                        }
                        else
                        {
                            state.Overflow = true;
                        }
                    }
                    else if (delta.Delta.TryPickThinking(out var thinking))
                    {
                        if (state.Kind == BlockKind.Thinking && !AppendBounded(state, thinking.Thinking))
                        {
                            error = ClaudeErrorCodes.OutputBudgetExceeded;
                            break;
                        }
                    }
                    else if (delta.Delta.TryPickSignature(out var signature))
                    {
                        if (state.Kind == BlockKind.Thinking)
                        {
                            state.Signature = signature.Signature;
                        }
                    }

                    // Citações e deltas desconhecidos opcionais são ignorados; nada é repassado à UI.
                }
                else if (item.TryPickContentBlockStop(out var blockStop))
                {
                    if (!blocks.TryGetValue(blockStop.Index, out var state) || state.Closed)
                    {
                        error = ClaudeErrorCodes.ProviderProtocolViolation;
                        break;
                    }

                    state.Closed = true;
                }
                else if (item.TryPickDelta(out var messageDelta))
                {
                    stop = MapStop(messageDelta.Delta.StopReason);
                    outputTokens = Math.Max(outputTokens, messageDelta.Usage.OutputTokens);
                }
                else if (item.TryPickStop(out _))
                {
                    sawStop = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (turn.UserToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (turn.WorkToken.IsCancellationRequested)
        {
            error = ClaudeErrorCodes.TurnTimeout;
        }
        catch (OperationCanceledException) when (watchdog.IsCancellationRequested)
        {
            error = ClaudeErrorCodes.ProviderTimeout;
        }
        catch (Exception exception)
        {
            error = MapException(exception);
        }

        var tokens = inputTokens + outputTokens;
        usage.Tokens += tokens;
        if (messageId is { } open)
        {
            // Mensagem visível fecha antes de qualquer pedido de tool ou erro do turno.
            await writer.WriteAsync(new AgentProviderEvent(AgentEventKind.MessageCompleted, MessageId: open), turn.UserToken)
                .ConfigureAwait(false);
        }

        if (error is not null)
        {
            return Failed(error, tokens);
        }

        if (!sawStop || stop == RoundStop.None || blocks.Values.Any(static block => !block.Closed))
        {
            // Stream cortado antes de message_stop: nenhuma tool é executada a partir de um bloco incompleto.
            return Failed(ClaudeErrorCodes.ProviderStreamIncomplete, tokens);
        }

        var content = new List<ContentBlockParam>(blocks.Count);
        var calls = new List<ToolCallState>();
        var chars = 0;
        foreach (var block in blocks.Values)
        {
            switch (block.Kind)
            {
                case BlockKind.Text when block.Text.Length > 0:
                    content.Add(new TextBlockParam { Text = block.Text.ToString() });
                    chars += block.Text.Length;
                    break;
                case BlockKind.Thinking:
                    // Blocos de raciocínio voltam inalterados ao mesmo modelo no ciclo de tools; nunca vão à UI.
                    content.Add(new ThinkingBlockParam { Thinking = block.Text.ToString(), Signature = block.Signature ?? string.Empty });
                    chars += block.Text.Length + (block.Signature?.Length ?? 0);
                    break;
                case BlockKind.RedactedThinking:
                    content.Add(new RedactedThinkingBlockParam { Data = block.RedactedData ?? string.Empty });
                    chars += block.RedactedData?.Length ?? 0;
                    break;
                case BlockKind.ToolUse:
                    var call = CompleteToolCall(block);
                    calls.Add(call);
                    content.Add(new ToolUseBlockParam { ID = call.NativeId, Name = call.Name, Input = call.Input });
                    chars += call.ArgumentsJson?.Length ?? 2;
                    break;
            }
        }

        if (stop == RoundStop.ToolUse && calls.Count == 0)
        {
            return Failed(ClaudeErrorCodes.ProviderProtocolViolation, tokens);
        }

        // max_tokens/refusal com tool_use: nenhuma tool do ciclo é executada (entrada pode estar truncada).
        return new RoundResult(stop, content, chars, calls, tokens, null);

        static RoundResult Failed(string code, long tokens) => new(RoundStop.None, [], 0, [], tokens, code);
    }

    private static BlockState? StartBlock(RawContentBlockStartEventContentBlock block)
    {
        if (block.TryPickText(out var text))
        {
            var state = new BlockState(BlockKind.Text);
            state.Text.Append(text.Text);
            return state;
        }

        if (block.TryPickToolUse(out var toolUse))
        {
            // O input inicial vem vazio no stream; os argumentos chegam por input_json_delta.
            return string.IsNullOrEmpty(toolUse.ID) || toolUse.ID.Length > 256 || string.IsNullOrEmpty(toolUse.Name) ||
                   toolUse.Name.Length > 128
                ? null
                : new BlockState(BlockKind.ToolUse) { NativeId = toolUse.ID, Name = toolUse.Name };
        }

        if (block.TryPickThinking(out var thinking))
        {
            var state = new BlockState(BlockKind.Thinking) { Signature = thinking.Signature };
            state.Text.Append(thinking.Thinking);
            return state;
        }

        if (block.TryPickRedactedThinking(out var redacted))
        {
            return new BlockState(BlockKind.RedactedThinking) { RedactedData = redacted.Data };
        }

        // Tools de servidor e blocos desconhecidos não são declarados por este adapter: protocolo inesperado.
        return null;
    }

    private static ToolCallState CompleteToolCall(BlockState block)
    {
        var nativeId = block.NativeId!;
        var name = block.Name!;
        if (block.Overflow)
        {
            return new(nativeId, name, null, EmptyInput, ClaudeErrorCodes.ToolArgumentsTooLarge);
        }

        var raw = block.Text.Length == 0 ? "{}" : block.Text.ToString();
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 64 });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new(nativeId, name, null, EmptyInput, ClaudeErrorCodes.InvalidToolArguments);
            }

            var input = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!input.TryAdd(property.Name, property.Value.Clone()))
                {
                    return new(nativeId, name, null, EmptyInput, ClaudeErrorCodes.InvalidToolArguments);
                }
            }

            // Nome e argumentos do modelo seguem como dados não confiáveis; o registry valida schema e autorização.
            return new(nativeId, name, document.RootElement.GetRawText(), input, null);
        }
        catch (JsonException)
        {
            return new(nativeId, name, null, EmptyInput, ClaudeErrorCodes.InvalidToolArguments);
        }
    }

    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyInput = new Dictionary<string, JsonElement>();

    private bool AppendBounded(BlockState state, string text)
    {
        if (state.Text.Length + text.Length > _budget.MaxOutputCharsPerTurn)
        {
            return false;
        }

        state.Text.Append(text);
        return true;
    }

    private static async Task<AgentMessageId> StartMessageAsync(ChannelWriter<AgentProviderEvent> writer, TurnContext turn)
    {
        var id = AgentMessageId.New();
        await writer.WriteAsync(new AgentProviderEvent(AgentEventKind.MessageStarted, MessageId: id), turn.UserToken)
            .ConfigureAwait(false);
        return id;
    }

    private async Task<bool> EmitTextAsync(
        ChannelWriter<AgentProviderEvent> writer, TurnContext turn, AgentMessageId id, string text, TurnUsage usage)
    {
        if (text.Length == 0)
        {
            return true;
        }

        usage.OutputChars += text.Length;
        if (usage.OutputChars > _budget.MaxOutputCharsPerTurn)
        {
            return false;
        }

        await writer.WriteAsync(new AgentProviderEvent(AgentEventKind.MessageDelta, text, MessageId: id), turn.UserToken)
            .ConfigureAwait(false);
        return true;
    }

    private static long TotalInput(Usage usage) =>
        usage.InputTokens + (usage.CacheCreationInputTokens ?? 0) + (usage.CacheReadInputTokens ?? 0);

    private static RoundStop MapStop(ApiEnum<string, StopReason>? reason)
    {
        if (reason is null)
        {
            return RoundStop.None;
        }

        return reason.Raw() switch
        {
            "end_turn" or "stop_sequence" => RoundStop.EndTurn,
            "tool_use" => RoundStop.ToolUse,
            "max_tokens" => RoundStop.MaxTokens,
            "refusal" => RoundStop.Refusal,
            "model_context_window_exceeded" => RoundStop.ContextWindow,
            _ => RoundStop.Other,
        };
    }

    private static Tool ToSdkTool(ClaudeToolDefinition definition) => new()
    {
        Name = definition.Name,
        Description = definition.Description,
        InputSchema = InputSchema.FromRawUnchecked(definition.InputSchema),
    };

    /// <summary>
    /// Exceções do SDK viram códigos fixos. Mensagem, corpo, cabeçalhos e request-id não são copiados: podem conter
    /// trechos do prompt ou dados da conta.
    /// </summary>
    private string MapException(Exception exception)
    {
        switch (exception)
        {
            case AnthropicUnauthorizedException:
                if (_options.ApiKeyReference is { } reference)
                {
                    _provider.ReportCredentialRejected(reference);
                }

                return ClaudeErrorCodes.AuthenticationFailed;
            case AnthropicForbiddenException:
                return ClaudeErrorCodes.ProviderPermissionDenied;
            case AnthropicNotFoundException:
                lock (_gate)
                {
                    _modelUnavailable = true;
                }

                return ClaudeErrorCodes.ModelUnavailable;
            case AnthropicRateLimitException:
                return ClaudeErrorCodes.RateLimited;
            case Anthropic5xxException:
                return ClaudeErrorCodes.ProviderUnavailable;
            case AnthropicBadRequestException or AnthropicUnprocessableEntityException or Anthropic4xxException:
                return ClaudeErrorCodes.ProviderRequestRejected;
            case AnthropicSseException:
                return ClaudeErrorCodes.ProviderStreamError;
            case AnthropicIOException or HttpRequestException or IOException:
                return ClaudeErrorCodes.ProviderUnreachable;
            case AnthropicInvalidDataException or JsonException:
                return ClaudeErrorCodes.ProviderProtocolViolation;
            default:
                return ClaudeErrorCodes.ProviderFailure;
        }
    }
}
