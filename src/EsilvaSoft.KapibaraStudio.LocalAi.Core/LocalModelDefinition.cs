namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

public sealed record LocalModelDefinition(string Id, string Name, string Path, string Architecture, string? TokenizerPath = null)
{
    public const int ConservativeContextLength = 8192;
    public const int ConservativeAutocompleteMaximumTokens = 256;

    public string PromptFormat { get; init; } = LocalModelPromptFormats.QwenFim;
    /// <summary>Without metadata a FIM model serves autocomplete and the chat proposal contract.</summary>
    public LocalModelCapabilities Capabilities { get; init; } = LocalModelCapabilities.Autocomplete | LocalModelCapabilities.Chat | LocalModelCapabilities.Fim;
    public LocalModelMetadata? Metadata { get; init; }
    public int? ContextLength { get; init; }
    public int? AutocompleteMaximumTokens { get; init; }
    public long? ModelSizeBytes { get; init; }
    public int EffectiveContextLength => ContextLength ?? ConservativeContextLength;
    public int EffectiveAutocompleteMaximumTokens => AutocompleteMaximumTokens ?? ConservativeAutocompleteMaximumTokens;
    public string ContextLengthSource => ContextLength is null ? "teto conservador do produto (8192)" : "genai_config.json";
    public string AutocompleteMaximumTokensSource => AutocompleteMaximumTokens is null ? "teto conservador do produto (256)" : "metadata.autocomplete.maxTokens";
    public string BudgetConfidence => ContextLength is null || AutocompleteMaximumTokens is null ? "estimativa" : "declarado pelo modelo";
}
