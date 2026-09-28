using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class AutocompleteSettingsViewModel
{
    private const int ProductContextMaximum = AutocompleteSettings.AbsoluteContextMaximum;
    private const int ProductCompletionMaximum = AutocompleteSettings.AbsoluteCompletionMaximum;
    private const int UnknownModelContextMaximum = 8192;
    private const int UnknownModelCompletionMaximum = 256;
    private readonly ObservableCollection<AiHardwareProfileSettings> _hardwareProfiles = [];
    private ReadOnlyObservableCollection<AiHardwareProfileSettings>? _hardwareProfilesView;
    private bool _hardwareProfileLocked;
    private bool _selectingDetectedProfile;
    private bool _tokenBudgetIsConservative;

    public ObservableCollection<string> ContextTokenSuggestions { get; } = [];
    public ObservableCollection<string> MaximumTokenSuggestions { get; } = [];
    public ReadOnlyObservableCollection<AiHardwareProfileSettings> HardwareProfiles => _hardwareProfilesView ??= new(_hardwareProfiles);
    public bool HasSelectedHardwareProfile => SelectedHardwareProfile is not null;
    public bool HasTokenBudgetWarning => TokenBudgetWarning.Length > 0;
    public bool HasTokenBudgetError => TokenBudgetWarning.Length > 0 && !_tokenBudgetIsConservative;
    public string HardwareProfileSummary => SelectedHardwareProfile is { } profile
        ? F("hardwareProfileSummary", profile.Vendor, profile.Name, profile.MemoryBytes / 1024d / 1024d / 1024d, AiHardwareTiers.Label(AiHardwareTiers.For(profile.MemoryBytes)))
        : T("hardwareDetectionWaiting");

    [ObservableProperty] private string _contextTokensText = "2048";
    [ObservableProperty] private string _maximumTokensText = "32";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasTokenBudgetWarning), nameof(HasTokenBudgetError))] private string _tokenBudgetWarning = "";
    [ObservableProperty, NotifyPropertyChangedFor(nameof(HasSelectedHardwareProfile), nameof(HardwareProfileSummary))]
    private AiHardwareProfileSettings? _selectedHardwareProfile;
    [ObservableProperty] private string _hardwareProfileVendor = "";
    [ObservableProperty] private string _hardwareProfileName = "";
    [ObservableProperty] private string _hardwareProfileMemoryGiB = "";

    partial void OnContextTokensTextChanged(string value)
    {
        if (AutocompleteTokenBudget.TryParse(value, out var parsed)) ContextTokens = parsed;
        RefreshTokenBudget();
    }

    partial void OnMaximumTokensTextChanged(string value)
    {
        if (AutocompleteTokenBudget.TryParse(value, out var parsed)) MaximumTokens = parsed;
        RefreshTokenBudget();
    }

    partial void OnContextTokensChanged(int value)
    {
        if (ContextTokensText != AutocompleteTokenBudget.Format(value)) ContextTokensText = AutocompleteTokenBudget.Format(value);
        RefreshTokenBudget();
    }

    partial void OnMaximumTokensChanged(int value)
    {
        if (MaximumTokensText != AutocompleteTokenBudget.Format(value)) MaximumTokensText = AutocompleteTokenBudget.Format(value);
        RefreshTokenBudget();
    }

    partial void OnSelectedHardwareProfileChanged(AiHardwareProfileSettings? value)
    {
        if (!_loading && value is not null && !_selectingDetectedProfile) _hardwareProfileLocked = true;
        ApplyRecommendedBudget();
        RefreshTokenBudget();
    }

    /// <summary>Commits the editable ComboBox text before save/test, including values entered without selecting a suggestion.</summary>
    public void CommitTokenEditors()
    {
        if (AutocompleteTokenBudget.TryParse(ContextTokensText, out var context)) ContextTokens = context;
        if (AutocompleteTokenBudget.TryParse(MaximumTokensText, out var completion)) MaximumTokens = completion;
        RefreshTokenBudget();
    }

    [RelayCommand]
    private void AddHardwareProfile()
    {
        if (string.IsNullOrWhiteSpace(HardwareProfileName) || !double.TryParse(HardwareProfileMemoryGiB, out var gib) || gib <= 0 || gib > 1024)
        {
            OperationStatus = T("hardwareProfileRequired");
            return;
        }
        var profile = new AiHardwareProfileSettings(
            string.IsNullOrWhiteSpace(HardwareProfileVendor) ? "GPU" : HardwareProfileVendor.Trim(),
            HardwareProfileName.Trim(),
            (long)(gib * 1024 * 1024 * 1024));
        _hardwareProfiles.Remove(_hardwareProfiles.FirstOrDefault(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase))!);
        _hardwareProfiles.Add(profile);
        SelectedHardwareProfile = profile;
        HardwareProfileVendor = HardwareProfileName = HardwareProfileMemoryGiB = "";
        OperationStatus = F("hardwareProfileAdded", profile.Vendor, profile.Name);
    }

    [RelayCommand]
    private void RemoveHardwareProfile()
    {
        if (SelectedHardwareProfile is not { } profile) return;
        _hardwareProfiles.Remove(profile);
        _hardwareProfileLocked = false;
        SelectedHardwareProfile = null;
        OperationStatus = T("hardwareProfileRemoved");
    }

    private void LoadBudgetSettings(AutocompleteSettings settings)
    {
        _hardwareProfiles.Clear();
        foreach (var preset in AiHardwareTiers.Presets) _hardwareProfiles.Add(preset);
        try
        {
            var profiles = string.IsNullOrWhiteSpace(settings.HardwareProfilesJson)
                ? []
                : JsonSerializer.Deserialize<AiHardwareProfileSettings[]>(settings.HardwareProfilesJson) ?? [];
            foreach (var profile in profiles.Where(IsValidProfile).Where(profile => !_hardwareProfiles.Any(existing => string.Equals(existing.Name, profile.Name, StringComparison.OrdinalIgnoreCase))))
                _hardwareProfiles.Add(profile);
        }
        catch (JsonException) { }
        SelectedHardwareProfile = _hardwareProfiles.FirstOrDefault(profile => string.Equals(profile.Name, settings.SelectedHardwareProfile, StringComparison.OrdinalIgnoreCase));
        _hardwareProfileLocked = !string.IsNullOrWhiteSpace(settings.SelectedHardwareProfile);
        ContextTokensText = settings.ContextTokens.ToString(CultureInfo.InvariantCulture);
        MaximumTokensText = settings.MaximumCompletionTokens.ToString(CultureInfo.InvariantCulture);
        RefreshTokenBudget();
    }

    private void SelectDetectedProfile()
    {
        if (_hardwareProfileLocked) return;
        var memory = DetectMemoryBytes();
        var tier = AiHardwareTiers.For(memory);
        _selectingDetectedProfile = true;
        try
        {
            SelectedHardwareProfile = _hardwareProfiles.FirstOrDefault(profile => AiHardwareTiers.For(profile.MemoryBytes) == tier)
                ?? AiHardwareTiers.Presets[0];
        }
        finally { _selectingDetectedProfile = false; }
        ApplyRecommendedBudget();
    }

    private void ApplyRecommendedBudget()
    {
        if (_loading || SelectedHardwareProfile is not { } profile) return;
        var recommendation = AiHardwareTiers.Recommendation(AiHardwareTiers.For(profile.MemoryBytes));
        var model = SelectedModelOption?.Model;
        var maximumCompletion = Math.Min(ProductCompletionMaximum, model?.AutocompleteMaximumTokens ?? UnknownModelCompletionMaximum);
        var completion = Math.Min(recommendation.CompletionTokens, maximumCompletion);
        var maximumContext = Math.Min(ProductContextMaximum, model?.ContextLength ?? UnknownModelContextMaximum);
        maximumContext = Math.Max(64, maximumContext - completion - AutocompleteTokenBudget.PromptOverheadTokens);
        var context = Math.Min(recommendation.ContextTokens, maximumContext);
        if (model?.Metadata?.RecommendedContextTokens is { } recommendedContext) context = Math.Min(context, recommendedContext);
        if (model?.Metadata?.RecommendedCompletionTokens is { } recommendedCompletion) completion = Math.Min(completion, recommendedCompletion);
        completion = Math.Min(completion, Math.Max(1, maximumContext - context));
        SetEffectiveWhileLoading(() =>
        {
            ContextTokens = Math.Max(64, context);
            MaximumTokens = Math.Max(1, completion);
        });
    }

    private void RefreshTokenBudget()
    {
        var suggestions = BuildSuggestions();
        Replace(ContextTokenSuggestions, suggestions.Context);
        Replace(MaximumTokenSuggestions, suggestions.Completion);
        var contextIsValid = AutocompleteTokenBudget.TryParse(ContextTokensText, out var parsedContext);
        var completionIsValid = AutocompleteTokenBudget.TryParse(MaximumTokensText, out var parsedCompletion);
        var context = contextIsValid ? parsedContext : 0;
        var completion = completionIsValid ? parsedCompletion : 0;
        var modelLimit = SelectedModelOption?.Model?.ContextLength;
        var effectiveContext = modelLimit ?? UnknownModelContextMaximum;
        var maximumContext = Math.Min(ProductContextMaximum, effectiveContext);
        var maximumCompletion = Math.Min(ProductCompletionMaximum, SelectedModelOption?.Model?.AutocompleteMaximumTokens ?? UnknownModelCompletionMaximum);
        _tokenBudgetIsConservative = false;
        TokenBudgetWarning = !contextIsValid
            ? T("contextTokensDigitsOnly")
            : !completionIsValid
                ? T("completionTokensDigitsOnly")
                : context < 64 || context > maximumContext
                    ? F("contextTokensRange", AutocompleteTokenBudget.Format(maximumContext))
                    : completion < 1 || completion > maximumCompletion
                        ? F("completionTokensRange", AutocompleteTokenBudget.Format(maximumCompletion))
                            : (long)context + completion + AutocompleteTokenBudget.PromptOverheadTokens > effectiveContext
                            ? F("tokenWindowExceeded", AutocompleteTokenBudget.Format(effectiveContext))
                            : SelectedModelOption?.Model?.ContextLength is null
                                ? SetConservativeWarning()
                                : "";
    }

    private string SetConservativeWarning()
    {
        _tokenBudgetIsConservative = true;
        return F("conservativeTokenWindow", UnknownModelContextMaximum);
    }

    private (IReadOnlyList<string> Context, IReadOnlyList<string> Completion) BuildSuggestions()
    {
        var model = SelectedModelOption?.Model;
        var memory = SelectedHardwareProfile?.MemoryBytes ?? DetectMemoryBytes();
        var usable = Math.Max(0, memory * 0.70 - (model?.ModelSizeBytes ?? 0));
        var tier = AiHardwareTiers.For(memory);
        var memoryContext = tier switch
        {
            AiHardwareTier.NoAccelerator => 512,
            AiHardwareTier.Basic => 1024,
            AiHardwareTier.Intermediate => 2048,
            AiHardwareTier.Advanced => 4096,
            _ => 8192
        };
        if (usable < 512L * 1024 * 1024) memoryContext = Math.Min(memoryContext, 512);
        var effectiveContext = Math.Min(ProductContextMaximum, model?.ContextLength ?? UnknownModelContextMaximum);
        var selectedCompletion = AutocompleteTokenBudget.TryParse(MaximumTokensText, out var parsedCompletion)
            ? parsedCompletion
            : Math.Min(AiHardwareTiers.Recommendation(tier).CompletionTokens, Math.Min(ProductCompletionMaximum, model?.AutocompleteMaximumTokens ?? UnknownModelCompletionMaximum));
        var maxContext = Math.Min(effectiveContext - selectedCompletion - AutocompleteTokenBudget.PromptOverheadTokens, memoryContext);
        maxContext = Math.Max(0, maxContext);
        var recommendedContext = model?.Metadata?.RecommendedContextTokens;
        var context = new HashSet<int>(new[] { 64, 128, 256, 512, 1024, 2048, 4096, 8192, recommendedContext ?? 0 }.Where(value => value >= 64 && value <= maxContext));
        var currentContext = AutocompleteTokenBudget.TryParse(ContextTokensText, out var parsedContext) ? parsedContext : 0;
        var maxCompletion = Math.Min(ProductCompletionMaximum, model?.AutocompleteMaximumTokens ?? UnknownModelCompletionMaximum);
        maxCompletion = Math.Min(maxCompletion, Math.Max(0, effectiveContext - currentContext - AutocompleteTokenBudget.PromptOverheadTokens));
        var recommendedCompletion = model?.Metadata?.RecommendedCompletionTokens;
        var completion = new HashSet<int>(new[] { 1, 8, 16, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, recommendedCompletion ?? 0 }.Where(value => value >= 1 && value <= maxCompletion));
        return (context.OrderBy(value => value).Select(AutocompleteTokenBudget.Format).ToArray(), completion.OrderBy(value => value).Select(AutocompleteTokenBudget.Format).ToArray());
    }

    private long DetectMemoryBytes()
    {
        var selectedMode = Enum.IsDefined((AiAccelerationMode)HardwareIndex) ? (AiAccelerationMode)HardwareIndex : AiAccelerationMode.Auto;
        if (selectedMode == AiAccelerationMode.Cpu)
            return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        var gpu = _hardware.FirstOrDefault(device => device.Kind == AiAccelerationMode.Gpu && device.IsAvailable && device.MemoryBytes is > 0);
        return gpu?.MemoryBytes ?? GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
    }

    private static bool IsValidProfile(AiHardwareProfileSettings profile) =>
        !string.IsNullOrWhiteSpace(profile.Name) && profile.MemoryBytes > 0 && profile.MemoryBytes <= 1024L * 1024 * 1024 * 1024;

    private static void Replace(ObservableCollection<string> target, IEnumerable<string> values)
    {
        var next = values.ToArray();
        if (target.SequenceEqual(next, StringComparer.Ordinal)) return;
        target.Clear();
        foreach (var value in next) target.Add(value);
    }

    private void UpdateBudgetModelDetails()
    {
        RefreshTokenBudget();
    }

    private string SnapshotHardwareProfiles()
    {
        var custom = _hardwareProfiles.Where(profile => !string.Equals(profile.Vendor, "Tier", StringComparison.Ordinal)).ToArray();
        return custom.Length == 0 ? "" : JsonSerializer.Serialize(custom);
    }
}
