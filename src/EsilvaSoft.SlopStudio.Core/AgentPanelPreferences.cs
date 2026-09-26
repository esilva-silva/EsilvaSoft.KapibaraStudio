using System.Text.Json.Serialization;
using EsilvaSoft.SlopStudio.Core.Agents;

namespace EsilvaSoft.SlopStudio.Core;

/// <summary>
/// Agent IA panel state kept in the workspace session (ADR-056). Additive to session version 2: every field is optional,
/// omitted when null and, when absent, left to the UI defaults. Holds identifiers and layout only: never messages,
/// attachments, provider credentials or connection data. The mode is stored by name.
/// </summary>
public sealed record AgentPanelPreferences
{
    public const int MaximumIdentifierChars = 128;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectedProviderId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SelectedModelId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AgentOperationMode? SelectedMode { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Guid? ActiveConversationId { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsOpen { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PanelWidth { get; init; }

    /// <summary>Rejects values no UI could have written (the session boundary reports them instead of guessing).</summary>
    public void Validate()
    {
        if (!IsSafeIdentifier(SelectedProviderId) || !IsSafeIdentifier(SelectedModelId) ||
            (SelectedMode is { } mode && !Enum.IsDefined(mode)) || ActiveConversationId == Guid.Empty ||
            (PanelWidth is { } width && (!double.IsFinite(width) || width <= 0)))
            throw new InvalidDataException("Preferências do painel do Agente IA inválidas.");
    }

    private static bool IsSafeIdentifier(string? value) =>
        value is null || (value.Length is > 0 and <= MaximumIdentifierChars && !value.Any(char.IsControl));
}
