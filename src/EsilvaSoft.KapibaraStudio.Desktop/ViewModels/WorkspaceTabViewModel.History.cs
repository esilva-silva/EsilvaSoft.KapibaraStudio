using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class WorkspaceTabViewModel
{
    public ObservableCollection<MqlSuggestion> Suggestions { get; } = [];
    public ObservableCollection<QueryHistoryEntry> History { get; } = [];
    public ObservableCollection<SavedQuery> SavedQueries { get; } = [];
    public ObservableCollection<ScriptHistoryEntry> ScriptHistory { get; } = [];

    [ObservableProperty] private string _savedQueryName = "";
    [ObservableProperty] private bool _savedQueryIsFavorite;
    [ObservableProperty] private QueryHistoryEntry? _selectedHistory;
    [ObservableProperty] private SavedQuery? _selectedSavedQuery;
    [ObservableProperty] private ScriptHistoryEntry? _selectedScriptHistory;
    [ObservableProperty] private MqlSuggestion? _selectedSuggestion;

    [RelayCommand]
    private async Task LoadHistoryAsync()
    {
        var generation = ++_historyGeneration;
        var profile = Profile;
        try
        {
            History.Clear(); SavedQueries.Clear(); ScriptHistory.Clear(); ConsoleHistory.Clear(); LocalizedConsoleHistory.Clear();
            var consoleEntries = await _workspace.GetConsoleHistoryAsync();
            if (generation != _historyGeneration) return;
            foreach (var entry in consoleEntries)
            {
                ConsoleHistory.Add(entry);
                LocalizedConsoleHistory.Add(new(entry));
            }
            if (profile is not null)
            {
                var history = HistoryEnabled ? await _workspace.GetRecentQueryHistoryAsync(profile.Id) : [];
                var saved = await _workspace.GetSavedQueriesAsync(profile.Id);
                if (generation != _historyGeneration) return;
                foreach (var entry in history) History.Add(entry);
                foreach (var entry in saved) SavedQueries.Add(entry);
            }
            var scripts = ScriptHistoryEnabled ? await _workspace.GetRecentScriptHistoryAsync() : [];
            if (generation != _historyGeneration) return;
            foreach (var entry in scripts) ScriptHistory.Add(entry);
        }
        catch (Exception ex) { Errors = DesktopOperationErrorMessages.Describe(ex); ResultTabIndex = 2; }
    }

    [RelayCommand]
    private void ApplyHistory()
    {
        if (IsRunning) return;
        var query = SelectedHistory?.ToQuery() ?? SelectedSavedQuery?.ToQuery();
        if (query is null) return;
        Mode = "Console"; Database = query.Database; Collection = query.Collection;
        Text = ConsoleScripts.FromQuery(query); Projection = query.ProjectionJson ?? ""; Sort = query.SortJson ?? "";
        Limit = query.Limit; Skip = query.Skip;
        Hint = query.HintJson ?? ""; Comment = query.Comment ?? ""; Collation = query.CollationJson ?? "";
        BatchSize = query.BatchSize ?? 100; MaxTimeMs = query.MaxTimeMs ?? 30000;
    }

    [RelayCommand]
    private async Task SaveQueryAsync()
    {
        if (Profile is null || !IsQuery) return;
        try
        {
            var query = BuildQuery(Text);
            var saved = SavedQuery.Create(SavedQueryName, Profile.Id, query, SavedQueryIsFavorite);
            if (SelectedSavedQuery is { } existing) saved = saved with { Id = existing.Id };
            await _workspace.SaveSavedQueryAsync(saved);
            await LoadHistoryAsync();
        }
        catch (Exception ex) { Errors = DesktopOperationErrorMessages.Describe(ex); ResultTabIndex = 2; }
    }

    partial void OnSelectedSavedQueryChanged(SavedQuery? value)
    {
        if (value is null) return;
        SavedQueryName = value.Name; SavedQueryIsFavorite = value.IsFavorite;
    }
    [RelayCommand] private void NewSavedQuery() { SelectedSavedQuery = null; SavedQueryName = ""; SavedQueryIsFavorite = false; }
    [RelayCommand] private async Task DeleteSavedQueryAsync()
    {
        if (SelectedSavedQuery is null) return;
        try { await _workspace.DeleteSavedQueryAsync(SelectedSavedQuery.Id); NewSavedQuery(); await LoadHistoryAsync(); }
        catch (Exception ex) { Errors = DesktopOperationErrorMessages.Describe(ex); }
    }
}
