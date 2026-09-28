using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Global identifier mode (Standard, ObjectId or UUID v4) with a preview adapted to the selected mode.</summary>
public sealed partial class IdentifierPreferenceViewModel : ObservableObject
{
    /// <summary>UUID v4 sample (version 4, RFC 4122 variant) for the UUID section of the preview.</summary>
    public static Guid SampleUuidV4 { get; } = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
    private readonly Func<UuidRepresentation> _uuid;
    private readonly Func<IdentifierRepresentationMode, Task>? _apply;
    private readonly LocalizationViewModel _localization = LocalizationViewModel.Current;
    private bool _loading;

    /// <param name="uuid">Current global UUID representation, used by the UUID section of the preview.</param>
    /// <param name="apply">Called on each user selection to apply and persist the mode.</param>
    public IdentifierPreferenceViewModel(Func<UuidRepresentation> uuid, Func<IdentifierRepresentationMode, Task>? apply = null)
    {
        _uuid = uuid; _apply = apply;
        foreach (var mode in IdentifierRepresentationService.All) Choices.Add(new(mode, ChoiceLabel(mode)));
        Load(IdentifierRepresentationMode.Standard);
    }

    public string Title => _localization.Resolve("identifierModeTitle");
    public ObservableCollection<IdentifierModeChoice> Choices { get; } = [];
    public ObservableCollection<IdentifierPreviewRow> ObjectIdPreview { get; } = [];
    public ObservableCollection<IdentifierPreviewRow> UuidPreview { get; } = [];
    [ObservableProperty] private IdentifierModeChoice? _selectedChoice;
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _isObjectIdPreviewVisible;
    [ObservableProperty] private bool _isUuidPreviewVisible;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasError;
    public IdentifierRepresentationMode Mode => SelectedChoice?.Value ?? IdentifierRepresentationMode.Standard;
    public Task ApplyTask { get; private set; } = Task.CompletedTask;

    public void Load(IdentifierRepresentationMode mode)
    {
        _loading = true;
        SelectedChoice = Choices.FirstOrDefault(choice => choice.Value == mode) ?? Choices[0];
        _loading = false;
        Status = ""; HasError = false;
        RefreshPreview();
    }

    /// <summary>Rebuilds the preview; also called when the UUID representation changes.</summary>
    public void RefreshPreview()
    {
        var mode = Mode;
        Description = DescriptionFor(mode);
        IsObjectIdPreviewVisible = mode is IdentifierRepresentationMode.Standard or IdentifierRepresentationMode.ObjectId;
        IsUuidPreviewVisible = mode is IdentifierRepresentationMode.Standard or IdentifierRepresentationMode.UuidV4;
        ObjectIdPreview.Clear();
        if (IsObjectIdPreviewVisible)
        {
            var hex = IdentifierRepresentationService.SampleObjectId;
            ObjectIdPreview.Add(new(T("objectIdLabel"), IdentifierRepresentationService.FormatObjectId(hex), T("objectIdBsonType")));
            ObjectIdPreview.Add(new(T("hexLabel"), hex, T("hexDigits")));
            ObjectIdPreview.Add(new(T("equivalentUuidLabel"), IdentifierRepresentationService.ObjectIdToUuid(hex).ToString("D"),
                T("objectIdBytesNote")));
        }
        UuidPreview.Clear();
        if (IsUuidPreviewVisible)
        {
            var representation = _uuid();
            UuidPreview.Add(new(T("uuidV4Label"), IdentifierRepresentationService.FormatUuid(SampleUuidV4, representation),
                LocalizationViewModel.Current.Format("uuidPreviewDetails", UuidCodec.DisplayName(representation), UuidCodec.SubType(representation))));
        }
    }

    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string ChoiceLabel(IdentifierRepresentationMode mode) => mode switch
    {
        IdentifierRepresentationMode.Standard => T("identifierStandardChoice"),
        IdentifierRepresentationMode.ObjectId => T("identifierObjectIdChoice"),
        IdentifierRepresentationMode.UuidV4 => T("identifierUuidChoice"),
        _ => IdentifierRepresentationService.DisplayName(mode)
    };
    private static string DescriptionFor(IdentifierRepresentationMode mode) => mode switch
    {
        IdentifierRepresentationMode.Standard => T("identifierStandardDescription"),
        IdentifierRepresentationMode.ObjectId => T("identifierObjectIdDescription"),
        IdentifierRepresentationMode.UuidV4 => T("identifierUuidDescription"),
        _ => IdentifierRepresentationService.Description(mode)
    };

    partial void OnSelectedChoiceChanged(IdentifierModeChoice? value)
    {
        OnPropertyChanged(nameof(Mode));
        if (_loading) return;
        RefreshPreview();
        if (_apply is not null && value is not null) ApplyTask = ApplyAsync(value.Value);
    }

    private async Task ApplyAsync(IdentifierRepresentationMode mode)
    {
        try
        {
            await _apply!(mode);
            HasError = false; Status = LocalizationViewModel.Current.Resolve("identifierSaved");
        }
        catch (Exception exception)
        {
            HasError = true; Status = LocalizationViewModel.Current.Format("identifierSessionOnly", exception.Message);
        }
    }
}
