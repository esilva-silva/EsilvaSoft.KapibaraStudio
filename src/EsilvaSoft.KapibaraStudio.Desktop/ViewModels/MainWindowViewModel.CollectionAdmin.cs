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
    private bool _renameDropTarget;

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

    public bool CanValidateCollectionIntegrity => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(SelectedCollection)
        && string.Equals(SelectedCollection, CollectionIntegrityConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanCompactCollection => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(SelectedCollection)
        && string.Equals(SelectedCollection, CollectionCompactConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanLoadCollectionStats => CanExecuteQuery;

    public bool CanCreateCollection => SelectedProfile is not null
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(NewCollectionName);

    public bool CanRenameCollection => CanExecuteQuery && !string.IsNullOrWhiteSpace(RenameCollectionName);

    public bool CanDropCollection => CanExecuteQuery && string.Equals(SelectedCollection, DropCollectionConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanConfigureCollectionValidation => CanExecuteQuery
        && !string.IsNullOrWhiteSpace(CollectionValidatorJson)
        && string.Equals(SelectedCollection, CollectionValidationConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanUpdateView => CanExecuteQuery
        && !string.IsNullOrWhiteSpace(ViewUpdatePipeline)
        && string.Equals(SelectedCollection, ViewUpdateConfirmation.Trim(), StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanCreateCollection))]
    private async Task CreateCollectionAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        var name = NewCollectionName;
        var isView = NewCollectionIsView;
        var request = new CollectionCreateRequest(
            SelectedDatabase,
            name,
            NewCollectionIsCapped,
            NewCollectionMaxSizeBytes is null ? null : decimal.ToInt64(NewCollectionMaxSizeBytes.Value),
            NewCollectionMaxDocuments is null ? null : decimal.ToInt64(NewCollectionMaxDocuments.Value),
            NewCollectionIsView ? NewCollectionViewOn : null,
            NewCollectionIsView ? NewCollectionViewPipeline : null,
            string.IsNullOrWhiteSpace(NewCollectionCollation) ? null : NewCollectionCollation,
            NewCollectionIsClustered,
            NewCollectionIsClustered ? NewCollectionClusteredIndexKey : null);
        if (!await RunAsync(cancellationToken => _workspace.CreateCollectionAsync(SelectedProfile, request, cancellationToken)))
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
        await LoadCollectionsAsync(SelectedDatabase);
        SelectedCollection = Collections.FirstOrDefault(collection => string.Equals(collection, name, StringComparison.Ordinal));
        await RecordAuditAsync(
            isView ? "view.create" : "collection.create",
            SelectedProfile,
            SelectedDatabase,
            name,
            isView ? T("viewCreatedAudit") : T("collectionCreatedAudit"));
        StatusMessage = isView ? F("viewCreated", name) : F("collectionCreated", name);
    }

    [RelayCommand(CanExecute = nameof(CanRenameCollection))]
    private async Task RenameCollectionAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var sourceCollection))
        {
            return;
        }

        var targetCollection = RenameCollectionName;
        if (!await RunAsync(cancellationToken => _workspace.RenameCollectionAsync(
            profile,
            new CollectionRenameRequest(database, sourceCollection, targetCollection, RenameDropTarget),
            cancellationToken)))
        {
            return;
        }

        RenameCollectionName = string.Empty;
        RenameDropTarget = false;
        await LoadCollectionsAsync(database);
        SelectedCollection = Collections.FirstOrDefault(collection => string.Equals(collection, targetCollection, StringComparison.Ordinal));
        await RecordAuditAsync("collection.rename", profile, database, targetCollection, F("collectionRenamedAudit", sourceCollection, targetCollection));
        StatusMessage = F("collectionRenamed", sourceCollection, targetCollection);
    }

    [RelayCommand(CanExecute = nameof(CanUpdateView))]
    private async Task UpdateViewAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var view))
        {
            return;
        }

        if (!await RunAsync(cancellationToken => _workspace.UpdateViewAsync(
            profile,
            new ViewUpdateRequest(database, view, ViewUpdatePipeline, ViewUpdateConfirmation),
            cancellationToken)))
        {
            return;
        }

        ViewUpdateConfirmation = string.Empty;
        await RecordAuditAsync("view.update", profile, database, view, T("viewPipelineUpdatedAudit"));
        StatusMessage = F("viewPipelineUpdated", view);
    }

    [RelayCommand(CanExecute = nameof(CanConfigureCollectionValidation))]
    private async Task ConfigureCollectionValidationAsync()
    {
        if (!TryGetCollectionContext(out var profile, out var database, out var collection))
        {
            return;
        }

        if (!await RunAsync(cancellationToken => _workspace.ConfigureCollectionValidationAsync(
            profile,
            new CollectionValidationRequest(
                database,
                collection,
                CollectionValidatorJson,
                CollectionValidationLevel,
                CollectionValidationAction,
                CollectionValidationConfirmation),
            cancellationToken)))
        {
            return;
        }

        CollectionValidationConfirmation = string.Empty;
        await RecordAuditAsync(
            "collection.validation.configure",
            profile,
            database,
            collection,
            F("validationConfiguredAudit", CollectionValidationLevel, CollectionValidationAction));
        StatusMessage = F("collectionValidationUpdated", collection);
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

        CollectionValidatorJson = validation!.ValidatorJson;
        CollectionValidationLevel = validation.ValidationLevel;
        CollectionValidationAction = validation.ValidationAction;
        StatusMessage = F("collectionValidationLoaded", collection);
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
        await LoadCollectionsAsync(database);
        SelectedCollection = null;
        await RecordAuditAsync("collection.drop", profile, database, collection, T("collectionRemovedAudit"));
        StatusMessage = F("collectionRemoved", collection);
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
}
