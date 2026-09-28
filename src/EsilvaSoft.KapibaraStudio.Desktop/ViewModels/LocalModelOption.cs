using EsilvaSoft.KapibaraStudio.LocalAi.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>A model in preferences. Catalog entries persist only the folder name; external entries keep their path.</summary>
public sealed partial class LocalModelOption(string reference, bool isExternal, LocalModelValidation? validation = null) : ObservableObject
{
    public string Reference { get; } = reference;
    public bool IsExternal { get; } = isExternal;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(Display), nameof(Title), nameof(Subtitle), nameof(HasSubtitle), nameof(Model))]
    private LocalModelValidation? _validation = validation;
    public LocalModelDefinition? Model => Validation?.Model;
    public string FolderName => IsExternal ? Path.GetFileName(Path.TrimEndingDirectorySeparator(Reference)) : Reference;

    /// <summary>
    /// Family, hardware and precision when the folder or metadata follows the SlopCoder naming ("SlopCoder-Mongo-0.5B — CPU INT4");
    /// otherwise the metadata name or folder. Unusable folders are labeled, never hidden when selected.
    /// </summary>
    public string Title
    {
        get
        {
            var state = Validation is null || Model is not null ? ""
                : Validation.Status.State == LocalModelState.NotInstalled ? " — " + LocalizationViewModel.Current.Resolve("notFound")
                : " — " + LocalAiStatusFormatter.ValidityLabel(Validation.Validity, LocalizationViewModel.Current.Resolve);
            var name = ModelDisplayNames.ForInstalled(FolderName, Model?.Metadata) ?? Model?.Name ?? FolderName;
            return name + (IsExternal ? " — " + LocalizationViewModel.Current.Resolve("external") : "") + state;
        }
    }

    /// <summary>Parameters, architecture and the folder that identifies the model.</summary>
    public string Subtitle => Model is not { } model ? ""
        : string.Join(" · ", new[] { model.Metadata?.Parameters, model.Architecture, LocalizationViewModel.Current.Format("modelFolder", FolderName) }.Where(part => !string.IsNullOrEmpty(part)));

    public bool HasSubtitle => Subtitle.Length > 0;
    public string Display => Title;
    public override string ToString() => Display;

    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HasSubtitle));
        OnPropertyChanged(nameof(Display));
    }
}
