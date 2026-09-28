using EsilvaSoft.SlopStudio.LocalAi.Core;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class AutocompleteSettingsViewModel(IAutocompleteService service, ILocalModelCatalog? catalog,
    Func<AutocompleteSettings, Task> save, ILocalAiModelService? models = null, IRemoteModelSource? remote = null,
    IApplicationOperationService? operations = null) : ObservableObject
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly IRemoteModelSource? _remote = ConfigureLocalization(remote);
    private readonly IApplicationOperationService? _operations = operations;
    private IReadOnlyList<AiHardwareDevice> _hardware = [];
    private AiExecutionProvider _executionProvider;
    private string _chatModel = "";
    private bool _loading;
    private AutocompleteSettings _loaded = new();
    private long _refreshGeneration;

    private readonly ILocalAiModelService? _localizedModels = ConfigureLocalization(models);
    private readonly ILocalModelCatalog? _localizedCatalog = ConfigureLocalization(catalog);

    private static ILocalAiModelService? ConfigureLocalization(ILocalAiModelService? value)
    {
        value?.SetLocalization(LocalizationViewModel.Current.Resolve);
        return value;
    }

    private static ILocalModelCatalog? ConfigureLocalization(ILocalModelCatalog? value)
    {
        value?.SetLocalization(LocalizationViewModel.Current.Resolve);
        return value;
    }

    private static IRemoteModelSource? ConfigureLocalization(IRemoteModelSource? value)
    {
        value?.SetLocalization(LocalizationViewModel.Current.Resolve);
        return value;
    }

    public IReadOnlyList<string> Modes
    {
        get
        {
            _ = _loaded;
            return [T("autocompleteModeAuto"), T("autocompleteModeBasic"), T("autocompleteModeLocalAi")];
        }
    }
    public ObservableCollection<HardwareOption> HardwareOptions { get; } = CreateHardwareOptions();
    public ObservableCollection<LocalModelOption> Models { get; } = [];
    public string DefaultDirectory => catalog?.DefaultDirectory ?? models?.DefaultDirectory ?? "";
    public string EffectiveModelDirectory => string.IsNullOrWhiteSpace(ModelDirectory) ? DefaultDirectory : ModelDirectory.Trim();
    public bool HasModelDetails => ModelDetails.Length > 0;
    public bool HasModelDiagnostics => ModelDiagnostics.Length > 0;
    public bool HasTestReport => TestReport.Length > 0;

    private static ObservableCollection<HardwareOption> CreateHardwareOptions() =>
    [
        new(AiAccelerationMode.Auto, T("hardwareAuto")), new(AiAccelerationMode.Cpu, "CPU"),
        new(AiAccelerationMode.Gpu, "GPU"), new(AiAccelerationMode.Npu, "NPU")
    ];

    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private int _hardwareIndex;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(EffectiveModelDirectory))] private string _modelDirectory = "";
    /// <summary>External model folder; setting it selects that folder.</summary>
    [ObservableProperty] private string _modelPath = "";
    [ObservableProperty] private LocalModelOption? _selectedModelOption;
    [ObservableProperty] private bool _chatEnabled = true;
    // No UI and no consumer since the per-tab assistant was removed (ADR-055): carried through unchanged so saving
    // preferences never rewrites the persisted opt-ins until the product decision is recorded.
    [ObservableProperty] private bool _localAiContextEnabled;
    [ObservableProperty] private bool _includeInputJsonInLocalAiContext;
    [ObservableProperty] private int _contextTokens = 2048;
    [ObservableProperty] private int _maximumTokens = 32;
    [ObservableProperty] private int _delayMilliseconds = 150;
    [ObservableProperty] private bool _useDictionary = true;
    [ObservableProperty] private bool _useInputPanelContext = true;
    [ObservableProperty] private bool _useResultPanelContext = true;
    [ObservableProperty] private bool _useEditorContext = true;
    [ObservableProperty] private bool _incrementalTab = true;
    [ObservableProperty] private bool _completionAutoOpenOnTrigger;
    [ObservableProperty] private bool _completionEnterAccepts = true;
    // As três propriedades abaixo espelham o par valor/efetivo de AutocompleteSettings: o campo privado guarda
    // exatamente o que está persistido (nulo = ausente/nunca tocado pelo usuário nesta janela) e a propriedade
    // "Effective" alimenta o CheckBox. Um toggle do usuário materializa o valor explícito; "Usar padrão" volta a nulo.
    private bool? _inlineEnabledOverride;
    private bool? _inlineUseTraditionalOverride;
    private bool? _inlineUseAiOverride;
    [ObservableProperty] private bool _inlineEnabledEffective = true;
    [ObservableProperty] private bool _inlineEnabledIsOverridden;
    [ObservableProperty] private bool _inlineUseTraditionalEffective = true;
    [ObservableProperty] private bool _inlineUseTraditionalIsOverridden;
    [ObservableProperty] private bool _inlineUseAiEffective;
    [ObservableProperty] private bool _inlineUseAiIsOverridden;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasModelDetails))] private string _modelDetails = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasModelDiagnostics))] private string _modelDiagnostics = "";
    [ObservableProperty] private string _detectedHardware = T("modelHardwareNotQueried");
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasTestReport))] private string _testReport = "";
    [ObservableProperty] private string _status = T("noModelBasicAvailable");
    [ObservableProperty] private string _operationStatus = "";
    [ObservableProperty] private bool _isBusy;

    partial void OnHardwareIndexChanged(int value)
    {
        RefreshStatus();
        RefreshTokenBudget();
    }

    partial void OnInlineEnabledEffectiveChanged(bool value)
    {
        if (_loading) return;
        _inlineEnabledOverride = value;
        InlineEnabledIsOverridden = true;
    }

    partial void OnInlineUseTraditionalEffectiveChanged(bool value)
    {
        if (_loading) return;
        _inlineUseTraditionalOverride = value;
        InlineUseTraditionalIsOverridden = true;
    }

    partial void OnInlineUseAiEffectiveChanged(bool value)
    {
        if (_loading) return;
        _inlineUseAiOverride = value;
        InlineUseAiIsOverridden = true;
    }

    /// <summary>Sem override explícito, a sugestão determinística automática segue o dicionário local, como no valor efetivo do modelo.</summary>
    partial void OnUseDictionaryChanged(bool value)
    {
        if (_loading || _inlineUseTraditionalOverride.HasValue) return;
        SetEffectiveWhileLoading(() => InlineUseTraditionalEffective = value);
    }

    [RelayCommand]
    private void ResetInlineEnabled()
    {
        _inlineEnabledOverride = null;
        InlineEnabledIsOverridden = false;
        SetEffectiveWhileLoading(() => InlineEnabledEffective = true);
    }

    [RelayCommand]
    private void ResetInlineUseTraditional()
    {
        _inlineUseTraditionalOverride = null;
        InlineUseTraditionalIsOverridden = false;
        SetEffectiveWhileLoading(() => InlineUseTraditionalEffective = UseDictionary);
    }

    [RelayCommand]
    private void ResetInlineUseAi()
    {
        _inlineUseAiOverride = null;
        InlineUseAiIsOverridden = false;
        SetEffectiveWhileLoading(() => InlineUseAiEffective = false);
    }

    /// <summary>Reassigns an "Effective" property without the change being mistaken for a fresh user override.</summary>
    private void SetEffectiveWhileLoading(Action assign)
    {
        var wasLoading = _loading;
        _loading = true;
        try { assign(); } finally { _loading = wasLoading; }
    }

    partial void OnModelPathChanged(string value)
    {
        if (_loading || string.IsNullOrWhiteSpace(value)) return;
        var path = value.Trim();
        if (SelectedModelOption is { IsExternal: true } current && current.Reference == path) return;
        SelectedModelOption = Models.FirstOrDefault(option => option.IsExternal && option.Reference == path) ?? Add(new(path, true));
    }

    partial void OnSelectedModelOptionChanged(LocalModelOption? value)
    {
        UpdateModelDetails();
        RefreshStatus();
        // A deliberate choice applies the model's recommended budgets; loading saved preferences never overrides them.
        if (!_loading && value?.Model?.Metadata is { } metadata)
        {
            if (metadata.RecommendedContextTokens is { } context) ContextTokens = context;
            if (metadata.RecommendedCompletionTokens is { } completion) MaximumTokens = completion;
        }
        ApplyRecommendedBudget();
        RefreshTokenBudget();
    }

    public void Load(AutocompleteSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _loaded = settings;
        _loading = true;
        try
        {
            Enabled = settings.Enabled; ModeIndex = (int)settings.Mode; HardwareIndex = (int)settings.Acceleration;
            ContextTokens = settings.ContextTokens; MaximumTokens = settings.MaximumCompletionTokens;
            DelayMilliseconds = settings.DelayMilliseconds; _executionProvider = settings.ExecutionProvider;
            UseDictionary = settings.UseDictionary; UseInputPanelContext = settings.UseInputPanelContext;
            UseResultPanelContext = settings.UseResultPanelContext; UseEditorContext = settings.UseEditorContext;
            IncrementalTab = settings.IncrementalTab; ChatEnabled = settings.ChatEnabled;
            LocalAiContextEnabled = settings.LocalAiContextEnabled; _chatModel = settings.ChatModel;
            IncludeInputJsonInLocalAiContext = settings.IncludeInputJsonInLocalAiContext;
            CompletionAutoOpenOnTrigger = settings.CompletionAutoOpenOnTrigger; CompletionEnterAccepts = settings.CompletionEnterAccepts;
            _inlineEnabledOverride = settings.InlineEnabledValue; InlineEnabledIsOverridden = _inlineEnabledOverride.HasValue;
            InlineEnabledEffective = settings.InlineEnabled;
            _inlineUseTraditionalOverride = settings.InlineUseTraditionalValue; InlineUseTraditionalIsOverridden = _inlineUseTraditionalOverride.HasValue;
            InlineUseTraditionalEffective = settings.InlineUseTraditional;
            _inlineUseAiOverride = settings.InlineUseAiValue; InlineUseAiIsOverridden = _inlineUseAiOverride.HasValue;
            InlineUseAiEffective = settings.InlineUseAi;
            ModelDirectory = settings.ModelDirectory; ModelPath = settings.ModelPath;
            Models.Clear();
            SelectedModelOption = settings.SelectedModel.Length > 0 ? Add(new(settings.SelectedModel, false))
                : !string.IsNullOrWhiteSpace(settings.ModelPath) ? Add(new(settings.ModelPath.Trim(), true)) : null;
            TestReport = "";
            LoadBudgetSettings(settings);
        }
        finally { _loading = false; }
        UpdateModelDetails();
        RefreshStatus();
        RefreshTokenBudget();
    }

    public void RefreshStatus()
    {
        var hardware = Enum.IsDefined((AiAccelerationMode)HardwareIndex) ? (AiAccelerationMode)HardwareIndex : AiAccelerationMode.Auto;
        Status = LocalAiStatusFormatter.Format(service.Status, SelectedModelOption is { } option ? option.Model?.Name ?? option.FolderName : null, hardware, _hardware, LocalizationViewModel.Current.Resolve);
    }

    public AutocompleteSettings Snapshot()
    {
        CommitTokenEditors();
        RefreshTokenBudget();
        if (HasTokenBudgetError) throw new ArgumentException(TokenBudgetWarning);
        var option = SelectedModelOption;
        // Start from the loaded settings so fields without a control here (list flags) keep their saved values.
        return (_loaded with
        {
            Enabled = Enabled, Mode = (AutocompleteMode)ModeIndex, Acceleration = (AiAccelerationMode)HardwareIndex,
            ModelDirectory = ModelDirectory.Trim(), SelectedModel = option is { IsExternal: false } ? option.Reference : "",
            ModelPath = option is { IsExternal: true } ? option.Reference : "", ChatModel = _chatModel, ChatEnabled = ChatEnabled,
            LocalAiContextEnabled = LocalAiContextEnabled,
            IncludeInputJsonInLocalAiContext = IncludeInputJsonInLocalAiContext,
            ContextTokens = ContextTokens, MaximumCompletionTokens = MaximumTokens,
            HardwareProfilesJson = SnapshotHardwareProfiles(),
            SelectedHardwareProfile = _hardwareProfileLocked ? SelectedHardwareProfile?.Name ?? "" : "",
            DelayMilliseconds = DelayMilliseconds, ExecutionProvider = _executionProvider,
            UseDictionary = UseDictionary, UseInputPanelContext = UseInputPanelContext,
            UseResultPanelContext = UseResultPanelContext, UseEditorContext = UseEditorContext, IncrementalTab = IncrementalTab,
            CompletionAutoOpenOnTrigger = CompletionAutoOpenOnTrigger, CompletionEnterAccepts = CompletionEnterAccepts,
            InlineEnabledValue = _inlineEnabledOverride, InlineUseTraditionalValue = _inlineUseTraditionalOverride,
            InlineUseAiValue = _inlineUseAiOverride
        }).Validate();
    }

    /// <summary>Scans the directory and detects hardware when the window opens; the loaded model is not touched.</summary>
    public Task OpenedAsync() => Task.WhenAll(RefreshModelsCommand.ExecuteAsync(null), DetectHardwareCommand.ExecuteAsync(null));

    public void RefreshLanguage()
    {
        foreach (var option in Models) option.RefreshLanguage();
        foreach (var option in RemoteModels) option.RefreshLanguage();
        OnPropertyChanged(nameof(Modes));
        OnPropertyChanged(nameof(HardwareOptions));
        OnPropertyChanged(nameof(ModelDetails));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(OperationStatus));
        OnPropertyChanged(nameof(DetectedHardware));
    }

    /// <summary>A folder directly inside the models directory is stored by name; any other folder stays external.</summary>
    public async Task SelectExternalModelAsync(string path)
    {
        string full;
        try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception) { OperationStatus = T("invalidModelPath"); return; }
        var directory = EffectiveModelDirectory;
        if (directory.Length > 0 && string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)), PathComparison))
        {
            var name = Path.GetFileName(full);
            SelectedModelOption = Models.FirstOrDefault(option => !option.IsExternal && string.Equals(option.Reference, name, PathComparison)) ?? Add(new(name, false));
            await RefreshModelsCommand.ExecuteAsync(null);
            return;
        }
        ModelPath = full;
        if (SelectedModelOption is not { IsExternal: true } external || Validate(full) is not { } validation) return;
        try
        {
            external.Validation = await validation;
            UpdateModelDetails();
            RefreshStatus();
            OperationStatus = external.Model is { } model ? F("externalModelValid", model.Name)
                : F("externalFolderInvalid", external.Validation.Status.Message);
        }
        catch (Exception) { OperationStatus = T("validateExternalFailed"); }
    }

    [RelayCommand]
    private async Task RefreshModelsAsync(CancellationToken cancellationToken)
    {
        if (catalog is null && models is null) return;
        var generation = ++_refreshGeneration;
        var directory = EffectiveModelDirectory;
        OperationStatus = F("searchingModels", directory);
        try
        {
            var found = catalog is not null ? await catalog.DiscoverAsync(directory, cancellationToken) : await models!.DiscoverModelsAsync(directory, cancellationToken);
            var selected = SelectedModelOption;
            var external = selected is { IsExternal: true } && Validate(selected.Reference) is { } validation ? await validation : null;
            if (generation != _refreshGeneration) return;
            _loading = true;
            try
            {
                Models.Clear();
                foreach (var model in found.Where(candidate => candidate.Model is not null)) Models.Add(new(FolderOf(model.Path), false, model));
                LocalModelOption? restored = null;
                if (selected is { IsExternal: true }) restored = Add(new(selected.Reference, true, external));
                else if (selected is not null)
                    restored = Models.FirstOrDefault(option => string.Equals(option.Reference, selected.Reference, PathComparison))
                        ?? Add(new(selected.Reference, false, found.FirstOrDefault(candidate => string.Equals(FolderOf(candidate.Path), selected.Reference, PathComparison))
                            ?? new LocalModelValidation(null, new(LocalModelState.NotInstalled, F("modelNotFound", selected.Reference, directory)))));
                SelectedModelOption = restored;
            }
            finally { _loading = false; }
            UpdateModelDetails();
            RefreshStatus();
            ApplyRecommendedBudget();
            RefreshTokenBudget();
            var invalid = found.Where(candidate => candidate.Model is null).ToArray();
            ModelDiagnostics = invalid.Length == 0 ? string.Empty
                : F("ignoredFolders", string.Join("; ", invalid.Select(candidate => $"{FolderOf(candidate.Path)} — {LocalAiStatusFormatter.ValidityLabel(candidate.Validity, LocalizationViewModel.Current.Resolve)}")));
            OperationStatus = found.Count == 0 && !Directory.Exists(directory) ? F("modelsDirectoryMissing", directory)
                : F("modelsFound", found.Count - invalid.Length) + (invalid.Length > 0 ? F("ignoredFoldersCount", invalid.Length) : ".");
        }
        catch (OperationCanceledException) { }
        catch (Exception) { OperationStatus = T("readModelsFailed"); }
    }

    [RelayCommand]
    private async Task DetectHardwareAsync(CancellationToken cancellationToken)
    {
        if (models is null) { DetectedHardware = T("hardwareDetectionUnavailable"); return; }
        DetectedHardware = T("queryingOnnx");
        try { _hardware = await models.GetAvailableHardwareAsync(cancellationToken); }
        catch (OperationCanceledException) { return; }
        catch (Exception) { DetectedHardware = T("onnxRuntimeFailed"); return; }
        foreach (var option in HardwareOptions.Where(option => option.Mode != AiAccelerationMode.Auto))
        {
            var device = _hardware.FirstOrDefault(device => device.Kind == option.Mode && device.IsAvailable);
            option.IsAvailable = device is not null;
            option.Label = device is null ? LocalAiStatusFormatter.HardwareLabel(option.Mode, LocalizationViewModel.Current.Resolve) + " — " + T("unavailable") : LocalAiStatusFormatter.DeviceLine(device, LocalizationViewModel.Current.Resolve);
        }
        DetectedHardware = string.Join("\n", _hardware.Select(device => LocalAiStatusFormatter.DeviceLine(device, LocalizationViewModel.Current.Resolve)));
        SelectDetectedProfile();
        // GPU exports are marked only after the runtime reported which devices exist.
        RefreshInstalledRemoteModels();
        RefreshStatus();
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        IsBusy = true;
        try { await save(Snapshot()); OperationStatus = T("autocompleteSaved"); RefreshStatus(); }
        catch (ArgumentException ex) { OperationStatus = ex.Message; }
        catch (Exception) { OperationStatus = T("autocompleteNotSaved"); }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task TestAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            await save(Snapshot());
            OperationStatus = T("testingModel");
            TestReport = "";
            var report = await service.RunModelTestAsync(cancellationToken);
            TestReport = LocalAiStatusFormatter.FormatReport(report, LocalizationViewModel.Current.Resolve);
            OperationStatus = report.Message;
            RefreshStatus();
        }
        catch (OperationCanceledException) { OperationStatus = T("testCancelled"); }
        catch (ArgumentException ex) { OperationStatus = ex.Message; }
        catch (LocalModelUnavailableException ex) { OperationStatus = ex.Message; }
        catch (Exception) { OperationStatus = T("testIncomplete"); }
        finally { IsBusy = false; }
    }

    private Task<LocalModelValidation>? Validate(string path) => catalog?.ValidateAsync(path) ?? models?.ValidateModelAsync(path);

    private LocalModelOption Add(LocalModelOption option)
    {
        Models.Add(option);
        return option;
    }

    private static string FolderOf(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    private void UpdateModelDetails()
    {
        var option = SelectedModelOption;
        if (option?.Model is not { } model)
        {
            ModelDetails = option?.Validation is { Model: null } rejected ? rejected.Status.Message : "";
            return;
        }
        var metadata = model.Metadata;
        var parts = new List<string> { model.Architecture };
        if (metadata?.Parameters is { } parameters) parts.Add(parameters);
        if (metadata?.Version is { } version) parts.Add(F("modelVersion", version));
        parts.Add(F("modelCapabilities", CapabilityText(model.Capabilities)));
        if (metadata is { Domain.Count: > 0 }) parts.Add(F("modelDomain", string.Join(", ", metadata.Domain)));
        if (metadata?.Hardware is { } hardware) parts.Add(F("declaredHardware", string.Join(", ", hardware.Select(mode => LocalAiStatusFormatter.HardwareLabel(mode, LocalizationViewModel.Current.Resolve)))));
        parts.Add(model.Path);
        ModelDetails = string.Join(" · ", parts);
    }

    private static string CapabilityText(LocalModelCapabilities capabilities)
    {
        var names = new (LocalModelCapabilities Flag, string Name)[]
        {
            (LocalModelCapabilities.Autocomplete, "autocomplete"), (LocalModelCapabilities.Chat, "chat"),
            (LocalModelCapabilities.Fim, "FIM"), (LocalModelCapabilities.Embeddings, "embeddings")
        }.Where(entry => capabilities.HasFlag(entry.Flag)).Select(entry => entry.Name).ToArray();
        return names.Length == 0 ? T("none") : string.Join(", ", names);
    }
}
