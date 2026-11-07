using EsilvaSoft.KapibaraStudio.Autocomplete.Core;
using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Application;

/// <summary>Deterministic editor completion and shared local-model configuration. Generation is explicit.</summary>
public sealed class AutocompleteService(ILocalAiModelService? models = null) : IAutocompleteService
{
    private AutocompleteSettings _settings = new();
    public AutocompleteSettings Settings => Volatile.Read(ref _settings);
    public event EventHandler? SettingsChanged;
    public LocalModelStatus Status => models?.Status ?? new(LocalModelState.NotInstalled, "IA não instalada; autocomplete básico disponível.");

    public AutocompleteResult? GetImmediateCompletion(AutocompleteRequest request) => Settings is { Enabled: true, UseDictionary: true }
        ? BasicAutocompleteProvider.GetCompletion(request.Bounded()) : null;

    public async Task ConfigureAsync(AutocompleteSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        Volatile.Write(ref _settings, settings);
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        if (models is not null) await models.SwitchModelAsync(settings, cancellationToken).ConfigureAwait(false);
    }

    public Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CancellationToken cancellationToken = default) =>
        GetCompletionAsync(request, CompletionSourcePolicy.All, cancellationToken);

    /// <summary>The automatic editor path never loads a model or infers, even for a legacy policy requesting AI.</summary>
    public Task<AutocompleteResult?> GetCompletionAsync(AutocompleteRequest request, CompletionSourcePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var settings = Settings;
        return Task.FromResult(settings.Enabled && settings.UseDictionary && policy.Dictionary
            ? BasicAutocompleteProvider.GetCompletion(request.Bounded()) : null);
    }

    public async Task<LocalModelStatus> TestModelAsync(CancellationToken cancellationToken = default)
    {
        if (models is null) return Status;
        var report = await RunModelTestAsync(cancellationToken).ConfigureAwait(false);
        return report.Succeeded ? models.Status : new(LocalModelState.Failed, report.Message);
    }

    public async Task<LocalModelTestReport> RunModelTestAsync(CancellationToken cancellationToken = default)
    {
        if (models is null) return new(false, "IA local não configurada nesta instalação; autocomplete básico disponível.", []);
        var settings = Settings;
        if (!settings.UsesLocalModel)
            return new(false, "A IA local está desligada. Habilite o autocomplete com IA ou o Agente IA local para testar.", []) { Hardware = settings.Acceleration };
        return await models.TestModelAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
