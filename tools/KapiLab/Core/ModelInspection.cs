using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Infrastructure.LocalAi;
using System.Text.Json.Serialization;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

internal sealed record ModelInspectionReport(string Schema, IReadOnlyList<ModelInspection> Models);

internal sealed record ModelInspection(string Path, string State, string Message, string? Id, string? Name,
    string? Architecture, string? PromptFormat, LocalModelCapabilities? Capabilities, int? ContextLength,
    string ContextLengthSource, int? AutocompleteMaximumTokens, string AutocompleteMaximumTokensSource,
    long? ModelSizeBytes, LocalModelMetadata? Metadata, IReadOnlyList<AiHardwareDevice>? AvailableHardware)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? SchemaValid { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IdeSupported { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StrictSchema { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HashesValid { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HashScope { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? VerifiedFiles { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? ValidationIssues { get; init; }

    public static ModelInspection From(LocalModelValidation validation, bool includeHardware)
    {
        var model = validation.Model;
        return new(validation.Path, validation.Status.State.ToString(), validation.Status.Message,
            model?.Id, model?.Name ?? validation.Status.ModelName, model?.Architecture, model?.PromptFormat,
            model?.Capabilities, model?.ContextLength, model?.ContextLengthSource ?? "unavailable",
            model?.AutocompleteMaximumTokens, model?.AutocompleteMaximumTokensSource ?? "unavailable", model?.ModelSizeBytes,
            model?.Metadata, includeHardware ? OnnxHardwareProbe.Detect() : null);
    }
}
