using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private static string T(string key) => LocalizationViewModel.Current.Resolve(key);
    private static string F(string key, params object?[] arguments) => LocalizationViewModel.Current.Format(key, arguments);
    private readonly WorkspaceService _workspace;

    private readonly HashSet<string> _knownFields = new(StringComparer.Ordinal);

    private CancellationTokenSource? _operationCancellation;

    private readonly bool _autoLoadCollections;

    public MainWindowViewModel(WorkspaceService workspace, bool autoLoadCollections = true)
    {
        workspace.OperationLocalizer = LocalizationViewModel.Current.ResolveOperationText;
        _workspace = workspace;
        _autoLoadCollections = autoLoadCollections;
        ExportResults = T("exportDatabaseInitial");
        ImportResults = T("importDatabaseInitial");
        IndexResults = T("indexesInitial");
        AutocompleteSuggestions = T("autocompleteFieldsNote");
        ScriptResults = T("scriptNoOutput");
        LoadProfilesCommand.Execute(null);
        _ = LoadScriptHistoryAsync();
    }

    public ObservableCollection<string> Databases { get; } = [];

    public ObservableCollection<string> Collections { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(HasNoSelectedProfile))]
    [NotifyPropertyChangedFor(nameof(CollectionContextHint))]
    [NotifyPropertyChangedFor(nameof(SelectedProfileDetails))]
    [NotifyPropertyChangedFor(nameof(CanExportDatabase))]
    [NotifyPropertyChangedFor(nameof(CanCreateDatabase))]
    [NotifyPropertyChangedFor(nameof(CanDropDatabase))]
    [NotifyPropertyChangedFor(nameof(CanLoadDatabaseStats))]
    [NotifyPropertyChangedFor(nameof(CanLoadCollectionStats))]
    [NotifyPropertyChangedFor(nameof(CanImportDatabase))]
    [NotifyPropertyChangedFor(nameof(CanCreateCollection))]
    [NotifyPropertyChangedFor(nameof(CanRenameCollection))]
    [NotifyPropertyChangedFor(nameof(CanDropCollection))]
    [NotifyPropertyChangedFor(nameof(CanConfigureCollectionValidation))]
    [NotifyPropertyChangedFor(nameof(CanValidateCollectionIntegrity))]
    [NotifyPropertyChangedFor(nameof(CanCompactCollection))]
    [NotifyPropertyChangedFor(nameof(CanUpdateView))]
    [NotifyCanExecuteChangedFor(nameof(DeleteSelectedProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditSelectedProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateSelectedProfileCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestSelectedConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadDatabasesCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SampleSchemaFieldsCommand))]
    [NotifyCanExecuteChangedFor(nameof(InferCollectionValidatorCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountExactDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountEstimatedDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(GetDistinctValuesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSavedQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExplainQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteAggregationCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertManyDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadIndexesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteScriptCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadServerStatusCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadTopologyCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCurrentOperationsCommand))]
    [NotifyCanExecuteChangedFor(nameof(KillOperationCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadUsersCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDatabaseUserRolesCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadRolesCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadDatabaseStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateCollectionIntegrityCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompactCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateViewCommand))]
    private ConnectionProfile? _selectedProfile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExportDatabase))]
    [NotifyPropertyChangedFor(nameof(CanDropDatabase))]
    [NotifyPropertyChangedFor(nameof(CanLoadDatabaseStats))]
    [NotifyPropertyChangedFor(nameof(CanLoadCollectionStats))]
    [NotifyPropertyChangedFor(nameof(CanImportDatabase))]
    [NotifyPropertyChangedFor(nameof(CanCreateCollection))]
    [NotifyPropertyChangedFor(nameof(CanRenameCollection))]
    [NotifyPropertyChangedFor(nameof(CanDropCollection))]
    [NotifyPropertyChangedFor(nameof(CanConfigureCollectionValidation))]
    [NotifyPropertyChangedFor(nameof(CanValidateCollectionIntegrity))]
    [NotifyPropertyChangedFor(nameof(CanCompactCollection))]
    [NotifyPropertyChangedFor(nameof(CanUpdateView))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SampleSchemaFieldsCommand))]
    [NotifyCanExecuteChangedFor(nameof(InferCollectionValidatorCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountExactDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountEstimatedDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(GetDistinctValuesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSavedQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExplainQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteAggregationCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertManyDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadIndexesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadDatabaseStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateDatabaseUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropDatabaseUserCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDatabaseUserRolesCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionStatsCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(ImportDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateCollectionIntegrityCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompactCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateViewCommand))]
    [NotifyPropertyChangedFor(nameof(CollectionContextHint))]
    private string? _selectedDatabase;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExecuteQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(SampleSchemaFieldsCommand))]
    [NotifyCanExecuteChangedFor(nameof(InferCollectionValidatorCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountExactDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(CountEstimatedDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(GetDistinctValuesCommand))]
    [NotifyCanExecuteChangedFor(nameof(SaveSavedQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExplainQueryCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteAggregationCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviewDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(InsertManyDocumentsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ReplaceDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(UpdateDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteDocumentCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadIndexesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CreateIndexCommand))]
    [NotifyPropertyChangedFor(nameof(CanLoadCollectionStats))]
    [NotifyCanExecuteChangedFor(nameof(LoadCollectionStatsCommand))]
    [NotifyPropertyChangedFor(nameof(CanDropIndex))]
    [NotifyCanExecuteChangedFor(nameof(DropIndexCommand))]
    [NotifyCanExecuteChangedFor(nameof(RenameCollectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DropCollectionCommand))]
    [NotifyPropertyChangedFor(nameof(CanConfigureCollectionValidation))]
    [NotifyPropertyChangedFor(nameof(CanValidateCollectionIntegrity))]
    [NotifyPropertyChangedFor(nameof(CanCompactCollection))]
    [NotifyCanExecuteChangedFor(nameof(ConfigureCollectionValidationCommand))]
    [NotifyCanExecuteChangedFor(nameof(ValidateCollectionIntegrityCommand))]
    [NotifyCanExecuteChangedFor(nameof(CompactCollectionCommand))]
    [NotifyPropertyChangedFor(nameof(CanUpdateView))]
    [NotifyCanExecuteChangedFor(nameof(UpdateViewCommand))]
    [NotifyPropertyChangedFor(nameof(CollectionContextHint))]
    private string? _selectedCollection;

    [ObservableProperty]
    private string _statusMessage = T("readyPeriod");

    [ObservableProperty]
    private string _footerMessage = T("localWorkspaceFooter");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCancelOperation))]
    [NotifyCanExecuteChangedFor(nameof(CancelOperationCommand))]
    private bool _isOperationRunning;

    public bool HasSelectedProfile => SelectedProfile is not null;

    public bool HasNoSelectedProfile => SelectedProfile is null;

    public string CollectionContextHint => SelectedProfile is null
        ? T("selectConnectionToStart")
        : string.IsNullOrWhiteSpace(SelectedDatabase)
            ? T("clickLoadDatabases")
            : string.IsNullOrWhiteSpace(SelectedCollection)
                ? T("chooseCollectionQuery")
                : F("readyContext", SelectedDatabase, SelectedCollection);

    public bool CanExecuteQuery => SelectedProfile is not null && !string.IsNullOrWhiteSpace(SelectedDatabase) && !string.IsNullOrWhiteSpace(SelectedCollection);

    public bool CanCancelOperation => IsOperationRunning;

    [RelayCommand(CanExecute = nameof(CanCancelOperation))]
    private void CancelOperation() => _operationCancellation?.Cancel();

    public string SelectedProfileDetails => SelectedProfile is null
        ? T("noConnectionSelected")
        : string.Join(" · ",
            F("folderValue", SelectedProfile.Folder ?? T("noFolder")),
            F("defaultDatabaseValue", SelectedProfile.DefaultDatabase ?? T("notDefined")),
            SelectedProfile.Environment ?? T("noEnvironment"),
            F("lastConnectionValue", SelectedProfile.LastConnectedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? T("never")));

    partial void OnSelectedProfileChanged(ConnectionProfile? value)
    {
        Databases.Clear();
        Collections.Clear();
        _knownFields.Clear();
        QueryHistory.Clear();
        SavedQueries.Clear();
        SelectedSavedQuery = null;
        SelectedDatabase = value?.DefaultDatabase;
        SelectedCollection = null;
        if (QueryHistoryEnabled)
        {
            _ = LoadQueryHistoryAsync();
        }

        _ = LoadSavedQueriesAsync();
    }

    partial void OnSelectedDatabaseChanged(string? value)
    {
        Collections.Clear();
        SelectedCollection = null;
        ExecuteQueryCommand.NotifyCanExecuteChanged();
        ExportDatabaseCommand.NotifyCanExecuteChanged();

        if (_autoLoadCollections && SelectedProfile is not null && !string.IsNullOrWhiteSpace(value))
        {
            _ = LoadCollectionsAsync(value);
        }
    }

    partial void OnSelectedCollectionChanged(string? value)
    {
        _knownFields.Clear();
        QuerySuggestions.Clear();
        SelectedQuerySuggestion = null;
        AutocompleteSuggestions = T("autocompleteFieldsNote");
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadDatabasesAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var databases = await _workspace.GetDatabasesAsync(SelectedProfile, cancellationToken);
            Databases.Clear();

            foreach (var database in databases)
            {
                Databases.Add(database);
            }

            SelectedDatabase ??= Databases.FirstOrDefault();
            StatusMessage = F("databasesLoaded", Databases.Count);
        });
    }

    private async Task RecordAuditAsync(string action, ConnectionProfile? profile, string? database, string? collection, string summary)
    {
        try
        {
            await _workspace.SaveAuditAsync(AuditEntry.Create(action, profile?.Id, database, collection, summary));
        }
        catch (Exception exception)
        {
            StatusMessage = F("auditNotRecorded", exception.Message);
        }
    }

    private async Task LoadCollectionsAsync(string database)
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            var collections = await _workspace.GetCollectionsAsync(SelectedProfile, database, cancellationToken);
            Collections.Clear();

            foreach (var collection in collections)
            {
                Collections.Add(collection);
            }

            StatusMessage = F("collectionsLoaded", Collections.Count, database);
        });
    }

    private bool TryGetCollectionContext(out ConnectionProfile profile, out string database, out string collection)
    {
        profile = SelectedProfile!;
        database = SelectedDatabase!;
        collection = SelectedCollection!;
        return profile is not null && !string.IsNullOrWhiteSpace(database) && !string.IsNullOrWhiteSpace(collection);
    }

    private async Task<bool> RunAsync(Func<CancellationToken, Task> operation)
    {
        var ownsCancellation = _operationCancellation is null;
        var cancellation = _operationCancellation ?? new CancellationTokenSource();

        if (ownsCancellation)
        {
            _operationCancellation = cancellation;
            IsOperationRunning = true;
            FooterMessage = T("operationInProgress");
        }

        var keepTerminalFooter = false;
        try
        {
            await operation(cancellation.Token);
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            StatusMessage = T("operationCancelledByUser");
            FooterMessage = T("operationNotReverted");
            keepTerminalFooter = true;
            return false;
        }
        catch (Exception exception)
        {
            SetError(DesktopOperationErrorMessages.Describe(exception));
            keepTerminalFooter = true;
            return false;
        }
        finally
        {
            if (ownsCancellation)
            {
                _operationCancellation = null;
                IsOperationRunning = false;
                cancellation.Dispose();
                if (!keepTerminalFooter)
                {
                    FooterMessage = T("localWorkspaceFooter");
                }
            }
        }
    }

    private void SetError(string message)
    {
        StatusMessage = F("errorPrefix", message);
        FooterMessage = T("operationNotCompleted");
    }
}
