using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.SlopStudio.Application;
using EsilvaSoft.SlopStudio.Core;

namespace EsilvaSoft.SlopStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Guid? _editingProfileId;

    /// <summary>Invoked after a profile is persisted, for preferences stored outside the profile.</summary>
    public Func<ConnectionProfile, Task>? ProfileSaved { get; set; }
    /// <summary>Profile edited or duplicated by the open editor; null for a new profile.</summary>
    public Guid? ProfileEditorSourceId { get; private set; }



    public ObservableCollection<ConnectionProfile> Profiles { get; } = [];

    [ObservableProperty]
    private bool _isProfileEditorVisible;

    [ObservableProperty]
    private string _quickConnectionString = string.Empty;

    [ObservableProperty]
    private string _newProfileName = string.Empty;

    [ObservableProperty]
    private string _newProfileConnectionString = "mongodb://localhost:27017";

    [ObservableProperty]
    private string _newProfileDatabase = string.Empty;

    [ObservableProperty]
    private string _newProfileUsername = string.Empty;

    [ObservableProperty]
    private string _newProfilePassword = string.Empty;

    [ObservableProperty]
    private string _newProfileEnvironment = string.Empty;

    [ObservableProperty]
    private string _newProfileColor = string.Empty;

    [ObservableProperty]
    private string _newProfileTags = string.Empty;

    [ObservableProperty]
    private string _newProfileFolder = string.Empty;

    [ObservableProperty]
    private bool _newProfileIsReadOnly;

    [ObservableProperty]
    private bool _newProfileIsFavorite;

    // No UI and no consumer since the per-tab assistant was removed (ADR-055): editing a profile carries its persisted
    // value through unchanged; new profiles keep the historical default.
    [ObservableProperty]
    private bool _newProfileLocalAiContextEnabled = true;

    [RelayCommand]
    private async Task LoadProfilesAsync()
    {
        await RunAsync(async cancellationToken =>
        {
            var profiles = await _workspace.GetProfilesAsync(cancellationToken);
            Profiles.Clear();

            foreach (var profile in profiles)
            {
                Profiles.Add(profile);
            }

            SelectedProfile ??= Profiles.FirstOrDefault();
            StatusMessage = F("profilesLoaded", Profiles.Count);
        });
    }

    [RelayCommand]
    private void ShowProfileEditor()
    {
        NewProfileUsername = string.Empty;
        NewProfilePassword = string.Empty;
        _editingProfileId = null;
        ProfileEditorSourceId = null;
        NewProfileName = string.Empty;
        NewProfileConnectionString = "mongodb://localhost:27017";
        NewProfileDatabase = string.Empty;
        NewProfileEnvironment = string.Empty;
        NewProfileColor = string.Empty;
        NewProfileTags = string.Empty;
        NewProfileFolder = string.Empty;
        NewProfileIsReadOnly = false;
        NewProfileIsFavorite = false;
        NewProfileLocalAiContextEnabled = true;
        IsProfileEditorVisible = true;
    }

    [RelayCommand]
    private void StartProfileFromConnectionString()
    {
        NewProfileUsername = string.Empty;
        NewProfilePassword = string.Empty;
        try
        {
            var draft = ConnectionProfileDraft.FromConnectionString(QuickConnectionString);
            _editingProfileId = null;
            ProfileEditorSourceId = null;
            NewProfileName = draft.SuggestedName;
            NewProfileConnectionString = draft.ConnectionString;
            NewProfileDatabase = draft.DefaultDatabase ?? string.Empty;
            NewProfileEnvironment = string.Empty;
            NewProfileColor = string.Empty;
            NewProfileTags = string.Empty;
            NewProfileFolder = string.Empty;
            NewProfileIsReadOnly = false;
            NewProfileIsFavorite = false;
            NewProfileLocalAiContextEnabled = true;
            QuickConnectionString = string.Empty;
            IsProfileEditorVisible = true;
            StatusMessage = T("profileFromUri");
        }
        catch (ArgumentException exception)
        {
            SetError(DesktopOperationErrorMessages.Describe(exception));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private void EditSelectedProfile()
    {
        NewProfileUsername = string.Empty;
        NewProfilePassword = string.Empty;
        if (SelectedProfile is null)
        {
            return;
        }

        _editingProfileId = SelectedProfile.Id;
        ProfileEditorSourceId = SelectedProfile.Id;
        NewProfileName = SelectedProfile.Name;
        NewProfileConnectionString = SelectedProfile.ConnectionString;
        NewProfileDatabase = SelectedProfile.DefaultDatabase ?? string.Empty;
        NewProfileEnvironment = SelectedProfile.Environment ?? string.Empty;
        NewProfileColor = SelectedProfile.Color ?? string.Empty;
        NewProfileTags = SelectedProfile.Tags ?? string.Empty;
        NewProfileFolder = SelectedProfile.Folder ?? string.Empty;
        NewProfileIsReadOnly = SelectedProfile.IsReadOnly;
        NewProfileIsFavorite = SelectedProfile.IsFavorite;
        NewProfileLocalAiContextEnabled = SelectedProfile.LocalAiContextEnabled;
        IsProfileEditorVisible = true;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private void DuplicateSelectedProfile()
    {
        NewProfileUsername = string.Empty;
        NewProfilePassword = string.Empty;
        if (SelectedProfile is null)
        {
            return;
        }

        var copy = SelectedProfile.Duplicate($"{SelectedProfile.Name} - cópia");
        _editingProfileId = null;
        ProfileEditorSourceId = SelectedProfile.Id;
        NewProfileName = copy.Name;
        NewProfileConnectionString = copy.ConnectionString;
        NewProfileDatabase = copy.DefaultDatabase ?? string.Empty;
        NewProfileEnvironment = copy.Environment ?? string.Empty;
        NewProfileColor = copy.Color ?? string.Empty;
        NewProfileTags = copy.Tags ?? string.Empty;
        NewProfileFolder = copy.Folder ?? string.Empty;
        NewProfileIsReadOnly = copy.IsReadOnly;
        NewProfileIsFavorite = copy.IsFavorite;
        NewProfileLocalAiContextEnabled = copy.LocalAiContextEnabled;
        IsProfileEditorVisible = true;
        StatusMessage = T("duplicateProfileHint");
    }

    [RelayCommand]
    private void HideProfileEditor()
    {
        NewProfileUsername = string.Empty;
        NewProfilePassword = string.Empty;
        _editingProfileId = null;
        IsProfileEditorVisible = false;
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task DeleteSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var profile = SelectedProfile;
        if (!await RunAsync(cancellationToken => _workspace.DeleteProfileAsync(profile.Id, cancellationToken)))
        {
            return;
        }

        Profiles.Remove(profile);
        SelectedProfile = Profiles.FirstOrDefault();
        StatusMessage = F("profileRemoved", profile.Name);
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        try
        {
            var connectionString = string.IsNullOrWhiteSpace(NewProfileUsername)
                ? NewProfileConnectionString
                : AddCredentials(NewProfileConnectionString, NewProfileUsername, NewProfilePassword);
            var profile = ConnectionProfile.Create(NewProfileName, connectionString, NewProfileDatabase, NewProfileIsReadOnly, NewProfileIsFavorite, NewProfileEnvironment, NewProfileColor, NewProfileTags, NewProfileFolder)
                with { LocalAiContextEnabled = NewProfileLocalAiContextEnabled };
            var source = Profiles.FirstOrDefault(existing => existing.Id == ProfileEditorSourceId);
            if (source?.SecretReference is not null && string.IsNullOrEmpty(NewProfilePassword) &&
                !string.Equals(connectionString, source.ConnectionString, StringComparison.Ordinal))
                throw new ArgumentException(T("profileCredentialReentryRequired"));
            if (source is not null && source.SecretReference is not null &&
                string.Equals(connectionString, source.ConnectionString, StringComparison.Ordinal) &&
                string.IsNullOrEmpty(NewProfilePassword))
                profile = profile with { SecretReference = source.SecretReference };
            var editingId = _editingProfileId;
            if (editingId is not null)
            {
                profile = profile with { Id = editingId.Value };
            }

            ConnectionProfile? savedProfile = null;
            var cleanupPending = false;
            if (!await RunAsync(async cancellationToken =>
            {
                await _workspace.SaveProfileAsync(profile, cancellationToken);
                savedProfile = (await _workspace.GetProfilesAsync(cancellationToken))
                    .Single(saved => saved.Id == profile.Id);
                cleanupPending = await _workspace.HasPendingProfileCredentialCleanupAsync(profile.Id, cancellationToken);
            }))
            {
                return;
            }
            // The repository returns the redacted URI and the versioned OS-store reference.
            profile = savedProfile!;

            if (editingId is null)
            {
                Profiles.Add(profile);
            }
            else
            {
                var existingIndex = Profiles.IndexOf(Profiles.First(existing => existing.Id == editingId.Value));
                Profiles[existingIndex] = profile;
            }

            SelectedProfile = profile;
            string? preferenceError = null;
            if (ProfileSaved is { } saved)
            {
                try { await saved(profile); }
                catch (Exception exception) { preferenceError = DesktopOperationErrorMessages.Describe(exception); }
            }
            NewProfileName = string.Empty;
            NewProfileConnectionString = "mongodb://localhost:27017";
            NewProfileDatabase = string.Empty;
            NewProfileUsername = string.Empty;
            NewProfilePassword = string.Empty;
            NewProfileEnvironment = string.Empty;
            NewProfileColor = string.Empty;
            NewProfileTags = string.Empty;
            NewProfileFolder = string.Empty;
            NewProfileIsReadOnly = false;
            NewProfileIsFavorite = false;
            NewProfileLocalAiContextEnabled = true;
            _editingProfileId = null;
            IsProfileEditorVisible = false;
            StatusMessage = (editingId is null ? T("profileSaved") : T("profileUpdated"))
                + (preferenceError is null ? string.Empty : " " + F("uuidPreferenceNotSaved", preferenceError))
                + (cleanupPending ? " " + T("profileCredentialCleanupPending") : string.Empty);
        }
        catch (ArgumentException exception)
        {
            SetError(DesktopOperationErrorMessages.Describe(exception));
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task TestSelectedConnectionAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var result = await _workspace.TestConnectionAsync(SelectedProfile, cancellationToken);
            if (result.IsSuccess)
            {
                var connectedProfile = SelectedProfile.MarkConnected();
                await _workspace.SaveProfileAsync(connectedProfile, cancellationToken);
                var profileIndex = Profiles.IndexOf(SelectedProfile);
                if (profileIndex >= 0)
                {
                    Profiles[profileIndex] = connectedProfile;
                }

                var selectedDatabase = SelectedDatabase;
                var selectedCollection = SelectedCollection;
                SelectedProfile = connectedProfile;
                SelectedDatabase = selectedDatabase;
                SelectedCollection = selectedCollection;
            }

            StatusMessage = result.IsSuccess
                ? F("connectedToMongo", result.ServerVersion ?? T("versionUnknown"), result.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture))
                : F("connectionFailure", result.Message);
        });
    }

    private static string AddCredentials(string connectionString, string username, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        var schemeEnd = connectionString.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            throw new ArgumentException(T("invalidMongoUri"), nameof(connectionString));
        }

        var authorityStart = schemeEnd + 3;
        var authorityEnd = connectionString.IndexOfAny(['/', '?'], authorityStart);
        if (authorityEnd < 0)
        {
            authorityEnd = connectionString.Length;
        }

        var authority = connectionString[authorityStart..authorityEnd];
        var host = authority[(authority.LastIndexOf('@') + 1)..];
        return connectionString[..authorityStart] + Uri.EscapeDataString(username.Trim()) + ":" + (string.IsNullOrEmpty(password) ? "${MONGODB_PASSWORD}" : Uri.EscapeDataString(password)) + "@" + host + connectionString[authorityEnd..];
    }
}
