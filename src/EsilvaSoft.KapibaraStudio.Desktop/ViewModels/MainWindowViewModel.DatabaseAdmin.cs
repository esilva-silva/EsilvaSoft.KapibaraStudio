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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    private string _newDatabaseUsername = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    private string _newDatabaseUserPassword = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    private string _newDatabaseUserRoles = "[{ \"role\": \"readWrite\", \"db\": \"selected_database\" }]";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    private string _newDatabaseUserConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseUserCommand))]
    private string _databaseUsernameToDrop = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDropDatabaseUser))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseUserCommand))]
    private string _databaseUserDropConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateDatabaseUserRoles), nameof(CanPreviewDatabaseUserRoles))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDatabaseUserRolesCommand), nameof(PreviewDatabaseUserRolesCommand))]
    private string _databaseRoleUsername = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateDatabaseUserRoles), nameof(CanPreviewDatabaseUserRoles))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDatabaseUserRolesCommand), nameof(PreviewDatabaseUserRolesCommand))]
    private string _databaseRolePayload = "[{ \"role\": \"read\", \"db\": \"selected_database\" }]";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateDatabaseUserRoles))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDatabaseUserRolesCommand))]
    private string _databaseRoleConfirmation = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdateDatabaseUserRoles))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDatabaseUserRolesCommand))]
    private bool _revokeDatabaseRoles;

    [ObservableProperty]
    private string _databaseUserRolesPreview = string.Empty;

    private string? _databaseUserRolesExpected;
    private string? _databaseUserRolesPreviewSignature;
    private Guid? _activeExportOperationId;
    private static readonly JsonSerializerOptions UserRolesPreviewOptions = new() { WriteIndented = true };

    public bool CanExportDatabase => !IsExportInProgress
        && SelectedProfile is not null
        && !string.IsNullOrWhiteSpace(SelectedDatabase);

    public bool CanCreateDatabase => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(NewDatabaseName)
        && !string.IsNullOrWhiteSpace(NewDatabaseInitialCollection)
        && string.Equals(NewDatabaseName.Trim(), NewDatabaseConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanDropDatabase => CanExportDatabase && string.Equals(SelectedDatabase, DropDatabaseConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanCreateDatabaseUser => SelectedProfile is not null
        && !SelectedProfile.IsReadOnly
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(NewDatabaseUsername)
        && !string.IsNullOrWhiteSpace(NewDatabaseUserPassword)
        && string.Equals(NewDatabaseUsername.Trim(), NewDatabaseUserConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanDropDatabaseUser => SelectedProfile is not null
        && !SelectedProfile.IsReadOnly
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && string.Equals(DatabaseUsernameToDrop.Trim(), DatabaseUserDropConfirmation.Trim(), StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(DatabaseUsernameToDrop);

    public bool CanUpdateDatabaseUserRoles => SelectedProfile is not null
        && !SelectedProfile.IsReadOnly
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(DatabaseRoleUsername)
        && !string.IsNullOrWhiteSpace(DatabaseRolePayload)
        && _databaseUserRolesExpected is not null
        && string.Equals(_databaseUserRolesPreviewSignature, CurrentDatabaseUserRoleSignature(), StringComparison.Ordinal)
        && string.Equals(DatabaseRoleUsername.Trim(), DatabaseRoleConfirmation.Trim(), StringComparison.Ordinal);

    public bool CanPreviewDatabaseUserRoles => SelectedProfile is not null
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(DatabaseRoleUsername)
        && !string.IsNullOrWhiteSpace(DatabaseRolePayload);

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

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadUsersAsync()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            return;
        }

        await RunUserAdministrationAsync(async cancellationToken =>
        {
            var result = await _workspace.GetUsersAsync(profile, cancellationToken);
            if (!ReferenceEquals(profile, SelectedProfile))
                return;
            AdministrationResults = result;
            StatusMessage = T("usersLoaded");
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadRolesAsync()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            return;
        }

        await RunRoleAdministrationAsync(async cancellationToken =>
        {
            var result = await _workspace.GetRolesAsync(profile, cancellationToken);
            if (!ReferenceEquals(profile, SelectedProfile))
                return;
            AdministrationResults = result;
            StatusMessage = T("rolesLoaded");
        });
    }

    [RelayCommand(CanExecute = nameof(CanCreateDatabaseUser))]
    private async Task CreateDatabaseUserAsync()
    {
        var profile = SelectedProfile;
        var database = SelectedDatabase;
        if (profile is null || string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        var request = new DatabaseUserCreateRequest(
            database,
            NewDatabaseUsername,
            NewDatabaseUserPassword,
            NewDatabaseUserRoles,
            NewDatabaseUserConfirmation);
        bool created;
        try
        {
            created = await RunUserAdministrationAsync(cancellationToken => _workspace.CreateUserAsync(profile, request, cancellationToken));
        }
        finally
        {
            NewDatabaseUserPassword = string.Empty;
        }
        if (!created)
        {
            return;
        }

        var username = request.Username.Trim();
        NewDatabaseUsername = string.Empty;
        NewDatabaseUserPassword = string.Empty;
        NewDatabaseUserRoles = "[{ \"role\": \"readWrite\", \"db\": \"selected_database\" }]";
        NewDatabaseUserConfirmation = string.Empty;
        StatusMessage = F("userCreated", username, database);
        await RecordSensitiveAdministrationAuditAsync("user.create", profile, database, T("auditUserCreated"));
    }

    [RelayCommand(CanExecute = nameof(CanDropDatabaseUser))]
    private async Task DropDatabaseUserAsync()
    {
        var profile = SelectedProfile;
        var database = SelectedDatabase;
        if (profile is null || string.IsNullOrWhiteSpace(database))
        {
            return;
        }

        var request = new DatabaseUserDropRequest(database, DatabaseUsernameToDrop, DatabaseUserDropConfirmation);
        if (!await RunUserAdministrationAsync(cancellationToken => _workspace.DropUserAsync(profile, request, cancellationToken)))
        {
            return;
        }

        var username = request.Username.Trim();
        DatabaseUsernameToDrop = string.Empty;
        DatabaseUserDropConfirmation = string.Empty;
        StatusMessage = F("userRemoved", username, database);
        await RecordSensitiveAdministrationAuditAsync("user.drop", profile, database, T("auditUserRemoved"));
    }

    [RelayCommand(CanExecute = nameof(CanUpdateDatabaseUserRoles))]
    private async Task UpdateDatabaseUserRolesAsync()
    {
        var profile = SelectedProfile;
        var database = SelectedDatabase;
        var expectedRoles = _databaseUserRolesExpected;
        if (profile is null || string.IsNullOrWhiteSpace(database) || expectedRoles is null || !CanUpdateDatabaseUserRoles)
        {
            return;
        }

        var request = new DatabaseUserRoleRequest(
            database,
            DatabaseRoleUsername,
            DatabaseRolePayload,
            DatabaseRoleConfirmation,
            RevokeDatabaseRoles,
            expectedRoles);
        if (!await RunUserAdministrationAsync(cancellationToken => _workspace.UpdateUserRolesAsync(profile, request, cancellationToken)))
        {
            return;
        }

        var username = request.Username.Trim();
        var action = request.Revoke ? "user.roles.revoke" : "user.roles.grant";
        DatabaseRoleUsername = string.Empty;
        DatabaseRolePayload = "[{ \"role\": \"read\", \"db\": \"selected_database\" }]";
        DatabaseRoleConfirmation = string.Empty;
        ClearDatabaseUserRolePreview();
        StatusMessage = F("rolesUpdated", username);
        await RecordSensitiveAdministrationAuditAsync(action, profile, database, T("auditUserRolesChanged"));
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

    [RelayCommand(CanExecute = nameof(CanPreviewDatabaseUserRoles))]
    private async Task PreviewDatabaseUserRolesAsync()
    {
        var profile = SelectedProfile;
        var database = SelectedDatabase;
        var username = DatabaseRoleUsername.Trim();
        var rolesJson = DatabaseRolePayload;
        var revoke = RevokeDatabaseRoles;
        if (profile is null || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(username))
            return;

        ClearDatabaseUserRolePreview();
        var signature = BuildDatabaseUserRoleSignature(profile, database, username, rolesJson, revoke);
        if (!await RunUserAdministrationAsync(async cancellationToken =>
            {
                var current = await _workspace.GetUserRolesAsync(profile, database, username, cancellationToken);
                if (!ReferenceEquals(profile, SelectedProfile)
                    || !string.Equals(database, SelectedDatabase, StringComparison.Ordinal)
                    || !string.Equals(username, DatabaseRoleUsername.Trim(), StringComparison.Ordinal)
                    || !string.Equals(rolesJson, DatabaseRolePayload, StringComparison.Ordinal)
                    || revoke != RevokeDatabaseRoles
                    || !string.Equals(signature, CurrentDatabaseUserRoleSignature(), StringComparison.Ordinal))
                    return;
                if (current is null)
                    throw new InvalidOperationException(T("userMissingForRoles"));

                _databaseUserRolesExpected = current;
                _databaseUserRolesPreviewSignature = signature;
                DatabaseUserRolesPreview = BuildDatabaseUserRolesPreview(current, rolesJson, revoke);
                OnPropertyChanged(nameof(CanUpdateDatabaseUserRoles));
                UpdateDatabaseUserRolesCommand.NotifyCanExecuteChanged();
            }))
            ClearDatabaseUserRolePreview();
    }

    private string CurrentDatabaseUserRoleSignature() => BuildDatabaseUserRoleSignature(
        SelectedProfile, SelectedDatabase, DatabaseRoleUsername.Trim(), DatabaseRolePayload, RevokeDatabaseRoles);

    private static string BuildDatabaseUserRoleSignature(
        ConnectionProfile? profile,
        string? database,
        string username,
        string rolesJson,
        bool revoke) => string.Join("\u001f",
        profile?.Id.ToString("D") ?? string.Empty,
        profile?.SourceGenerationId?.ToString("D") ?? string.Empty,
        database ?? string.Empty,
        username,
        rolesJson,
        revoke.ToString());

    private static string BuildDatabaseUserRolesPreview(string currentJson, string requestedJson, bool revoke)
    {
        var before = JsonNode.Parse(currentJson)!.AsArray();
        var after = before.DeepClone().AsArray();
        var requested = JsonNode.Parse(requestedJson)!.AsArray();
        foreach (var role in requested)
        {
            var roleName = role?["role"]?.GetValue<string>() ?? string.Empty;
            var database = role?["db"]?.GetValue<string>() ?? string.Empty;
            var existing = after.FirstOrDefault(item =>
                string.Equals(item?["role"]?.GetValue<string>(), roleName, StringComparison.Ordinal)
                && string.Equals(item?["db"]?.GetValue<string>(), database, StringComparison.Ordinal));
            if (revoke)
            {
                if (existing is not null) after.Remove(existing);
            }
            else if (existing is null)
            {
                after.Add(role?.DeepClone());
            }
        }
        return JsonSerializer.Serialize(new { before, after }, UserRolesPreviewOptions);
    }

    private void ClearDatabaseUserRolePreview()
    {
        _databaseUserRolesExpected = null;
        _databaseUserRolesPreviewSignature = null;
        DatabaseUserRolesPreview = string.Empty;
        OnPropertyChanged(nameof(CanUpdateDatabaseUserRoles));
        UpdateDatabaseUserRolesCommand.NotifyCanExecuteChanged();
    }

    partial void OnDatabaseRoleUsernameChanged(string value) => ClearDatabaseUserRolePreview();
    partial void OnDatabaseRolePayloadChanged(string value) => ClearDatabaseUserRolePreview();
    partial void OnRevokeDatabaseRolesChanged(bool value) => ClearDatabaseUserRolePreview();

    private void NotifyDatabaseUserRoleTargetChanged()
    {
        ClearDatabaseUserRolePreview();
        OnPropertyChanged(nameof(CanPreviewDatabaseUserRoles));
        PreviewDatabaseUserRolesCommand.NotifyCanExecuteChanged();
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
