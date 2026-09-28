using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<QueryHistoryEntry> QueryHistory { get; } = [];

    public ObservableCollection<SavedQuery> SavedQueries { get; } = [];

    [ObservableProperty]
    private QueryHistoryEntry? _selectedQueryHistory;

    [ObservableProperty]
    private bool _queryHistoryEnabled = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteSavedQueryCommand))]
    private SavedQuery? _selectedSavedQuery;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSaveSavedQuery))]
    [NotifyCanExecuteChangedFor(nameof(SaveSavedQueryCommand))]
    private string _savedQueryName = string.Empty;

    [ObservableProperty]
    private bool _savedQueryIsFavorite;

    public bool CanSaveSavedQuery => CanExecuteQuery && !string.IsNullOrWhiteSpace(SavedQueryName);

    public bool CanDeleteSavedQuery => SelectedSavedQuery is not null;

    partial void OnSelectedQueryHistoryChanged(QueryHistoryEntry? value)
    {
        if (value is null)
        {
            return;
        }

        var query = value.ToQuery();
        SelectedDatabase = query.Database;
        SelectedCollection = query.Collection;
        QueryFilter = query.FilterJson;
        QueryProjection = query.ProjectionJson ?? string.Empty;
        QuerySort = query.SortJson ?? string.Empty;
        QueryHint = query.HintJson ?? string.Empty;
        QueryLimit = query.Limit;
        QuerySkip = query.Skip;
        QueryMaxTimeMs = query.MaxTimeMs;
        QueryComment = query.Comment ?? string.Empty;
        QueryBatchSize = query.BatchSize;
        QueryCollation = query.CollationJson ?? string.Empty;
        StatusMessage = T("queryLoadedHistory");
    }

    partial void OnQueryHistoryEnabledChanged(bool value)
    {
        if (!value)
        {
            SelectedQueryHistory = null;
            QueryHistory.Clear();
            return;
        }

        _ = LoadQueryHistoryAsync();
    }

    partial void OnSelectedSavedQueryChanged(SavedQuery? value)
    {
        if (value is null)
        {
            return;
        }

        var query = value.ToQuery();
        SavedQueryName = value.Name;
        SavedQueryIsFavorite = value.IsFavorite;
        SelectedDatabase = query.Database;
        SelectedCollection = query.Collection;
        QueryFilter = query.FilterJson;
        QueryProjection = query.ProjectionJson ?? string.Empty;
        QuerySort = query.SortJson ?? string.Empty;
        QueryHint = query.HintJson ?? string.Empty;
        QueryLimit = query.Limit;
        QuerySkip = query.Skip;
        QueryMaxTimeMs = query.MaxTimeMs;
        StatusMessage = T("savedQueryLoaded");
    }

    [RelayCommand]
    private async Task LoadQueryHistoryAsync()
    {
        if (!QueryHistoryEnabled)
        {
            QueryHistory.Clear();
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var entries = await _workspace.GetRecentQueryHistoryAsync(SelectedProfile?.Id, cancellationToken: cancellationToken);
            QueryHistory.Clear();
            foreach (var entry in entries)
            {
                QueryHistory.Add(entry);
            }
        });
    }

    [RelayCommand]
    private async Task LoadSavedQueriesAsync()
    {
        await RunAsync(async cancellationToken =>
        {
            var queries = await _workspace.GetSavedQueriesAsync(SelectedProfile?.Id, cancellationToken);
            SavedQueries.Clear();
            foreach (var query in queries)
            {
                SavedQueries.Add(query);
            }
        });
    }

    [RelayCommand(CanExecute = nameof(CanSaveSavedQuery))]
    private async Task SaveSavedQueryAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        SavedQuery? saved = null;
        if (!await RunAsync(async cancellationToken =>
        {
            var query = new MongoQuery(
                database,
                collection,
                QueryFilter,
                string.IsNullOrWhiteSpace(QueryProjection) ? null : QueryProjection,
                string.IsNullOrWhiteSpace(QuerySort) ? null : QuerySort,
                decimal.ToInt32(QueryLimit ?? 100),
                decimal.ToInt32(QuerySkip ?? 0),
                string.IsNullOrWhiteSpace(QueryHint) ? null : QueryHint,
                QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value));
            saved = SelectedSavedQuery is null
                ? SavedQuery.Create(SavedQueryName, profile.Id, query, SavedQueryIsFavorite)
                : new SavedQuery(SelectedSavedQuery.Id, SavedQueryName.Trim(), profile.Id, query.Database, query.Collection, query.FilterJson, query.ProjectionJson, query.SortJson, query.HintJson, query.Limit, query.Skip, query.MaxTimeMs, SavedQueryIsFavorite, DateTimeOffset.UtcNow).Validate();
            await _workspace.SaveSavedQueryAsync(saved, cancellationToken);
        }))
        {
            return;
        }

        await LoadSavedQueriesAsync();
        SelectedSavedQuery = SavedQueries.FirstOrDefault(item => item.Id == saved!.Id);
        StatusMessage = T("savedQueryStored");
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSavedQuery))]
    private async Task DeleteSavedQueryAsync()
    {
        if (SelectedSavedQuery is null)
        {
            return;
        }

        var query = SelectedSavedQuery;
        if (!await RunAsync(cancellationToken => _workspace.DeleteSavedQueryAsync(query.Id, cancellationToken)))
        {
            return;
        }

        SelectedSavedQuery = null;
        SavedQueryName = string.Empty;
        SavedQueryIsFavorite = false;
        await LoadSavedQueriesAsync();
        StatusMessage = F("savedQueryRemoved", query.Name);
    }

    [RelayCommand]
    private void NewSavedQuery()
    {
        SelectedSavedQuery = null;
        SavedQueryName = string.Empty;
        SavedQueryIsFavorite = false;
        StatusMessage = T("savedQueryNameHint");
    }
}
