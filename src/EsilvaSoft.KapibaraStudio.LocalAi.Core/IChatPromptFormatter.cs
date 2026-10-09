namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>Neutral input to a model's own chat template. Content is data, never a template directive.</summary>
public sealed record ModelChatMessage(string Role, string Content);

public sealed record ModelChatPrompt(IReadOnlyList<ModelChatMessage> Messages, string? ToolsJson = null,
    bool AddGenerationPrompt = true, bool DisableReasoning = false);

/// <summary>Owned by the loaded runtime and used only inside the generation lease.</summary>
public interface IChatPromptFormatter
{
    IReadOnlyList<int> RenderTokens(ModelChatPrompt prompt);
    int? ReasoningStartTokenId { get; }
    int? ReasoningEndTokenId { get; }
}
