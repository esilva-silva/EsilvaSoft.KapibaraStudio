using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.LocalAi.Core;

/// <summary>Optional kapibarastudio-model.json. Every field is optional; the folder name remains the identity.</summary>
public sealed record LocalModelMetadata
{
    public const string FileName = "kapibarastudio-model.json";
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? Architecture { get; init; }
    public string? Parameters { get; init; }
    public IReadOnlyList<string> Domain { get; init; } = [];
    /// <summary>Null when the file does not declare capabilities.</summary>
    public LocalModelCapabilities? Capabilities { get; init; }
    /// <summary>Hardware the export supports; null means no restriction declared.</summary>
    public IReadOnlyList<AiAccelerationMode>? Hardware { get; init; }
    /// <summary>
    /// Prompt contract the model expects (see <see cref="LocalModelContextContracts"/>); null means the package
    /// declares none, which is treated as the frozen <c>editor-context-v1</c> and never as an incompatibility.
    /// </summary>
    public string? ContextContract { get; init; }
    /// <summary>
    /// Whether the model can exploit context from other files besides the current tab. Null means undeclared and
    /// behaves as false; the distinction is preserved so a future package can state false explicitly.
    /// </summary>
    public bool? SupportsRepositoryContext { get; init; }
    public int? RecommendedContextTokens { get; init; }
    public int? RecommendedCompletionTokens { get; init; }
    public LocalModelGenerationDefaults Autocomplete { get; init; } = new();
    public LocalModelGenerationDefaults Chat { get; init; } = new();
    public LocalModelAgentMetadata? Agent { get; init; }
    public LocalModelReasoningMetadata? Reasoning { get; init; }
    public LocalModelAgentGeneration? AgentGeneration { get; init; }
    public Version? MinimumGenAi { get; init; }
}
