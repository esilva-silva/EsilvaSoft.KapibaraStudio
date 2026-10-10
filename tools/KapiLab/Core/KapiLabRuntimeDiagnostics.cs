using EsilvaSoft.KapibaraStudio.Application;

namespace EsilvaSoft.KapibaraStudio.KapiLab.Core;

/// <summary>Retains only fixed runtime phases for safe CLI diagnostics; never stores detail text.</summary>
internal sealed class KapiLabRuntimeDiagnostics : IAutocompleteDiagnostics
{
    private readonly Action? _firstTokenGenerated;
    private string? _failureStage;
    private string? _loadStage;

    public KapiLabRuntimeDiagnostics(Action? firstTokenGenerated = null) => _firstTokenGenerated = firstTokenGenerated;

    public string? FailureStage => Volatile.Read(ref _failureStage);

    public void Record(string eventName, string? detail = null, TimeSpan? duration = null)
    {
        ArgumentNullException.ThrowIfNull(eventName);
        if (eventName == "provider.generation.first-token") _firstTokenGenerated?.Invoke();
        var loadStage = eventName switch
        {
            "provider.config.creating" => "provider-config-create",
            "provider.config.created" => "provider-config-ready",
            "provider.clearing" => "provider-clear",
            "provider.appending" => "provider-append",
            "provider.appended" => "provider-appended",
            "provider.overlaying" => "provider-overlay",
            "provider.model.creating" => "model-create",
            "provider.model.created" => "model-created",
            _ => null,
        };
        if (loadStage is not null) Volatile.Write(ref _loadStage, loadStage);
        var stage = eventName switch
        {
            "provider.load.failed" => Volatile.Read(ref _loadStage) ?? "provider-load",
            "tokenizer.load.failed" => "tokenizer-initialization",
            "provider.generation.failed" => "provider-generation",
            _ => null,
        };
        if (stage is not null) Volatile.Write(ref _failureStage, stage);
    }
}
