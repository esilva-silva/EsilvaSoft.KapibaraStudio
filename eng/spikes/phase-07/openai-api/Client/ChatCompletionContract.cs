using System.Text;
using System.Text.Json;
using OpenAI.Chat;

namespace EsilvaSoft.SlopStudio.Spikes.OpenAiApi;

public sealed record ProposedFunction(string CallId, string Name, string Arguments);
public sealed record ObservedTurn(string Text, IReadOnlyList<ProposedFunction> Functions, ChatFinishReason? FinishReason);

// Stable Chat Completions contract spike. It returns proposals only and never executes tools.
public static class ChatCompletionContract
{
    private const string FunctionName = "fixture_echo";

    public static ChatCompletionOptions CreateOptions() => new()
    {
        AllowParallelToolCalls = false,
        Tools =
        {
            ChatTool.CreateFunctionTool(
                FunctionName,
                "Synthetic fixture function; never connected to product data.",
                BinaryData.FromString(
                    """{"type":"object","properties":{"message":{"type":"string"}},"required":["message"],"additionalProperties":false}"""),
                true),
        },
    };

    public static List<ChatMessage> CreateMessages() =>
    [
        new UserChatMessage("Use only the synthetic function if needed."),
    ];

    public static async Task<ObservedTurn> ReadAsync(
        ChatClient client,
        IReadOnlyList<ChatMessage> messages,
        ChatCompletionOptions options,
        CancellationToken token)
    {
        var text = new StringBuilder();
        Dictionary<int, ToolCallBuilder> calls = [];
        ChatFinishReason? finishReason = null;

        await foreach (var update in client.CompleteChatStreamingAsync(messages, options, token))
        {
            foreach (var content in update.ContentUpdate)
            {
                text.Append(content.Text);
            }

            foreach (var callUpdate in update.ToolCallUpdates)
            {
                if (!calls.TryGetValue(callUpdate.Index, out var builder))
                {
                    builder = new ToolCallBuilder();
                    calls.Add(callUpdate.Index, builder);
                }

                if (callUpdate.ToolCallId is not null) builder.CallId = callUpdate.ToolCallId;
                if (callUpdate.FunctionName is not null) builder.Name = callUpdate.FunctionName;
                if (callUpdate.FunctionArgumentsUpdate is not null)
                {
                    builder.Arguments.Append(callUpdate.FunctionArgumentsUpdate.ToString());
                }
            }

            if (update.FinishReason is not null)
            {
                finishReason = update.FinishReason;
            }
        }

        if (finishReason is null)
        {
            throw new InvalidDataException("Chat Completion stream ended without a terminal finish reason.");
        }

        if (finishReason == ChatFinishReason.ToolCalls)
        {
            var proposals = calls.OrderBy(pair => pair.Key).Select(pair => Validate(pair.Value)).ToArray();
            if (proposals.Length == 0)
            {
                throw new InvalidDataException("Tool call finish reason had no complete tool proposal.");
            }

            return new(text.ToString(), Array.AsReadOnly(proposals), finishReason);
        }

        if (finishReason != ChatFinishReason.Stop)
        {
            throw new InvalidDataException("Chat Completion ended for an unsupported or incomplete reason.");
        }

        if (calls.Count != 0)
        {
            throw new InvalidDataException("A tool proposal was streamed without a tool_calls terminal reason.");
        }

        return new(text.ToString(), Array.Empty<ProposedFunction>(), finishReason);
    }

    private static ProposedFunction Validate(ToolCallBuilder call)
    {
        if (string.IsNullOrWhiteSpace(call.CallId) || call.Name != FunctionName)
        {
            throw new InvalidDataException("Tool proposal is missing an ID or is outside the explicit fixture allowlist.");
        }

        using var arguments = JsonDocument.Parse(call.Arguments.ToString());
        var root = arguments.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Tool proposal arguments must be a JSON object.");
        }
        var properties = root.EnumerateObject().ToArray();
        if (properties.Length != 1 ||
            properties[0].Name != "message" ||
            properties[0].Value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Tool proposal arguments did not match the closed fixture schema.");
        }

        return new(call.CallId, call.Name, call.Arguments.ToString());
    }

    private sealed class ToolCallBuilder
    {
        public string? CallId { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
    }
}
