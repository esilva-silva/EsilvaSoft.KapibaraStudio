using System.Text.Json.Serialization;

namespace EsilvaSoft.SlopStudio.Core;

public sealed record AutocompleteSettings
{
    /// <summary>Absolute safety bound for an editable budget; a model-declared smaller window still wins.</summary>
    public const int AbsoluteContextMaximum = 1_048_576;
    /// <summary>Absolute safety bound for output; an explicit model cap can be lower.</summary>
    public const int AbsoluteCompletionMaximum = 1_048_576;

    public int Version { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public AutocompleteMode Mode { get; init; } = AutocompleteMode.Automatic;
    /// <summary>External model folder, used only when <see cref="SelectedModel"/> is empty.</summary>
    public string ModelPath { get; init; } = "";
    /// <summary>Additive to version 1: directory scanned for models; empty uses the default local directory.</summary>
    public string ModelDirectory { get; init; } = "";
    /// <summary>Additive to version 1: folder name inside the models directory, never an absolute path.</summary>
    public string SelectedModel { get; init; } = "";
    /// <summary>Additive to version 1: folder name of a separate chat model; empty reuses <see cref="SelectedModel"/>.</summary>
    public string ChatModel { get; init; } = "";
    /// <summary>Additive to version 1: the local agent provider (Agente IA) may use the local model.</summary>
    public bool ChatEnabled { get; init; } = true;
    /// <summary>
    /// Explicit local-only consent to include eligible tab context in a manual AI chat request. Its only consumer, the
    /// per-tab "Assistente IA" panel, was removed on 25/09/2026; the persisted value is kept for session compatibility
    /// until a product decision (reuse by the Agente IA or additive removal) is recorded.
    /// </summary>
    public bool LocalAiContextEnabled { get; init; }
    /// <summary>Separate explicit opt-in for the tab's JSON input in local AI chat context (no consumer; see above).</summary>
    public bool IncludeInputJsonInLocalAiContext { get; init; }
    public AiAccelerationMode Acceleration { get; init; } = AiAccelerationMode.Auto;
    public AiExecutionProvider ExecutionProvider { get; init; } = AiExecutionProvider.Auto;
    public int ContextTokens { get; init; } = 2048;
    public int MaximumCompletionTokens { get; init; } = 32;
    public int DelayMilliseconds { get; init; } = 150;
    /// <summary>Stable JSON payload for manual estimation profiles; empty preserves legacy documents and equality.</summary>
    public string HardwareProfilesJson { get; init; } = "";
    public string SelectedHardwareProfile { get; init; } = "";

    /// <summary>
    /// Additive to version 1: hard timeout of an explicit AI completion (<c>Ctrl+;</c>), measured end to end — queue,
    /// model load and generation included. Expiring never fails the request outright: a partial preview that is
    /// already valid is kept, and only a generation that produced nothing falls back to the list.
    /// </summary>
    public int AiTimeoutMilliseconds { get; init; } = 10_000;
    public bool UseDictionary { get; init; } = true;
    public bool UseInputPanelContext { get; init; } = true;
    public bool UseResultPanelContext { get; init; } = true;
    public bool UseEditorContext { get; init; } = true;
    public bool IncrementalTab { get; init; } = true;
    /// <summary>Additive to version 1: the contextual list opens by itself on trigger characters; absent keeps it explicit only.</summary>
    public bool CompletionAutoOpenOnTrigger { get; init; }
    /// <summary>Additive to version 1: Enter accepts the highlighted list item; false leaves Enter to insert a new line.</summary>
    public bool CompletionEnterAccepts { get; init; } = true;

    /// <summary>
    /// Additive to version 1: whether persisted schema learning (Fase L) may be <em>served</em> to the catalog.
    /// True by default — unlike the automatic AI features (<see cref="InlineUseAi"/>), this is not generation: it is
    /// structural metadata (field names/types/frequencies) already collected, passively, from <c>find</c> results
    /// the user already executed, with no additional query and the same per-source quota as every other catalog
    /// source (K16-b). Turning it off only stops <em>serving</em> already learned structure; it neither deletes
    /// anything nor stops new collection, which is governed independently by <c>SchemaLearningPolicy</c>
    /// (schema-learning.md § Projeções, consultas derivadas e privacidade proposes the twin flags true by default
    /// for this same reason). Absent (legacy document) is <see langword="true"/>.
    /// </summary>
    public bool LearnedSchemaEnabled { get; init; } = true;

    // As três opções abaixo são aditivas à versão 1 e distinguem "campo ausente" de "false explícito" pela presença no
    // JSON, e não pelo inicializador da propriedade: o valor persistido é o anulável, e o valor efetivo é derivado.
    // Um documento antigo (sem as flags) precisa ser migrado; um documento novo com false explícito precisa ser
    // obedecido. Só o anulável é serializado, e apenas quando alguém realmente escolheu um valor.
    private readonly bool? _inlineEnabled;
    private readonly bool? _inlineUseTraditional;
    private readonly bool? _inlineUseAi;
    private readonly bool? _traditionalEnabled;

    /// <summary>Valor persistido de <see cref="TraditionalEnabled"/>; nulo significa ausente no documento salvo.</summary>
    [JsonPropertyName(nameof(TraditionalEnabled)), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? TraditionalEnabledValue { get => _traditionalEnabled; init => _traditionalEnabled = value; }

    /// <summary>
    /// Additive to version 1: the explicit contextual list (<c>Ctrl+Espaço</c>) is available. Absent means enabled.
    /// Independent of the AI and of the inline ghost — turning the list off does not remove the automatic suggestion
    /// (<see cref="InlineUseTraditional"/>), and turning the AI off does not remove the list. When an explicit AI
    /// request fails and this is off, the fallback reports the unavailability <em>without</em> opening a list the user
    /// turned off and without changing the preference.
    /// </summary>
    [JsonIgnore]
    public bool TraditionalEnabled { get => _traditionalEnabled ?? true; init => _traditionalEnabled = value; }

    /// <summary>Valor persistido de <see cref="InlineEnabled"/>; nulo significa ausente no documento salvo.</summary>
    [JsonPropertyName(nameof(InlineEnabled)), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? InlineEnabledValue { get => _inlineEnabled; init => _inlineEnabled = value; }

    /// <summary>Valor persistido de <see cref="InlineUseTraditional"/>; nulo significa ausente no documento salvo.</summary>
    [JsonPropertyName(nameof(InlineUseTraditional)), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? InlineUseTraditionalValue { get => _inlineUseTraditional; init => _inlineUseTraditional = value; }

    /// <summary>Valor persistido de <see cref="InlineUseAi"/>; nulo significa ausente no documento salvo.</summary>
    [JsonPropertyName(nameof(InlineUseAi)), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? InlineUseAiValue { get => _inlineUseAi; init => _inlineUseAi = value; }

    /// <summary>Additive to version 1: suspends every automatic (inline) suggestion; absent means enabled.</summary>
    [JsonIgnore]
    public bool InlineEnabled { get => _inlineEnabled ?? true; init => _inlineEnabled = value; }

    /// <summary>
    /// Additive to version 1: deterministic generator of the automatic suggestion. Absent derives from
    /// <see cref="UseDictionary"/>, which is the closest legacy intent for "automatic suggestion without AI"; a
    /// document that stored false explicitly keeps false even with the dictionary on.
    /// </summary>
    [JsonIgnore]
    public bool InlineUseTraditional { get => _inlineUseTraditional ?? UseDictionary; init => _inlineUseTraditional = value; }

    /// <summary>
    /// Additive to version 1: automatic (typing-triggered) AI inference. Absent means false — processamento automático
    /// de IA é opt-in explícito, tanto em instalação nova quanto em documento antigo migrado. A IA explícita
    /// (<c>Mode != Basic</c>) continua disponível e não depende desta opção.
    /// </summary>
    [JsonIgnore]
    public bool InlineUseAi { get => _inlineUseAi ?? false; init => _inlineUseAi = value; }

    public AutocompleteSettings Validate()
    {
        if (Version != 1 || !Enum.IsDefined(Mode) || !Enum.IsDefined(Acceleration) || !Enum.IsDefined(ExecutionProvider)
            || ModelPath is null || ModelPath.Length > 4096 || ModelDirectory is null || ModelDirectory.Length > 4096
            || !IsModelFolderName(SelectedModel) || !IsModelFolderName(ChatModel) || ContextTokens is < 64 or > AbsoluteContextMaximum
            || MaximumCompletionTokens is < 1 or > AbsoluteCompletionMaximum || DelayMilliseconds is < 50 or > 2000
            || AiTimeoutMilliseconds is < 1000 or > 60_000)
            throw new ArgumentException("Configuração de autocomplete inválida ou de versão não suportada.");
        return this;
    }

    /// <summary>Folder name of the model used by <paramref name="role"/>; empty when the external path applies.</summary>
    public string ModelReference(LocalModelRole role) => role == LocalModelRole.Chat && ChatModel.Length > 0 ? ChatModel : SelectedModel;

    public bool HasModelSelection(LocalModelRole role = LocalModelRole.Autocomplete) =>
        ModelReference(role).Length > 0 || !string.IsNullOrWhiteSpace(ModelPath);

    /// <summary>Selected folder inside the models directory, or the external folder; empty without a selection.</summary>
    public string ResolveModelPath(LocalModelRole role, string defaultDirectory)
    {
        var name = ModelReference(role);
        if (name.Length == 0) return ModelPath.Trim();
        var directory = string.IsNullOrWhiteSpace(ModelDirectory) ? defaultDirectory : ModelDirectory.Trim();
        return Path.Combine(directory, name);
    }

    /// <summary>A single folder segment: selections stay relative to the models directory and cannot escape it.</summary>
    public static bool IsModelFolderName(string? value) =>
        value is not null && (value.Length == 0 || value.Length <= 255 && value.Trim() == value && value is not ("." or "..")
            && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && value.IndexOfAny(['/', '\\', ':']) < 0);
}
