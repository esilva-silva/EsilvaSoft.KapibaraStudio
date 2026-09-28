using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class ConnectionsViewModel : ObservableObject
{
    public MainWindowViewModel Editor { get; }
    public ObservableCollection<ConnectionChoice> Choices { get; } = [];
    public bool HasNoMatches => Choices.Count == 0;
    [ObservableProperty] private string _search = "";
    [ObservableProperty] private ConnectionChoice? _selectedChoice;
    [ObservableProperty] private bool _isOpening;
    [ObservableProperty] private string _error = "";

    public UuidPreferenceViewModel? Uuid { get; }

    public ConnectionsViewModel(WorkspaceService workspace, WorkspaceViewModel? owner = null)
    {
        Editor = new MainWindowViewModel(workspace, autoLoadCollections: false);
        Editor.Profiles.CollectionChanged += (_, _) => Filter();
        Editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Editor.SelectedProfile) && Editor.SelectedProfile is { } profile)
                SelectedChoice = Choices.FirstOrDefault(c => c.Profile.Id == profile.Id);
        };
        if (owner is not null)
        {
            var uuid = new UuidPreferenceViewModel(LocalizationViewModel.Current.Resolve("connectionUuidTitle"), allowInherit: true, () => owner.UuidRepresentation);
            Uuid = uuid;
            Editor.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Editor.IsProfileEditorVisible) && Editor.IsProfileEditorVisible)
                    uuid.Load(owner.GetProfileUuidRepresentation(Editor.ProfileEditorSourceId));
            };
            // The override is a workspace preference, so saving it never invalidates the explorer like a profile change.
            Editor.ProfileSaved = profile => owner.SetProfileUuidRepresentationAsync(profile.Id, uuid.Value);
        }
        Filter();
    }

    partial void OnSearchChanged(string value) => Filter();
    partial void OnSelectedChoiceChanged(ConnectionChoice? value) => Editor.SelectedProfile = value?.Profile;
    private void Filter()
    {
        var selected = SelectedChoice?.Profile.Id;
        Choices.Clear();
        foreach (var profile in Editor.Profiles.OrderByDescending(p => p.IsFavorite).ThenBy(p => p.Folder).ThenBy(p => p.Name))
        {
            var choice = new ConnectionChoice(profile);
            if ($"{choice.Name} {choice.Details} {choice.Endpoint}".Contains(Search, StringComparison.CurrentCultureIgnoreCase)) Choices.Add(choice);
        }
        SelectedChoice = Choices.FirstOrDefault(c => c.Profile.Id == selected) ?? Choices.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoMatches));
    }
}
