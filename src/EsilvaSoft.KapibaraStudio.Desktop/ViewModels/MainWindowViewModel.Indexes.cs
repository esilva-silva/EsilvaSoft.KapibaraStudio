using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private string _indexKeys = "{ \"campo\": 1 }";

    [ObservableProperty]
    private string _indexName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropIndex))]
    [NotifyCanExecuteChangedFor(nameof(DropIndexCommand))]
    private string _indexNameToDrop = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetIndexVisibility))]
    [NotifyCanExecuteChangedFor(nameof(SetIndexVisibilityCommand))]
    private string _indexNameForVisibility = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSetIndexVisibility))]
    [NotifyCanExecuteChangedFor(nameof(SetIndexVisibilityCommand))]
    private string _indexVisibilityConfirmation = string.Empty;

    [ObservableProperty]
    private bool _indexVisibilityHidden;

    [ObservableProperty]
    private bool _indexIsUnique;

    [ObservableProperty]
    private bool _indexIsSparse;

    [ObservableProperty]
    private bool _indexIsHidden;

    [ObservableProperty]
    private decimal? _indexExpireAfterSeconds;

    [ObservableProperty]
    private string _indexPartialFilter = string.Empty;

    [ObservableProperty]
    private string _indexCollation = string.Empty;

    [ObservableProperty]
    private string _indexWildcardProjection = string.Empty;

    [ObservableProperty]
    private string _indexResults = string.Empty;

    partial void OnIndexResultsChanged(string value) { }

    public bool CanDropIndex => CanExecuteQuery && !string.IsNullOrWhiteSpace(IndexNameToDrop);

    public bool CanSetIndexVisibility => CanExecuteQuery
        && !string.IsNullOrWhiteSpace(IndexNameForVisibility)
        && string.Equals(IndexNameForVisibility.Trim(), IndexVisibilityConfirmation.Trim(), StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task LoadIndexesAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var indexes = await _workspace.GetIndexesAsync(profile, database, collection, cancellationToken);
            IndexResults = indexes.Count == 0 ? T("noIndexes") : string.Join(Environment.NewLine + Environment.NewLine, indexes);
            StatusMessage = F("indexesLoaded", indexes.Count);
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task LoadIndexUsageStatsAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var statistics = await _workspace.GetIndexUsageStatsAsync(profile, database, collection, cancellationToken);
            IndexResults = statistics.Count == 0 ? T("noIndexStats") : string.Join(Environment.NewLine + Environment.NewLine, statistics);
            StatusMessage = F("indexStatsLoaded", statistics.Count);
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task CreateIndexAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new IndexCreateRequest(
                database,
                collection,
                IndexKeys,
                IndexName,
                IndexIsUnique,
                IndexIsSparse,
                IndexExpireAfterSeconds is null ? null : decimal.ToInt32(IndexExpireAfterSeconds.Value),
                string.IsNullOrWhiteSpace(IndexPartialFilter) ? null : IndexPartialFilter,
                string.IsNullOrWhiteSpace(IndexCollation) ? null : IndexCollation,
                IndexIsHidden,
                string.IsNullOrWhiteSpace(IndexWildcardProjection) ? null : IndexWildcardProjection);
            var name = await _workspace.CreateIndexAsync(profile, request, cancellationToken);
            StatusMessage = F("indexCreated", name);
            await LoadIndexesAsync();
        });
    }

    [RelayCommand(CanExecute = nameof(CanDropIndex))]
    private async Task DropIndexAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var name = IndexNameToDrop.Trim();
        if (!await RunAsync(async cancellationToken =>
        {
            await _workspace.DropIndexAsync(profile, new IndexDropRequest(database, collection, name), cancellationToken);
            StatusMessage = F("indexDropped", name);
        }))
        {
            return;
        }

        IndexNameToDrop = string.Empty;
        await LoadIndexesAsync();
    }

    [RelayCommand(CanExecute = nameof(CanSetIndexVisibility))]
    private async Task SetIndexVisibilityAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var request = new IndexVisibilityRequest(database, collection, IndexNameForVisibility, IndexVisibilityConfirmation, IndexVisibilityHidden);
        if (!await RunAsync(cancellationToken => _workspace.SetIndexVisibilityAsync(profile, request, cancellationToken)))
        {
            return;
        }

        var name = request.Name.Trim();
        IndexNameForVisibility = string.Empty;
        IndexVisibilityConfirmation = string.Empty;
        await RecordAuditAsync("index.visibility", profile, database, collection, F("indexVisibilityChanged", name));
        await LoadIndexesAsync();
    }
}
