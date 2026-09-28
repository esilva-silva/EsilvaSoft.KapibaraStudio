using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using EsilvaSoft.KapibaraStudio.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>A downloadable variant; installed means a folder with its name already exists in the models directory.</summary>
public sealed class RemoteModelOption(RemoteModelVariant variant, bool isInstalled, bool hardwareMissing = false) : ObservableObject
{
    public RemoteModelVariant Variant { get; } = variant;
    public bool IsInstalled { get; } = isInstalled;

    /// <summary>The variant needs a GPU and the runtime reported none available on this machine.</summary>
    public bool HardwareMissing { get; } = hardwareMissing;

    public string Title => Variant.Title;

    public string Subtitle => string.Join(" · ", new[]
    {
        AutocompleteSettingsViewModel.FormatSize(Variant.SizeBytes), Variant.HintKey is { Length: > 0 } key ? LocalizationViewModel.Current.Resolve(key) : Variant.Hint,
        HardwareMissing ? LocalizationViewModel.Current.Resolve("gpuNotDetected") : null, IsInstalled ? LocalizationViewModel.Current.Resolve("installed") : null
    }.Where(part => !string.IsNullOrEmpty(part)));

    public string Display => $"{Title} · {AutocompleteSettingsViewModel.FormatSize(Variant.SizeBytes)}" + (IsInstalled ? " — " + LocalizationViewModel.Current.Resolve("installed") : "");
    public override string ToString() => Display;

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(Display));
    }
}
