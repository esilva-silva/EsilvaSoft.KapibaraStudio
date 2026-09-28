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
    private string _documentJson = "{\n  \n}";

    [ObservableProperty]
    private string _bulkDocumentsJson = "[\n  {\n    \n  }\n]";

    [ObservableProperty]
    private decimal? _bulkMaximumDocuments = 1_000;

    [ObservableProperty]
    private bool _bulkOrdered = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewDocument))]
    [NotifyCanExecuteChangedFor(nameof(PreviewDocumentCommand))]
    private string _mutationFilter = "{\n  \n}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDuplicatePreviewDocument))]
    [NotifyCanExecuteChangedFor(nameof(DuplicatePreviewDocumentCommand))]
    private string _documentPreview = T("documentPreviewInitial");

    [ObservableProperty]
    private bool _deleteManyDocuments;

    [ObservableProperty]
    private string _updateJson = "{\n  \"$set\": { }\n}";

    [ObservableProperty]
    private string _updateArrayFiltersJson = string.Empty;

    [ObservableProperty]
    private bool _updateUpsert;

    [ObservableProperty]
    private string _findAndModifyResult = T("findModifyInitial");

    public bool CanPreviewDocument => CanExecuteQuery && !string.IsNullOrWhiteSpace(MutationFilter) && !string.Equals(MutationFilter.Trim(), "{}", StringComparison.Ordinal);

    public bool CanDuplicatePreviewDocument => CanExecuteQuery && DocumentPreview.TrimStart().StartsWith('{');

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task InsertDocumentAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var result = await _workspace.InsertAsync(profile, database, collection, DocumentJson, cancellationToken);
            StatusMessage = T("insertedDocument") + (result.InsertedId is null ? string.Empty : $" _id: {result.InsertedId}");
        });
    }

    [RelayCommand(CanExecute = nameof(CanPreviewDocument))]
    private async Task PreviewDocumentAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var page = await _workspace.QueryAsync(profile, new MongoQuery(database, collection, MutationFilter, Limit: 1), cancellationToken);
            DocumentPreview = page.Documents.Count == 0
                ? T("previewNone")
                : page.Documents[0];
            StatusMessage = page.Documents.Count == 0
                ? T("previewNotFound")
                : T("previewLoaded");
        });
    }

    [RelayCommand(CanExecute = nameof(CanDuplicatePreviewDocument))]
    private void DuplicatePreviewDocument()
    {
        try
        {
            DocumentJson = DocumentDuplicateDraft.CreateWithoutId(DocumentPreview);
            StatusMessage = T("duplicateDraft");
        }
        catch (ArgumentException exception)
        {
            SetError(DesktopOperationErrorMessages.Describe(exception));
        }
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task InsertManyDocumentsAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new BulkInsertRequest(database, collection, BulkDocumentsJson, decimal.ToInt32(BulkMaximumDocuments ?? 1_000), BulkOrdered);
            var count = await _workspace.InsertManyAsync(profile, request, cancellationToken);
            StatusMessage = F("bulkInsertDone", count);
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task ReplaceDocumentAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var result = await _workspace.ReplaceAsync(profile, database, collection, MutationFilter, DocumentJson, cancellationToken);
            StatusMessage = F("replaceDone", result.MatchedCount, result.ModifiedCount);
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task UpdateDocumentAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new DocumentUpdateRequest(
                database,
                collection,
                MutationFilter,
                UpdateJson,
                UpdateUpsert,
                string.IsNullOrWhiteSpace(UpdateArrayFiltersJson) ? null : UpdateArrayFiltersJson);
            var result = await _workspace.UpdateAsync(profile, request, cancellationToken);
            StatusMessage = F("partialUpdateDone", result.MatchedCount, result.ModifiedCount) +
                (result.InsertedId is null ? string.Empty : F("upsertedId", result.InsertedId));
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task FindAndModifyAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var request = new DocumentUpdateRequest(database, collection, MutationFilter, UpdateJson, UpdateUpsert, string.IsNullOrWhiteSpace(UpdateArrayFiltersJson) ? null : UpdateArrayFiltersJson);
            var result = await _workspace.FindAndModifyAsync(profile, request, cancellationToken);
            FindAndModifyResult = result.DocumentJson ?? T("findModifyNone");
            StatusMessage = result.DocumentJson is null ? T("findModifyNone") : T("findModifyDone");
        });
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task DeleteDocumentAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var result = DeleteManyDocuments
                ? await _workspace.DeleteManyAsync(profile, database, collection, MutationFilter, cancellationToken)
                : await _workspace.DeleteAsync(profile, database, collection, MutationFilter, cancellationToken);
            StatusMessage = DeleteManyDocuments ? F("deleteManyDone", result.MatchedCount) : F("deleteOneDone", result.MatchedCount);
        });
    }
}
