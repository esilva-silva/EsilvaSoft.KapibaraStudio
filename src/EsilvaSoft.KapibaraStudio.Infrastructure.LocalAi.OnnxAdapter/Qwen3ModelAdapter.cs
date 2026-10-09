using System.Text;
using System.Text.Json;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;

/// <summary>Qwen3 chat/agent export. It deliberately has no FIM capability or FIM prompt path.</summary>
public sealed class Qwen3ModelAdapter(ILocalModelFileAccess fileAccess) : IModelAdapter
{
    private static readonly string[] ChatStops = ["<|im_end|>", "<|endoftext|>"];
    private readonly ILocalModelFileAccess _files = fileAccess ?? throw new ArgumentNullException(nameof(fileAccess));
    public string Architecture => "Qwen3";
    public string PromptFormat => LocalModelPromptFormats.Qwen3ChatMlTools;
    public string Description => "Qwen3 ChatML (qwen3)";
    public LocalModelCapabilities DefaultCapabilities => LocalModelCapabilities.Chat;
    public bool CanHandle(string modelType, string root) => modelType == "qwen3";

    public ModelAdapterFailure? Validate(ModelFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        var templatePath = Path.Combine(folder.Root, "chat_template.jinja");
        if (!_files.FileExists(templatePath) || _files.GetFileLength(templatePath) is <= 0 or > 1024 * 1024)
            return new(LocalModelState.MissingFiles, "Arquivo ausente ou vazio: chat_template.jinja.");
        if (!folder.Tokenizer.TryGetProperty("added_tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException();
        var added = tokens.EnumerateArray().ToDictionary(
            token => token.GetProperty("content").GetString()!,
            token => token.GetProperty("id").GetInt32(), StringComparer.Ordinal);
        if (!added.ContainsKey("<|im_start|>") || !added.ContainsKey("<|im_end|>"))
            return new(LocalModelState.Unsupported, "Tokenizer Qwen3 sem marcadores de chat.");
        if (folder.Metadata?.Reasoning is { Supported: true } reasoning &&
            (!added.TryGetValue("<think>", out var start) || start != reasoning.StartTokenId ||
             !added.TryGetValue("</think>", out var end) || end != reasoning.EndTokenId))
            return new(LocalModelState.Unsupported, "Tokenizer Qwen3 sem IDs de raciocínio declarados no metadata.");
        return null;
    }

    public ICompletionPromptBuilder CreatePromptBuilder() => new UnsupportedPromptBuilder();
    public ITokenizer CreateTokenizer(Model model, string root) => new OnnxModelTokenizer(model);
    public IReadOnlySet<int> GetStopTokens(ITokenizer tokenizer) =>
        ChatStops.Select(tokenizer.Encode)
            .Where(ids => ids.Count == 1).Select(ids => ids[0]).ToHashSet();

    public IChatPromptFormatter? CreateChatPromptFormatter(ITokenizer tokenizer, string root, LocalModelMetadata? metadata)
    {
        if (tokenizer is not OnnxModelTokenizer native) throw new NotSupportedException("Tokenizer Qwen3 incompatível.");
        var path = Path.Combine(root, "chat_template.jinja");
        if (!_files.FileExists(path) || _files.GetFileLength(path) is <= 0 or > 1024 * 1024)
            throw new InvalidDataException();
        using var stream = _files.OpenRead(path);
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
        var template = reader.ReadToEnd();
        return new Qwen3ChatPromptFormatter(native, template,
            metadata?.Reasoning?.StartTokenId, metadata?.Reasoning?.EndTokenId,
            metadata?.Reasoning?.DisabledGenerationSuffix);
    }

    private sealed class UnsupportedPromptBuilder : ICompletionPromptBuilder
    {
        public IReadOnlyList<int> Build(string prefix, string suffix, int contextTokens, ITokenizer tokenizer) =>
            throw new NotSupportedException("Qwen3 requer mensagens de chat ou tokens de prompt.");
    }
}

internal sealed class Qwen3ChatPromptFormatter(
    OnnxModelTokenizer tokenizer, string template, int? reasoningStartTokenId, int? reasoningEndTokenId,
    string? disabledReasoningSuffix) : IChatPromptFormatter
{
    public int? ReasoningStartTokenId => reasoningStartTokenId;
    public int? ReasoningEndTokenId => reasoningEndTokenId;

    public IReadOnlyList<int> RenderTokens(ModelChatPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (prompt.Messages is null || prompt.Messages.Count is < 1 or > 128) throw new InvalidDataException();
        foreach (var message in prompt.Messages)
        {
            if (message is null || message.Role is not ("system" or "user" or "assistant" or "tool")
                || message.Content is null || message.Content.Length > 262_144
                || message.Role is "user" or "tool" && CompletionOutputProcessor.ContainsReservedOrSensitiveText(message.Content))
                throw new InvalidDataException("Mensagem de chat contém formato ou marcador reservado inválido.");
        }
        var tools = prompt.ToolsJson ?? "[]";
        if (tools.Length > 131_072) throw new InvalidDataException();
        using (var json = JsonDocument.Parse(tools))
            if (json.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException();
        var messages = JsonSerializer.Serialize(prompt.Messages.Select(message => new { role = message.Role, content = message.Content }));
        var rendered = tokenizer.ApplyChatTemplate(template, messages, tools, prompt.AddGenerationPrompt);
        if (prompt.DisableReasoning)
        {
            if (!prompt.AddGenerationPrompt || disabledReasoningSuffix is null) throw new InvalidDataException();
            rendered += disabledReasoningSuffix;
        }
        return tokenizer.Encode(rendered);
    }
}
