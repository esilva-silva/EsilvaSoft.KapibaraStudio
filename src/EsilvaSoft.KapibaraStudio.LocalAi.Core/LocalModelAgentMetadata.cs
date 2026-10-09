namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>Optional contract of a package trained for local agent turns. Metadata alone grants no tools.</summary>
public sealed record LocalModelAgentMetadata(
    string PromptFormat, string ChatTemplateFile, string ToolCallFormat, string ToolSchemaVersion,
    int MaxToolCallsPerTurn, int MaxToolResultBytes, string? ConstrainedDecoding);

public sealed record LocalModelReasoningBudget(int Default, int Minimum, int Maximum);

/// <summary>Model-owned reasoning protocol. Text is transient unless a separate retention policy permits it.</summary>
public sealed record LocalModelReasoningMetadata(
    bool Supported, string Format, int StartTokenId, int EndTokenId, bool DefaultEnabled,
    string Toggle, string DisabledGenerationSuffix, LocalModelReasoningBudget? BudgetTokens,
    int? TurnBudgetTokens, string? OnBudgetExceeded, string? ForceCloseText,
    string? History, string TemplateSha256);

public sealed record LocalModelAgentSampling(double Temperature, double TopP, int TopK);

public sealed record LocalModelAgentGeneration(
    int MaxAnswerTokensPerStep, LocalModelAgentSampling Thinking, LocalModelAgentSampling NonThinking);
