using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private IReadOnlyList<string> _lastQueryDocuments = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    private string _queryFilter = "{}";

    [ObservableProperty]
    private string _queryProjection = string.Empty;

    [ObservableProperty]
    private string _querySort = string.Empty;

    [ObservableProperty]
    private decimal? _queryLimit = 100;

    [ObservableProperty]
    private decimal? _querySkip;

    [ObservableProperty]
    private string _queryHint = string.Empty;

    [ObservableProperty]
    private string _queryComment = string.Empty;

    [ObservableProperty]
    private decimal? _queryBatchSize;

    [ObservableProperty]
    private string _queryCollation = string.Empty;

    [ObservableProperty]
    private decimal? _queryMaxTimeMs;

    [ObservableProperty]
    private string _queryResults = T("queryInitialResults");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExportQueryResults))]
    [NotifyCanExecuteChangedFor(nameof(ExportQueryResultsCommand))]
    private string _queryExportPath = string.Empty;

    [ObservableProperty]
    private string _countResults = T("exactCountNote");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGetDistinctValues))]
    [NotifyCanExecuteChangedFor(nameof(GetDistinctValuesCommand))]
    private string _distinctField = string.Empty;

    [ObservableProperty]
    private decimal? _distinctMaximumValues = 1_000;

    [ObservableProperty]
    private string _distinctResults = T("distinctInitial");

    [ObservableProperty]
    private string _explainResults = T("explainInitial");

    [ObservableProperty]
    private string _aggregationPipeline = "[\n  { \"$match\": {} },\n  { \"$limit\": 100 }\n]";

    [ObservableProperty]
    private decimal? _aggregationLimit = 100;

    [ObservableProperty]
    private string _aggregationResults = T("aggregationInitial");

    public bool CanExportQueryResults => _lastQueryDocuments.Count > 0 && !string.IsNullOrWhiteSpace(QueryExportPath);

    public bool CanGetDistinctValues => CanExecuteQuery && !string.IsNullOrWhiteSpace(DistinctField);

    partial void OnQueryFilterChanged(string value)
    {
        ExecuteQueryCommand.NotifyCanExecuteChanged();
        QuerySuggestions.Clear();
        SelectedQuerySuggestion = null;
    }

    [RelayCommand]
    private void ShowAllDocuments()
    {
        QueryFilter = "{}";
        QuerySkip = 0;
        StatusMessage = T("filterReset");
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ExecuteQueryAsync()
    {
        var profile = SelectedProfile;
        if (profile is null || string.IsNullOrWhiteSpace(SelectedDatabase) || string.IsNullOrWhiteSpace(SelectedCollection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var query = new MongoQuery(
                SelectedDatabase,
                SelectedCollection,
                QueryFilter,
                ProjectionJson: string.IsNullOrWhiteSpace(QueryProjection) ? null : QueryProjection,
                SortJson: string.IsNullOrWhiteSpace(QuerySort) ? null : QuerySort,
                Limit: decimal.ToInt32(QueryLimit ?? 100),
                Skip: decimal.ToInt32(QuerySkip ?? 0),
                HintJson: string.IsNullOrWhiteSpace(QueryHint) ? null : QueryHint,
                MaxTimeMs: QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value),
                Comment: string.IsNullOrWhiteSpace(QueryComment) ? null : QueryComment,
                BatchSize: QueryBatchSize is null ? null : decimal.ToInt32(QueryBatchSize.Value),
                CollationJson: string.IsNullOrWhiteSpace(QueryCollation) ? null : QueryCollation);
            var result = await _workspace.QueryAsync(profile, query, cancellationToken);
            QueryResults = result.Documents.Count == 0
                ? T("noDocumentsFound")
                : string.Join(Environment.NewLine + Environment.NewLine, result.Documents);
            _lastQueryDocuments = result.Documents;
            ExportQueryResultsCommand.NotifyCanExecuteChanged();
            _knownFields.UnionWith(MqlAutocompleteService.InferFieldPaths(result.Documents));
            if (QueryHistoryEnabled)
            {
                var historyEntry = QueryHistoryEntry.Create(profile.Id, query);
                await _workspace.SaveQueryHistoryAsync(historyEntry, cancellationToken);
                QueryHistory.Insert(0, historyEntry);
                while (QueryHistory.Count > 50)
                {
                    QueryHistory.RemoveAt(QueryHistory.Count - 1);
                }
            }
            StatusMessage = F("queryReturned", result.Documents.Count, result.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)) + (result.IsTruncated ? T("limitReached") : string.Empty);
        });
    }

    [RelayCommand(CanExecute = nameof(CanExportQueryResults))]
    private async Task ExportQueryResultsAsync()
    {
        try
        {
            if (!string.Equals(Path.GetExtension(QueryExportPath), ".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(T("newJsonFileRequired"), nameof(QueryExportPath));
            }

            var destination = Path.GetFullPath(QueryExportPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var content = QueryResultExportSerializer.Serialize(_lastQueryDocuments);
            await using var stream = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(content);
            StatusMessage = F("exportedExtendedJson", _lastQueryDocuments.Count);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            SetError(DesktopOperationErrorMessages.Describe(exception));
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private Task CountExactDocumentsAsync() => CountDocumentsAsync(useEstimatedCount: false);

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private Task CountEstimatedDocumentsAsync() => CountDocumentsAsync(useEstimatedCount: true);

    private async Task CountDocumentsAsync(bool useEstimatedCount)
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new CollectionCountRequest(
                database,
                collection,
                useEstimatedCount ? "{}" : QueryFilter,
                useEstimatedCount,
                QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value));
            var result = await _workspace.CountDocumentsAsync(profile, request, cancellationToken);
            CountResults = result.IsEstimated
                ? F("estimatedCount", result.Count.ToString("N0", CultureInfo.CurrentCulture), result.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture))
                : F("exactCountResult", result.Count.ToString("N0", CultureInfo.CurrentCulture), result.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture));
            StatusMessage = result.IsEstimated ? T("estimatedCountDone") : T("exactCountDone");
        });
    }

    [RelayCommand(CanExecute = nameof(CanGetDistinctValues))]
    private async Task GetDistinctValuesAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new DistinctValuesRequest(
                database,
                collection,
                DistinctField,
                QueryFilter,
                decimal.ToInt32(DistinctMaximumValues ?? 1_000),
                QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value));
            var result = await _workspace.GetDistinctValuesAsync(profile, request, cancellationToken);
            DistinctResults = result.Values.Count == 0
                ? T("distinctNone")
                : string.Join(Environment.NewLine, result.Values);
            StatusMessage = F("distinctReturned", result.Values.Count, result.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)) +
                (result.IsTruncated ? T("limitReached") : string.Empty);
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ExplainQueryAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase) || string.IsNullOrWhiteSpace(SelectedCollection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var query = new MongoQuery(
                SelectedDatabase,
                SelectedCollection,
                QueryFilter,
                ProjectionJson: string.IsNullOrWhiteSpace(QueryProjection) ? null : QueryProjection,
                SortJson: string.IsNullOrWhiteSpace(QuerySort) ? null : QuerySort,
                Limit: decimal.ToInt32(QueryLimit ?? 100),
                Skip: decimal.ToInt32(QuerySkip ?? 0),
                HintJson: string.IsNullOrWhiteSpace(QueryHint) ? null : QueryHint,
                MaxTimeMs: QueryMaxTimeMs is null ? null : decimal.ToInt32(QueryMaxTimeMs.Value),
                Comment: string.IsNullOrWhiteSpace(QueryComment) ? null : QueryComment,
                BatchSize: QueryBatchSize is null ? null : decimal.ToInt32(QueryBatchSize.Value),
                CollationJson: string.IsNullOrWhiteSpace(QueryCollation) ? null : QueryCollation);
            ExplainResults = await _workspace.ExplainAsync(SelectedProfile, query, cancellationToken);
            StatusMessage = T("explainLoaded");
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ExecuteAggregationAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var query = new AggregationQuery(
                database,
                collection,
                AggregationPipeline,
                decimal.ToInt32(AggregationLimit ?? 100));
            var result = await _workspace.AggregateAsync(profile, query, cancellationToken);
            AggregationResults = result.Documents.Count == 0
                ? T("pipelineNone")
                : string.Join(Environment.NewLine + Environment.NewLine, result.Documents);
            _knownFields.UnionWith(MqlAutocompleteService.InferFieldPaths(result.Documents));
            StatusMessage = F("pipelineReturned", result.Documents.Count, result.Duration.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture)) + (result.IsTruncated ? T("limitReached") : string.Empty);
        });
    }
}
