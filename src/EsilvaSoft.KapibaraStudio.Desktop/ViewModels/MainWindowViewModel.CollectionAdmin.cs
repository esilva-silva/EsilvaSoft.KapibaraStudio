using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<CollectionValidationLevel> CollectionValidationLevels { get; } = Enum.GetValues<CollectionValidationLevel>();

    public IReadOnlyList<CollectionValidationAction> CollectionValidationActions { get; } = Enum.GetValues<CollectionValidationAction>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateCollection))]
    [NotifyCanExecuteChangedFor(nameof(CreateCollectionCommand))]
    private string _newCollectionName = string.Empty;

    [ObservableProperty]
    private bool _newCollectionIsCapped;

    [ObservableProperty]
    private bool _newCollectionIsView;

    [ObservableProperty]
    private bool _newCollectionIsClustered;

    [ObservableProperty]
    private string _newCollectionClusteredIndexKey = "{ \"_id\": 1 }";

    [ObservableProperty]
    private string _newCollectionViewOn = string.Empty;

    [ObservableProperty]
    private string _newCollectionViewPipeline = "[]";

    [ObservableProperty]
    private string _newCollectionCollation = string.Empty;

    [ObservableProperty]
    private decimal? _newCollectionMaxSizeBytes;

    [ObservableProperty]
    private decimal? _newCollectionMaxDocuments;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanValidateCollectionIntegrity))]
    [NotifyCanExecuteChangedFor(nameof(ValidateCollectionIntegrityCommand))]
    private string _collectionIntegrityConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompactCollection))]
    [NotifyCanExecuteChangedFor(nameof(CompactCollectionCommand))]
    private string _collectionCompactConfirmation = string.Empty;

    [ObservableProperty]
    private bool _collectionCompactForce;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRenameCollection))]
    [NotifyCanExecuteChangedFor(nameof(RenameCollectionCommand))]
    private string _renameCollectionName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRenameCollection))]
    [NotifyCanExecuteChangedFor(nameof(RenameCollectionCommand))]
    private bool _renameDropTarget;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRenameCollection))]
    [NotifyCanExecuteChangedFor(nameof(RenameCollectionCommand))]
    private string _renameDropTargetConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropCollection))]
    [NotifyCanExecuteChangedFor(nameof(DropCollectionCommand))]
    private string _dropCollectionConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateView))]
    [NotifyCanExecuteChangedFor(nameof(UpdateViewCommand))]
    private string _viewUpdateConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateView))]
    [NotifyCanExecuteChangedFor(nameof(UpdateViewCommand))]
    private string _viewUpdatePipeline = "[]";

    [ObservableProperty]
    private string _viewUpdateSourceCollection = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigureCollectionValidation))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCollectionValidationCommand))]
    private string _collectionValidatorJson = "{\n  \"$jsonSchema\": {\n    \"bsonType\": \"object\"\n  }\n}";

    [ObservableProperty]
    private CollectionValidationLevel _collectionValidationLevel = CollectionValidationLevel.Strict;

    [ObservableProperty]
    private CollectionValidationAction _collectionValidationAction = CollectionValidationAction.Error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfigureCollectionValidation))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCollectionValidationCommand))]
    private string _collectionValidationConfirmation = string.Empty;

    [ObservableProperty]
    private string _collectionValidationReadbackText = string.Empty;

    public bool CanValidateCollectionIntegrity => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(SelectedCollection)
        && string.Equals(SelectedCollection, CollectionIntegrityConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanCompactCollection => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(SelectedCollection)
        && string.Equals(SelectedCollection, CollectionCompactConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanLoadCollectionStats => CanExecuteQuery;

    public bool CanCreateCollection => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(NewCollectionName);

    public bool CanRenameCollection => SelectedProfile is { IsReadOnly: false }
        && CanExecuteQuery
        && !string.IsNullOrWhiteSpace(RenameCollectionName)
        && (!RenameDropTarget || (string.Equals(RenameCollectionName, RenameDropTargetConfirmation.Trim(), StringComparison.Ordinal)
            && HasCurrentRenameTargetPreview()));

    public bool CanDropCollection => SelectedProfile is { IsReadOnly: false }
        && CanExecuteQuery && string.Equals(SelectedCollection, DropCollectionConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanConfigureCollectionValidation => SelectedProfile is { IsReadOnly: false }
        && CanExecuteQuery
        && !string.IsNullOrWhiteSpace(CollectionValidatorJson)
        && string.Equals(SelectedCollection, CollectionValidationConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanUpdateView => SelectedProfile is { IsReadOnly: false }
        && CanExecuteQuery
        && !string.IsNullOrWhiteSpace(ViewUpdatePipeline)
        && string.Equals(SelectedCollection, ViewUpdateConfirmation.Trim(), StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanCreateCollection))]
    private async Task CreateCollectionAsync()
    {
        var profile = SelectedProfile;
        var database = SelectedDatabase;
        if (profile is null || string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        var name = NewCollectionName;
        var isView = NewCollectionIsView;
        var originalCollection = SelectedCollection;
        var request = new CollectionCreateRequest(
            database,
            name,
            NewCollectionIsCapped,
            NewCollectionMaxSizeBytes is null ? null : decimal.ToInt64(NewCollectionMaxSizeBytes.Value),
            NewCollectionMaxDocuments is null ? null : decimal.ToInt64(NewCollectionMaxDocuments.Value),
            NewCollectionIsView ? NewCollectionViewOn : null,
            NewCollectionIsView ? NewCollectionViewPipeline : null,
            string.IsNullOrWhiteSpace(NewCollectionCollation) ? null : NewCollectionCollation,
            NewCollectionIsClustered,
            NewCollectionIsClustered ? NewCollectionClusteredIndexKey : null);
        if (!await RunAsync(cancellationToken => _workspace.CreateCollectionAsync(profile, request, cancellationToken)))
        {
            return;
        }

        NewCollectionName = string.Empty;
        NewCollectionIsCapped = false;
        NewCollectionIsView = false;
        NewCollectionIsClustered = false;
        NewCollectionClusteredIndexKey = "{ \"_id\": 1 }";
        NewCollectionViewOn = string.Empty;
        NewCollectionViewPipeline = "[]";
        NewCollectionCollation = string.Empty;
        NewCollectionMaxSizeBytes = null;
        NewCollectionMaxDocuments = null;
        StatusMessage = isView ? F("viewCreated", name) : F("collectionCreated", name);
        await RecordAuditAsync(
            isView ? "view.create" : "collection.create",
            profile,
            database,
            name,
            isView ? T("viewCreatedAudit") : T("collectionCreatedAudit"));
        await ReloadCollectionsForOriginalContextAsync(profile, database, originalCollection, name);
    }

    [RelayCommand(CanExecute = nameof(CanRenameCollection))]
    private async Task RenameCollectionAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var sourceCollection))
        {
            return;
        }

        var targetCollection = RenameCollectionName;
        var request = new CollectionRenameRequest(database, sourceCollection, targetCollection, RenameDropTarget, RenameDropTargetConfirmation);
        var preview = _renameTargetPreview;
        if (request.DropTarget && !HasCurrentRenameTargetPreview())
        {
            SetError(T("renameTargetPreviewRequired"));
            return;
        }

        if (!await RunAsync(async cancellationToken =>
            {
                if (request.DropTarget)
                {
                    var currentDefinition = await _workspace.GetCollectionDefinitionAsync(profile, database, targetCollection, cancellationToken);
                    if (!preview!.MatchesObservedDefinition(currentDefinition))
                    {
                        throw new InvalidOperationException(T("renameTargetPreviewChanged"));
                    }
                }

                await _workspace.RenameCollectionAsync(profile, request, cancellationToken);
            }))
        {
            return;
        }

        _renameTargetPreview = null;
        RenameTargetPreviewText = string.Empty;
        RenameCollectionName = string.Empty;
        RenameDropTarget = false;
        RenameDropTargetConfirmation = string.Empty;
        StatusMessage = F("collectionRenamed", sourceCollection, targetCollection);
        await RecordAuditAsync("collection.rename", profile, database, targetCollection, F("collectionRenamedAudit", sourceCollection, targetCollection));
        await ReloadCollectionsForOriginalContextAsync(profile, database, sourceCollection, targetCollection);
    }

    [RelayCommand(CanExecute = nameof(CanUpdateView))]
    private async Task UpdateViewAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var view))
        {
            return;
        }

        var request = new ViewUpdateRequest(
            database,
            view,
            ViewUpdatePipeline,
            ViewUpdateConfirmation,
            string.IsNullOrWhiteSpace(ViewUpdateSourceCollection) ? null : ViewUpdateSourceCollection);
        if (!await RunAsync(cancellationToken => _workspace.UpdateViewAsync(
            profile,
            request,
            cancellationToken)))
        {
            return;
        }

        ViewUpdateConfirmation = string.Empty;
        StatusMessage = F("viewPipelineUpdated", view);
        await RecordAuditAsync("view.update", profile, database, view, T("viewPipelineUpdatedAudit"));
    }

    [RelayCommand(CanExecute = nameof(CanConfigureCollectionValidation))]
    private async Task ConfigureCollectionValidationAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var level = CollectionValidationLevel;
        var action = CollectionValidationAction;
        var request = new CollectionValidationRequest(
            database,
            collection,
            CollectionValidatorJson,
            level,
            action,
            CollectionValidationConfirmation);
        if (!await RunAsync(cancellationToken => _workspace.ConfigureCollectionValidationAsync(
            profile,
            request,
            cancellationToken)))
        {
            return;
        }

        CollectionValidationConfirmation = string.Empty;
        CollectionValidationInfo? observed = null;
        var readbackSucceeded = await RunAsync(async cancellationToken =>
            observed = await _workspace.GetCollectionValidationAsync(profile, database, collection, cancellationToken));
        if (IsOriginalCollectionContext(profile, database, collection))
        {
            var matches = readbackSucceeded && CollectionValidationReadback.Matches(request, observed!);
            CollectionValidationReadbackText = readbackSucceeded
                ? (matches
                    ? T("validationReadbackMatches")
                    : T("validationReadbackDiffers"))
                    + Environment.NewLine + F("validationObservedOptions", observed!.ValidationLevel, observed.ValidationAction)
                    + Environment.NewLine + observed.ValidatorJson
                : T("validationReadbackFailed");
            StatusMessage = readbackSucceeded
                ? (matches
                    ? F("collectionValidationUpdated", collection)
                    : T("validationReadbackDiffers"))
                : T("validationReadbackFailed");
        }

        await RecordAuditAsync(
            "collection.validation.configure",
            profile,
            database,
            collection,
            F("validationConfiguredAudit", level, action));
    }

    [RelayCommand(CanExecute = nameof(CanExecuteQuery))]
    private async Task LoadCollectionValidationAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        CollectionValidationInfo? validation = null;
        if (!await RunAsync(async cancellationToken =>
        {
            validation = await _workspace.GetCollectionValidationAsync(profile, database, collection, cancellationToken);
        }))
        {
            return;
        }

        if (IsOriginalCollectionContext(profile, database, collection))
        {
            CollectionValidatorJson = validation!.ValidatorJson;
            CollectionValidationLevel = validation.ValidationLevel;
            CollectionValidationAction = validation.ValidationAction;
            StatusMessage = F("collectionValidationLoaded", collection);
        }
    }

    [RelayCommand(CanExecute = nameof(CanDropCollection))]
    private async Task DropCollectionAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        if (!await RunAsync(cancellationToken => _workspace.DropCollectionAsync(
            profile,
            new CollectionDropRequest(database, collection, DropCollectionConfirmation),
            cancellationToken)))
        {
            return;
        }

        DropCollectionConfirmation = string.Empty;
        StatusMessage = F("collectionRemoved", collection);
        await RecordAuditAsync("collection.drop", profile, database, collection, T("collectionRemovedAudit"));
        await ReloadCollectionsForOriginalContextAsync(profile, database, collection);
    }

    [RelayCommand(CanExecute = nameof(CanValidateCollectionIntegrity))]
    private async Task ValidateCollectionIntegrityAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var request = new CollectionIntegrityCheckRequest(database, collection, CollectionIntegrityConfirmation);
        if (!await RunAsync(async cancellationToken =>
            {
                AdministrationResults = await _workspace.ValidateCollectionIntegrityAsync(profile, request, cancellationToken);
                StatusMessage = F("collectionValidationDone", collection);
            }))
        {
            return;
        }

        CollectionIntegrityConfirmation = string.Empty;
        await RecordAuditAsync("collection.validate", profile, database, collection, T("collectionIntegrityAudit"));
    }

    [RelayCommand(CanExecute = nameof(CanCompactCollection))]
    private async Task CompactCollectionAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        var request = new CollectionCompactRequest(database, collection, CollectionCompactConfirmation, CollectionCompactForce);
        if (!await RunAsync(async cancellationToken =>
            {
                AdministrationResults = await _workspace.CompactCollectionAsync(profile, request, cancellationToken);
                StatusMessage = F("collectionCompacted", collection);
            }))
        {
            return;
        }

        CollectionCompactConfirmation = string.Empty;
        CollectionCompactForce = false;
        await RecordAuditAsync("collection.compact", profile, database, collection, T("compactionRequested"));
    }

    [RelayCommand(CanExecute = nameof(CanLoadCollectionStats))]
    private async Task LoadCollectionStatsAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetCollectionStatsAsync(profile, database, collection, cancellationToken);
            StatusMessage = F("collectionStatsLoaded", collection);
        });
    }

    private bool IsOriginalCollectionContext(ConnectionProfile profile, string database, string? collection = null) =>
        ReferenceEquals(SelectedProfile, profile)
        && string.Equals(SelectedDatabase, database, StringComparison.Ordinal)
        && (collection is null || string.Equals(SelectedCollection, collection, StringComparison.Ordinal));

    private async Task ReloadCollectionsForOriginalContextAsync(
        ConnectionProfile profile,
        string database,
        string? originalCollection,
        string? selectCollection = null)
    {
        IReadOnlyList<string>? collections = null;
        if (!await RunAsync(async cancellationToken =>
            collections = await _workspace.GetCollectionsAsync(profile, database, cancellationToken)))
        {
            return;
        }

        if (!IsOriginalCollectionContext(profile, database)
            || !string.Equals(SelectedCollection, originalCollection, StringComparison.Ordinal))
        {
            return;
        }

        Collections.Clear();
        foreach (var collection in collections!)
        {
            Collections.Add(collection);
        }

        SelectedCollection = selectCollection is null
            ? null
            : Collections.FirstOrDefault(collection => string.Equals(collection, selectCollection, StringComparison.Ordinal));
    }
}
