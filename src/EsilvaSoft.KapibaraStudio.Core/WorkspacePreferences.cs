using System.Text.Json.Serialization;

namespace EsilvaSoft.KapibaraStudio.Core;

public sealed record WorkspacePreferences
{
    public AutocompleteSettings Autocomplete { get; init; } = new();
    public string Theme { get; init; } = "Sistema";
    /// <summary>
    /// Additive to version 1: the initial UI language is pt-BR; an invalid or missing
    /// code is normalized to the English fallback at the session boundary.
    /// </summary>
    public string Language { get; init; } = ApplicationLanguages.DefaultCode;
    public double CodeFontSize { get; init; } = 14;
    public double ExplorerWidth { get; init; } = 260;
    public double EditorRatio { get; init; } = 0.6;
    public bool RecoverDrafts { get; init; } = true;
    public Guid[] ExcludedProfileIds { get; init; } = [];
    /// <summary>Additive to version 1: sessions without this value keep Standard.</summary>
    public UuidRepresentation UuidRepresentation { get; init; } = UuidRepresentation.Standard;
    /// <summary>Per-connection overrides; a missing entry uses <see cref="UuidRepresentation"/>.</summary>
    public Dictionary<Guid, UuidRepresentation> ProfileUuidRepresentations { get; init; } = [];
    /// <summary>
    /// Additive to version 1 and independent of <see cref="UuidRepresentation"/>: sessions without this value use Standard
    /// (ObjectId + UUID) and keep their UUID representation unchanged.
    /// </summary>
    public IdentifierRepresentationMode IdentifierMode { get; init; } = IdentifierRepresentationMode.Standard;
    /// <summary>
    /// Additive to version 1: profiles whose autocomplete may sample field names and types without an explicit action.
    /// Sessions without this value never sample automatically.
    /// </summary>
    public Guid[] SchemaSamplingProfileIds { get; init; } = [];
    /// <summary>
    /// Additive to version 1: per-connection opt-out of <em>serving</em> persisted schema learning to the
    /// autocomplete catalog, in the same shape as <see cref="SchemaSamplingProfileIds"/> (an explicit list, never a
    /// boolean per profile). Unlike that list — which is opt-in because it triggers automatic sampling — this one
    /// is opt-out, because <see cref="AutocompleteSettings.LearnedSchemaEnabled"/> already defaults serving to
    /// true; excluding a profile here only stops serving what was already learned for it, it never deletes
    /// anything (that is the explicit "Limpar aprendizado" command, DEC-L-RETENTION, which calls
    /// <c>ILearnedSchemaRepository.RemoveProfileAsync</c>/<c>RemoveDatabaseAsync</c> directly and needs no flag).
    /// Absent (legacy document) excludes nobody.
    /// </summary>
    public Guid[] LearnedSchemaExcludedProfileIds { get; init; } = [];
    /// <summary>
    /// Additive to version 1: null (absent) uses <see cref="Core.EditorKeyBindings.Defaults"/> and is not written back,
    /// so sessions without custom shortcuts keep their shape.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EditorKeyBindings? EditorKeyBindings { get; init; }
    /// <summary>
    /// Additive to version 2 (ADR-056): Agent IA panel state. Null (absent) keeps the UI defaults and is not written
    /// back, so sessions saved before this field keep their shape.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AgentPanelPreferences? AgentPanel { get; init; }

    /// <summary>Optional per-user Copilot executable override; null keeps automatic discovery.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CopilotCliExecutablePath { get; init; }

    public void ValidateKeyBindings() => EditorKeyBindings?.Validate();

    public void ValidateMetadata()
    {
        AgentPanel?.Validate();
        if (CopilotCliExecutablePath is { } cliPath &&
            (string.IsNullOrWhiteSpace(cliPath) || cliPath.Length > 4096 || cliPath.Any(char.IsControl) || !Path.IsPathFullyQualified(cliPath) ||
             (OperatingSystem.IsWindows() && !string.Equals(Path.GetExtension(cliPath), ".exe", StringComparison.OrdinalIgnoreCase))))
            throw new InvalidDataException("Caminho da CLI Copilot inválido.");
        if (SchemaSamplingProfileIds is null || SchemaSamplingProfileIds.Contains(Guid.Empty))
            throw new InvalidDataException("Preferência de amostragem de schema inválida.");
        if (LearnedSchemaExcludedProfileIds is null || LearnedSchemaExcludedProfileIds.Contains(Guid.Empty))
            throw new InvalidDataException("Preferência de exclusão de schema aprendido inválida.");
    }

    public void ValidateUuid()
    {
        if (!Enum.IsDefined(UuidRepresentation) || ProfileUuidRepresentations is null
            || ProfileUuidRepresentations.Any(entry => entry.Key == Guid.Empty || !Enum.IsDefined(entry.Value)))
            throw new InvalidDataException("Preferência de UUID inválida ou de versão não suportada.");
        if (!Enum.IsDefined(IdentifierMode))
            throw new InvalidDataException("Modo de identificador inválido ou de versão não suportada.");
    }
}
