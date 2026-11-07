using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty]
    private decimal? _exportDocumentsPerCollectionLimit = 100_000;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabase))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseCommand))]
    private string _newDatabaseName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabase))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseCommand))]
    private string _newDatabaseInitialCollection = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabase))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseCommand))]
    private string _newDatabaseConfirmation = string.Empty;

    [ObservableProperty]
    private string _exportResults = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImportDatabase))]
    [NotifyCanExecuteChangedFor(nameof(ImportDatabaseCommand))]
    private string _importSourceDirectory = string.Empty;

    [ObservableProperty]
    private bool _importUseUpsert;

    [ObservableProperty]
    private bool _importRestoreDefinitions;

    [ObservableProperty]
    private string _importConfirmation = string.Empty;

    [ObservableProperty]
    private string _importPreview = string.Empty;

    [ObservableProperty]
    private string _importDefinitionPreview = string.Empty;

    private ImportPreviewSnapshot? _importPreviewSnapshot;
    private ImportPreviewSnapshot? _definitionPreviewSnapshot;
    private bool _definitionPreviewBlocked;
    private sealed record ImportPreviewSnapshot(ConnectionProfile Profile, Guid? GenerationId, string Database,
        string SourceDirectory, DatabaseImportDuplicatePolicy Policy, bool RestoreDefinitions,
        DatabaseImportPreview? Plan);

    [ObservableProperty]
    private string _importResults = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropDatabase))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseCommand))]
    private string _dropDatabaseConfirmation = string.Empty;

    private Guid? _activeExportOperationId;

    public bool CanExportDatabase => !IsExportInProgress
        && SelectedProfile is not null
        && !string.IsNullOrWhiteSpace(SelectedDatabase);

    public bool CanCreateDatabase => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(NewDatabaseName)
        && !string.IsNullOrWhiteSpace(NewDatabaseInitialCollection)
        && string.Equals(NewDatabaseName.Trim(), NewDatabaseConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanDropDatabase => CanExportDatabase && string.Equals(SelectedDatabase, DropDatabaseConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanLoadDatabaseStats => CanExportDatabase;

    public bool CanPreviewImportDatabase => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(ImportSourceDirectory);

    public bool CanPreviewImportDefinitions => ImportRestoreDefinitions
        && _importPreviewSnapshot is { } preview
        && preview.Plan is not null
        && CanPreviewImportDatabase
        && ReferenceEquals(preview.Profile, SelectedProfile)
        && preview.GenerationId == SelectedProfile?.SourceGenerationId
        && string.Equals(preview.Database, SelectedDatabase, StringComparison.Ordinal)
        && string.Equals(preview.SourceDirectory, ImportSourceDirectory.Trim(), StringComparison.Ordinal);

    public bool CanImportDatabase => CanPreviewImportDatabase
        && _importPreviewSnapshot is { } preview
        && preview.Plan is { CanImport: true }
        && ReferenceEquals(preview.Profile, SelectedProfile)
        && preview.GenerationId == SelectedProfile?.SourceGenerationId
        && string.Equals(preview.Database, SelectedDatabase, StringComparison.Ordinal)
        && string.Equals(preview.SourceDirectory, ImportSourceDirectory.Trim(), StringComparison.Ordinal)
        && preview.Policy == (ImportUseUpsert ? DatabaseImportDuplicatePolicy.Upsert : DatabaseImportDuplicatePolicy.Reject)
        && preview.RestoreDefinitions == ImportRestoreDefinitions
        && (!preview.RestoreDefinitions || ReferenceEquals(preview, _definitionPreviewSnapshot) && !_definitionPreviewBlocked)
        && string.Equals(ImportConfirmation, preview.Database, StringComparison.Ordinal)
        && (SelectedImportCheckpoint is null || IsRecoveryVerified(ImportCheckpointKind.LogicalPackage));

    partial void OnImportSourceDirectoryChanged(string value) => InvalidateImportPreview();
    partial void OnImportUseUpsertChanged(bool value) => InvalidateImportPreview();
    partial void OnImportRestoreDefinitionsChanged(bool value) => InvalidateImportPreview();
    partial void OnImportConfirmationChanged(string value) => NotifyImportCommandState();

    private void NotifyImportTargetChanged()
    {
        ClearImportRecoveryForTargetChange();
        InvalidateImportPreview();
    }

    private void InvalidateImportPreview()
    {
        _importPreviewSnapshot = null;
        _definitionPreviewSnapshot = null;
        _definitionPreviewBlocked = false;
        ImportPreview = string.Empty;
        ImportDefinitionPreview = string.Empty;
        ImportConfirmation = string.Empty;
        InvalidateImportRecoveryVerification();
        NotifyImportCommandState();
    }

    private void NotifyImportCommandState()
    {
        OnPropertyChanged(nameof(CanPreviewImportDatabase));
        OnPropertyChanged(nameof(CanPreviewImportDefinitions));
        OnPropertyChanged(nameof(CanImportDatabase));
        PreviewImportDatabaseCommand.NotifyCanExecuteChanged();
        PreviewImportDefinitionsCommand.NotifyCanExecuteChanged();
        ImportDatabaseCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanPreviewImportDatabase))]
    private async Task PreviewImportDatabaseAsync()
    {
        if (SelectedProfile is not { IsReadOnly: false } profile
            || string.IsNullOrWhiteSpace(SelectedDatabase)
            || string.IsNullOrWhiteSpace(ImportSourceDirectory)) return;

        var database = SelectedDatabase;
        var sourceDirectory = ImportSourceDirectory.Trim();
        var policy = ImportUseUpsert ? DatabaseImportDuplicatePolicy.Upsert : DatabaseImportDuplicatePolicy.Reject;
        _definitionPreviewSnapshot = null;
        _definitionPreviewBlocked = false;
        ImportDefinitionPreview = string.Empty;
        var pending = new ImportPreviewSnapshot(profile, profile.SourceGenerationId,
            database, sourceDirectory, policy, ImportRestoreDefinitions, null);
        _importPreviewSnapshot = pending;
        ImportPreview = T("databaseImportPlanLoading");
        NotifyImportCommandState();

        var request = new DatabaseImportRequest(sourceDirectory, database, policy, database, ImportRestoreDefinitions);
        await RunAsync(async cancellationToken =>
        {
            var plan = await _workspace.PreviewDatabaseImportAsync(profile, request, cancellationToken);
            if (!ReferenceEquals(_importPreviewSnapshot, pending)
                || !ReferenceEquals(SelectedProfile, profile)
                || profile.SourceGenerationId != pending.GenerationId
                || !string.Equals(SelectedDatabase, database, StringComparison.Ordinal)
                || !string.Equals(ImportSourceDirectory.Trim(), sourceDirectory, StringComparison.Ordinal)
                || policy != (ImportUseUpsert ? DatabaseImportDuplicatePolicy.Upsert : DatabaseImportDuplicatePolicy.Reject)
                || pending.RestoreDefinitions != ImportRestoreDefinitions) return;

            var completed = pending with { Plan = plan };
            _importPreviewSnapshot = completed;
            ImportPreview = FormatDatabaseImportPreview(completed);
            NotifyImportCommandState();
        });
        if (ReferenceEquals(_importPreviewSnapshot, pending))
        {
            _importPreviewSnapshot = null;
            ImportPreview = T("databaseImportPlanFailed");
            NotifyImportCommandState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewImportDefinitions))]
    private async Task PreviewImportDefinitionsAsync()
    {
        if (_importPreviewSnapshot is not { RestoreDefinitions: true } preview || !CanPreviewImportDefinitions)
            return;

        _definitionPreviewSnapshot = null;
        _definitionPreviewBlocked = false;
        ImportDefinitionPreview = T("definitionPreviewLoading");
        NotifyImportCommandState();
        await RunAsync(async cancellationToken =>
        {
            var plan = await _workspace.PreviewDatabaseImportDefinitionsAsync(preview.Profile,
                preview.SourceDirectory, preview.Database, cancellationToken);
            if (!ReferenceEquals(_importPreviewSnapshot, preview)
                || !ReferenceEquals(SelectedProfile, preview.Profile)
                || SelectedProfile?.SourceGenerationId != preview.GenerationId
                || !string.Equals(SelectedDatabase, preview.Database, StringComparison.Ordinal)) return;
            _definitionPreviewBlocked = plan.Items.Any(item => item.Status == DatabaseDefinitionPlannedStatus.Blocked);
            _definitionPreviewSnapshot = preview;
            ImportDefinitionPreview = FormatDefinitionImportPreview(plan);
            NotifyImportCommandState();
        });
        if (ReferenceEquals(_importPreviewSnapshot, preview) && _definitionPreviewSnapshot is null)
            ImportDefinitionPreview = T("definitionPreviewFailed");
    }

    [RelayCommand(CanExecute = nameof(CanExportDatabase))]
    private async Task ExportDatabaseAsync()
    {
        if (!CanExportDatabase || SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        var profile = SelectedProfile;
        var generationId = profile.SourceGenerationId;
        var database = SelectedDatabase;
        var operationId = Guid.NewGuid();
        _activeExportOperationId = operationId;
        IsExportInProgress = true;
        ExportProgress = string.Empty;
        ExportProgressValue = 0;
        try
        {
            await RunAsync(async cancellationToken =>
            {
                var request = new DatabaseExportRequest(database, decimal.ToInt32(ExportDocumentsPerCollectionLimit ?? 100_000))
                {
                    Progress = new Progress<DatabaseExportProgress>(progress =>
                    {
                        if (!IsCurrentExportTarget(operationId, profile, generationId, database)) return;
                        var stage = progress.Stage == DatabaseExportStage.Completed ? T("exportStageCompleted") : T("exportStageRunning");
                        ExportProgress = F("databaseExportProgress", stage, progress.CompletedCollections,
                            progress.TotalCollections, progress.CurrentCollection ?? string.Empty,
                            progress.CurrentCollectionDocuments, progress.TotalDocuments);
                        ExportProgressValue = progress.TotalCollections == 0 ? 100
                            : Math.Clamp(progress.CompletedCollections * 100d / progress.TotalCollections, 0, 100);
                    })
                };
                var result = await _workspace.ExportDatabaseAsync(profile, request, cancellationToken);
                if (!IsCurrentExportTarget(operationId, profile, generationId, database)) return;
                ExportResults = F("databaseExportSummary", result.CollectionCount, result.DocumentCount) + Environment.NewLine
                    + F("folderLine", result.OutputDirectory) + Environment.NewLine
                    + T("manifestLine") + (result.IsTruncated ? Environment.NewLine + T("exportLimitWarning") : string.Empty);
                StatusMessage = F("databaseExported", result.DocumentCount);
            });
        }
        finally
        {
            if (_activeExportOperationId == operationId)
                IsExportInProgress = false;
        }
    }

    private bool IsCurrentExportTarget(Guid operationId, ConnectionProfile profile, Guid? generationId, string database) =>
        _activeExportOperationId == operationId
        && ReferenceEquals(profile, SelectedProfile)
        && generationId == SelectedProfile?.SourceGenerationId
        && string.Equals(database, SelectedDatabase, StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanImportDatabase))]
    private async Task ImportDatabaseAsync()
    {
        if (!CanImportDatabase || _importPreviewSnapshot is not { } preview)
        {
            return;
        }

        var request = new DatabaseImportRequest(preview.SourceDirectory, preview.Database,
            preview.Policy, ImportConfirmation, preview.RestoreDefinitions)
        {
            PreviewFingerprint = preview.Plan!.Fingerprint,
            RestartCheckpointId = IsRecoveryVerified(ImportCheckpointKind.LogicalPackage)
                ? SelectedImportCheckpoint?.Checkpoint.Id : null,
            Progress = new Progress<DatabaseImportProgress>(progress =>
            {
                if (!ReferenceEquals(preview.Profile, SelectedProfile)
                    || preview.GenerationId != SelectedProfile?.SourceGenerationId
                    || !string.Equals(preview.Database, SelectedDatabase, StringComparison.Ordinal)) return;
                var stage = progress.Stage switch
                {
                    DatabaseImportStage.Validating => T("importStageValidating"),
                    DatabaseImportStage.CreatingCollections => T("importStageCreating"),
                    DatabaseImportStage.ImportingDocuments => T("importStageWriting"),
                    DatabaseImportStage.CreatingViews => T("importStageViews"),
                    DatabaseImportStage.CleaningUp => T("importStageCleanup"),
                    DatabaseImportStage.Completed => T("importStageCompleted"),
                    DatabaseImportStage.Failed when progress.OutcomeMayBePartial => T("importStageFailedPartial"),
                    _ => T("importStageFailed")
                };
                ImportResults = F("databaseImportProgress", stage, progress.CompletedCollections,
                    progress.TotalCollections, progress.ProcessedDocuments, progress.TotalDocuments,
                    progress.InsertedDocuments, progress.ModifiedDocuments, progress.IgnoredDocuments,
                    progress.FailedDocuments,
                    string.IsNullOrWhiteSpace(progress.CurrentCollection) ? string.Empty : Environment.NewLine + progress.CurrentCollection);
            })
        };
        request.Validate();
        if (request.RestartCheckpointId is not null) InvalidateImportRecoveryVerification();

        if (!await RunAsync(async cancellationToken =>
            {
                DatabaseImportResult result;
                try
                {
                    result = await _workspace.ImportDatabaseAsync(preview.Profile, request, cancellationToken);
                }
                catch (DatabaseImportPreviewStaleException)
                {
                    if (ReferenceEquals(preview, _importPreviewSnapshot))
                    {
                        InvalidateImportPreview();
                        ImportPreview = T("databaseImportPlanStale");
                    }
                    throw;
                }
                catch (DatabaseDefinitionRestoreException exception)
                {
                    if (ReferenceEquals(preview.Profile, SelectedProfile)
                        && preview.GenerationId == SelectedProfile?.SourceGenerationId
                        && string.Equals(preview.Database, SelectedDatabase, StringComparison.Ordinal))
                    {
                        ImportResults = FormatDefinitionRestoreReport(exception.Report);
                        if (exception.OutcomeMayBePartial)
                        {
                            ImportResults += Environment.NewLine + T("definitionRestoreOutcomeMayBePartial");
                        }
                    }

                    throw;
                }
                if (!ReferenceEquals(preview.Profile, SelectedProfile)
                    || preview.GenerationId != SelectedProfile?.SourceGenerationId
                    || !string.Equals(preview.Database, SelectedDatabase, StringComparison.Ordinal)) return;
                ImportResults = F("databaseImportSummary", result.CollectionCount, result.DocumentCount, result.SourceDirectory, result.TargetDatabase,
                    request.DuplicatePolicy == DatabaseImportDuplicatePolicy.Upsert ? T("importPolicyUpsert") : T("importPolicyReject"));
                if (result.DefinitionRestoreReport is { } definitionsReport)
                {
                    ImportResults += Environment.NewLine + FormatDefinitionRestoreReport(definitionsReport);
                }
                StatusMessage = F("databaseImported", result.DocumentCount);
            }))
        {
            return;
        }

        if (ReferenceEquals(preview.Profile, SelectedProfile)
            && preview.GenerationId == SelectedProfile?.SourceGenerationId
            && string.Equals(preview.Database, SelectedDatabase, StringComparison.Ordinal))
            await LoadCollectionsAsync(preview.Database);
    }

    private static string FormatDefinitionRestoreReport(DatabaseDefinitionRestoreReport report)
    {
        report.Validate();
        var restored = report.Items.Count(item => item.Status == DatabaseDefinitionRestoreStatus.Restored);
        var omitted = report.Items.Count(item => item.Status == DatabaseDefinitionRestoreStatus.Omitted);
        var failed = report.Items.Count(item => item.Status == DatabaseDefinitionRestoreStatus.Failed);
        var blocked = report.Items.Count(item => item.Status == DatabaseDefinitionRestoreStatus.Blocked);
        var byId = report.Items.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var lines = report.Items.Select(item =>
        {
            var dependencies = item.DependsOn.Count == 0 ? string.Empty
                : " — " + F("definitionDependsOn", string.Join(", ", item.DependsOn.Select(id =>
                    T($"definitionKind{byId[id].Kind}") + ": " + SafeDefinitionLabel(byId[id].Target))));
            var failure = item.ErrorCode is null ? string.Empty
                : " — " + T(item.ErrorCode is "NotApplied" or "DefinitionConflict" or "CreateOutcomeUnknown"
                    or "DefinitionPreflightRejected" or "RestoreAborted"
                    ? "definitionError" + item.ErrorCode : "definitionErrorGeneric");
            return $"{T($"definitionKind{item.Kind}")}: {SafeDefinitionLabel(item.Target)} — "
                + T($"definitionStatus{item.Status}") + " — " + T($"definitionCollision{item.Collision}")
                + dependencies + failure;
        });
        return F("databaseDefinitionRestoreSummary", restored, omitted, failed, blocked)
            + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string FormatDefinitionImportPreview(DatabaseDefinitionImportPreview preview)
    {
        var lines = preview.Items.Select(item =>
        {
            var target = SafeDefinitionLabel(item.Target);
            if (item.Name is not null) target += " / " + SafeDefinitionLabel(item.Name);
            var dependencies = item.DependsOn.Count == 0 ? string.Empty
                : " — " + F("definitionDependsOn", string.Join(", ", item.DependsOn.Select(SafeDefinitionLabel)));
            return $"{T($"definitionKind{item.Kind}")}: {target} — "
                + T($"definitionPlanned{item.Status}") + " — " + T($"definitionCollision{item.Collision}")
                + dependencies;
        });
        return T("definitionPreviewNote") + Environment.NewLine + string.Join(Environment.NewLine, lines);
    }

    private static string FormatDatabaseImportPreview(ImportPreviewSnapshot preview)
    {
        var plan = preview.Plan!;
        var policy = plan.DuplicatePolicy == DatabaseImportDuplicatePolicy.Upsert
            ? T("importPolicyUpsert") : T("importPolicyReject");
        var definitions = plan.RestoreDefinitions ? T("restoreDefinitionsYes") : T("restoreDefinitionsNo");
        var existing = plan.DestinationNamespaces.Count == 0
            ? T("databaseImportNoNamespaces")
            : string.Join(", ", plan.DestinationNamespaces.Select(SafeDefinitionLabel));
        var lines = plan.Items.Select(item =>
        {
            var collision = plan.DuplicatePolicy == DatabaseImportDuplicatePolicy.Upsert
                && item.Kind == DatabaseImportObjectKind.Collection
                && item.DestinationNamespaceExists
                    ? T("databaseImportUpsertItem")
                    : T(item.DestinationNamespaceExists ? "definitionCollisionConflicting" : "definitionCollisionNone");
            return F("databaseImportPlanItem",
                T(item.Kind == DatabaseImportObjectKind.Collection ? "databaseImportCollection" : "databaseImportView"),
                SafeDefinitionLabel(item.Namespace), item.DocumentCount, collision);
        });
        var destinationState = plan.DestinationIsEmpty
            ? T("databaseImportTargetEmpty")
            : plan.RestoreDefinitions
                ? T("databaseImportTargetDefinitionsBlocked")
                : plan.CanImport
                    ? T("databaseImportTargetUpsertCompatible")
                    : plan.DuplicatePolicy == DatabaseImportDuplicatePolicy.Upsert
                        ? T("databaseImportTargetUpsertBlocked")
                        : T("databaseImportTargetNotEmpty");
        return F("databaseImportPreview", SafeDefinitionLabel(preview.SourceDirectory),
            SafeDefinitionLabel(plan.TargetDatabase), policy, definitions)
            + Environment.NewLine
            + F("databaseImportPlanTotals", plan.Items.Count, plan.TotalDocuments)
            + Environment.NewLine
            + F("databaseImportDestinationState", destinationState, existing)
            + Environment.NewLine
            + T("definitionPreviewNote")
            + (plan.Items.Count == 0 ? string.Empty : Environment.NewLine + string.Join(Environment.NewLine, lines))
            + (plan.DuplicatePolicy == DatabaseImportDuplicatePolicy.Upsert
                ? Environment.NewLine + T("databaseImportUpsertNote") : string.Empty);
    }

    private static string SafeDefinitionLabel(string value)
    {
        var printable = new string(value.Where(character => !char.IsControl(character) &&
            character is not '\u2028' and not '\u2029').Take(100).ToArray());
        return value.Length > 100 ? printable + "…" : printable;
    }

    [RelayCommand(CanExecute = nameof(CanLoadDatabaseStats))]
    private async Task LoadDatabaseStatsAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetDatabaseStatsAsync(SelectedProfile, SelectedDatabase, cancellationToken);
            StatusMessage = F("databaseStatsLoaded", SelectedDatabase);
        });
    }

    [RelayCommand(CanExecute = nameof(CanDropDatabase))]
    private async Task DropDatabaseAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        var database = SelectedDatabase;
        if (!await RunAsync(cancellationToken => _workspace.DropDatabaseAsync(
            SelectedProfile,
            new DatabaseDropRequest(database, DropDatabaseConfirmation),
            cancellationToken)))
        {
            return;
        }

        DropDatabaseConfirmation = string.Empty;
        Collections.Clear();
        SelectedCollection = null;
        SelectedDatabase = null;
        await LoadDatabasesAsync();
        await RecordAuditAsync("database.drop", SelectedProfile, database, null, T("databaseRemoved").Replace("{0}", database, StringComparison.Ordinal));
        StatusMessage = F("databaseRemoved", database);
    }

    [RelayCommand(CanExecute = nameof(CanCreateDatabase))]
    private async Task CreateDatabaseAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        var request = new DatabaseCreateRequest(NewDatabaseName, NewDatabaseInitialCollection, NewDatabaseConfirmation);
        if (!await RunAsync(cancellationToken => _workspace.CreateDatabaseAsync(SelectedProfile, request, cancellationToken)))
        {
            return;
        }

        var database = request.Database.Trim();
        var collection = request.InitialCollection.Trim();
        NewDatabaseName = string.Empty;
        NewDatabaseInitialCollection = string.Empty;
        NewDatabaseConfirmation = string.Empty;
        await LoadDatabasesAsync();
        SelectedDatabase = database;
        await RecordAuditAsync("database.create", SelectedProfile, database, collection, F("databaseCreated", database, collection));
        StatusMessage = F("databaseCreated", database, collection);
    }
}
