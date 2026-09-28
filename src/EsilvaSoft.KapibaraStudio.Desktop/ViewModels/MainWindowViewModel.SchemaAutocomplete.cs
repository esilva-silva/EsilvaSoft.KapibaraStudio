using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private decimal? _schemaSampleMaximumDocuments = 200;

    [ObservableProperty]
    private string _autocompleteSuggestions = string.Empty;

    public ObservableCollection<MqlSuggestion> QuerySuggestions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyQuerySuggestionCommand))]
    private MqlSuggestion? _selectedQuerySuggestion;

    [RelayCommand]
    private void SuggestMql()
    {
        QuerySuggestions.Clear();
        SelectedQuerySuggestion = null;
        AutocompleteSuggestions = T("suggestMqlHint");
    }

    private bool CanApplyQuerySuggestion() => SelectedQuerySuggestion is not null;

    [RelayCommand(CanExecute = nameof(CanApplyQuerySuggestion))]
    private void ApplyQuerySuggestion()
    {
        StatusMessage = T("applyMqlHint");
    }

    [RelayCommand]
    private void SuggestAggregation()
    {
        AutocompleteSuggestions = T("suggestAggregationHint");
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task SampleSchemaFieldsAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var maximum = decimal.ToInt32(SchemaSampleMaximumDocuments ?? 200);
            var sample = await _workspace.QueryAsync(profile, new MongoQuery(database, collection, Limit: maximum, MaxTimeMs: 2_000), cancellationToken);
            _knownFields.Clear();
            _knownFields.UnionWith(MqlAutocompleteService.InferFieldPaths(sample.Documents));
            AutocompleteSuggestions = _knownFields.Count == 0
                ? T("noSuggestableFields")
                : F("fieldsInferred", _knownFields.Count, sample.Documents.Count);
            StatusMessage = F("schemaSampleLoaded", sample.Documents.Count, _knownFields.Count);
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task InferCollectionValidatorAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var maximum = decimal.ToInt32(SchemaSampleMaximumDocuments ?? 200);
            var sample = await _workspace.QueryAsync(profile, new MongoQuery(database, collection, Limit: maximum, MaxTimeMs: 2_000), cancellationToken);
            CollectionValidatorJson = MqlAutocompleteService.InferJsonSchema(sample.Documents);
            StatusMessage = F("validatorInferred", sample.Documents.Count);
        });
    }
}
