using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

/// <summary>Global or per-connection UUID choice, previewing one fixed UUID in the four IDE forms.</summary>
public sealed partial class UuidPreferenceViewModel : ObservableObject
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] args) => LocalizationViewModel.Current.Format(key, args);
    public static Guid SampleUuid { get; } = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
    private readonly Func<UuidRepresentation> _inherited;
    private readonly Func<UuidRepresentation?, Task>? _apply;
    private bool _loading;

    /// <param name="title">Accessible label of the choice.</param>
    /// <param name="allowInherit">Adds "use global preference" for per-connection overrides.</param>
    /// <param name="inherited">Current global representation used by the preview when nothing is overridden.</param>
    /// <param name="apply">Called on each user selection; omit it when the choice is saved together with another form.</param>
    public UuidPreferenceViewModel(string title, bool allowInherit, Func<UuidRepresentation> inherited, Func<UuidRepresentation?, Task>? apply = null)
    {
        Title = title; _inherited = inherited; _apply = apply;
        if (allowInherit) Choices.Add(new(null, "Usar preferência global"));
        foreach (var representation in UuidCodec.All)
            Choices.Add(new(representation, $"{UuidCodec.DisplayName(representation)} · {UuidCodec.Constructor(representation)}(…) · subtype {UuidCodec.SubType(representation)}"));
        Load(allowInherit ? null : inherited());
    }

    public string Title { get; }
    public ObservableCollection<UuidRepresentationChoice> Choices { get; } = [];
    public ObservableCollection<UuidPreviewRow> Preview { get; } = [];
    [ObservableProperty] private UuidRepresentationChoice? _selectedChoice;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _hasError;
    /// <summary>The four-form comparison is hidden in ObjectId identifier mode; the representation choice stays available.</summary>
    [ObservableProperty] private bool _isPreviewVisible = true;
    public UuidRepresentation? Value => SelectedChoice?.Value;
    public UuidRepresentation Effective => Value ?? _inherited();
    public Task ApplyTask { get; private set; } = Task.CompletedTask;

    public void Load(UuidRepresentation? value)
    {
        _loading = true;
        SelectedChoice = Choices.FirstOrDefault(choice => choice.Value == value) ?? Choices[0];
        _loading = false;
        Status = ""; HasError = false;
        RefreshPreview();
    }

    public void RefreshPreview()
    {
        Preview.Clear();
        var effective = Effective;
        foreach (var representation in UuidCodec.All)
            Preview.Add(new(representation, UuidCodec.FormatConstructor(SampleUuid, representation),
                $"{UuidCodec.DisplayName(representation)} · subtype {UuidCodec.SubType(representation)} · bytes {Convert.ToHexStringLower(UuidCodec.ToBytes(SampleUuid, representation))}",
                representation == effective));
    }

    partial void OnSelectedChoiceChanged(UuidRepresentationChoice? value)
    {
        OnPropertyChanged(nameof(Value)); OnPropertyChanged(nameof(Effective));
        if (_loading) return;
        RefreshPreview();
        if (_apply is not null) ApplyTask = ApplyAsync(value?.Value);
    }

    private async Task ApplyAsync(UuidRepresentation? value)
    {
        try
        {
            await _apply!(value);
            HasError = false; Status = T("uuidPreferenceSaved");
        }
        catch (Exception exception)
        {
            HasError = true; Status = F("uuidPreferenceSessionOnly", exception.Message);
        }
    }
}
