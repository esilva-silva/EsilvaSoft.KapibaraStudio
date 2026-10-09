using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private static readonly JsonSerializerOptions StandaloneMappingOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [ObservableProperty] private string _standaloneSourceFile = string.Empty;
    [ObservableProperty] private string _standaloneTargetCollection = string.Empty;
    [ObservableProperty] private bool _standaloneUseCsv;
    [ObservableProperty] private bool _standaloneUseJsonArray;
    [ObservableProperty] private bool _standaloneUseUpsert;
    [ObservableProperty] private string _standaloneMappingJson =
        "[{\"SourceColumn\":\"id\",\"TargetField\":\"_id\",\"Type\":\"ObjectId\"}]";
    [ObservableProperty] private string _standaloneConfirmation = string.Empty;
    [ObservableProperty] private string _standalonePreviewText = string.Empty;
    [ObservableProperty] private string _standaloneImportResults = string.Empty;
    private StandalonePreviewSnapshot? _standaloneSnapshot;

    private sealed record StandalonePreviewSnapshot(
        ConnectionProfile Profile, Guid? GenerationId, string Database, string Collection,
        string SourceFile, TransferImportSchema Schema, string MappingJson, bool UseCsv, bool UseJsonArray,
        DatabaseImportDuplicatePolicy Policy);

    public bool CanPreviewStandaloneImport => SelectedProfile is { IsReadOnly: false }
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && !string.IsNullOrWhiteSpace(StandaloneSourceFile)
        && !string.IsNullOrWhiteSpace(StandaloneTargetCollection);

    public bool CanImportStandalone => CanPreviewStandaloneImport
        && _standaloneSnapshot is { } snapshot
        && IsStandaloneSnapshotCurrent(snapshot)
        && string.Equals(StandaloneConfirmation, snapshot.Database, StringComparison.Ordinal)
        && (SelectedImportCheckpoint is null || IsRecoveryVerified(ImportCheckpointKind.StandaloneFile));

    partial void OnStandaloneSourceFileChanged(string value) => InvalidateStandalonePreview();
    partial void OnStandaloneTargetCollectionChanged(string value) => InvalidateStandalonePreview();
    partial void OnStandaloneUseCsvChanged(bool value)
    {
        if (value) StandaloneUseJsonArray = false;
        InvalidateStandalonePreview();
    }

    partial void OnStandaloneUseJsonArrayChanged(bool value)
    {
        if (value) StandaloneUseCsv = false;
        InvalidateStandalonePreview();
    }
    partial void OnStandaloneUseUpsertChanged(bool value) => InvalidateStandalonePreview();
    partial void OnStandaloneMappingJsonChanged(string value) => InvalidateStandalonePreview();
    partial void OnStandaloneConfirmationChanged(string value) => NotifyStandaloneImportState();

    private void NotifyStandaloneImportTargetChanged()
    {
        ClearImportRecoveryForTargetChange();
        InvalidateStandalonePreview();
    }

    private void InvalidateStandalonePreview()
    {
        _standaloneSnapshot = null;
        StandalonePreviewText = string.Empty;
        StandaloneConfirmation = string.Empty;
        InvalidateImportRecoveryVerification();
        NotifyStandaloneImportState();
    }

    private void NotifyStandaloneImportState()
    {
        OnPropertyChanged(nameof(CanPreviewStandaloneImport));
        OnPropertyChanged(nameof(CanImportStandalone));
        PreviewStandaloneImportCommand.NotifyCanExecuteChanged();
        ImportStandaloneCommand.NotifyCanExecuteChanged();
    }

    private bool IsStandaloneSnapshotCurrent(StandalonePreviewSnapshot snapshot) =>
        ReferenceEquals(snapshot.Profile, SelectedProfile)
        && snapshot.GenerationId == SelectedProfile?.SourceGenerationId
        && string.Equals(snapshot.Database, SelectedDatabase, StringComparison.Ordinal)
        && string.Equals(snapshot.Collection, StandaloneTargetCollection.Trim(), StringComparison.Ordinal)
        && string.Equals(snapshot.SourceFile, StandaloneSourceFile.Trim(), StringComparison.Ordinal)
        && string.Equals(snapshot.MappingJson, StandaloneMappingJson, StringComparison.Ordinal)
        && snapshot.UseCsv == StandaloneUseCsv
        && snapshot.UseJsonArray == StandaloneUseJsonArray
        && snapshot.Policy == (StandaloneUseUpsert ? DatabaseImportDuplicatePolicy.Upsert : DatabaseImportDuplicatePolicy.Reject);

    private TransferImportSchema BuildStandaloneSchema()
    {
        if (!StandaloneUseCsv)
            return new TransferImportSchema(StandaloneUseJsonArray ? TransferImportFormat.JsonArray : TransferImportFormat.Ndjson);
        try
        {
            var columns = JsonSerializer.Deserialize<TransferColumnMapping[]>(StandaloneMappingJson, StandaloneMappingOptions);
            return new TransferImportSchema(TransferImportFormat.Csv, columns).Validate();
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            throw new ArgumentException(T("standaloneMappingInvalid"), nameof(StandaloneMappingJson));
        }
    }

    [RelayCommand(CanExecute = nameof(CanPreviewStandaloneImport))]
    private async Task PreviewStandaloneImportAsync()
    {
        if (SelectedProfile is not { IsReadOnly: false } profile || SelectedDatabase is not { } database) return;
        var source = StandaloneSourceFile.Trim();
        var collection = StandaloneTargetCollection.Trim();
        var mapping = StandaloneMappingJson;
        var useCsv = StandaloneUseCsv;
        var useJsonArray = StandaloneUseJsonArray;
        var policy = StandaloneUseUpsert ? DatabaseImportDuplicatePolicy.Upsert : DatabaseImportDuplicatePolicy.Reject;
        TransferImportSchema schema;
        try { schema = BuildStandaloneSchema(); }
        catch (ArgumentException) { StandalonePreviewText = T("standaloneMappingInvalid"); return; }

        await RunAsync(async cancellationToken =>
        {
            var preview = await _workspace.PreviewStandaloneImportAsync(source, schema, cancellationToken);
            var snapshot = new StandalonePreviewSnapshot(profile, profile.SourceGenerationId, database,
                collection, source, schema, mapping, useCsv, useJsonArray, policy);
            if (!IsStandaloneSnapshotCurrent(snapshot)) return;
            _standaloneSnapshot = snapshot;
            StandalonePreviewText = F("standalonePreviewHeader", source, database, collection,
                useCsv ? "CSV" : useJsonArray ? "JSON array" : "NDJSON", policy == DatabaseImportDuplicatePolicy.Upsert ? T("importPolicyUpsert") : T("importPolicyReject"),
                preview.SampledRows, preview.HasMoreRows ? T("standalonePreviewMore") : string.Empty)
                + Environment.NewLine
                + string.Join(Environment.NewLine, preview.Fields.Select(field =>
                    field.SourceColumn is null ? $"{field.Name}: {field.Type}" : $"{field.SourceColumn} → {field.Name}: {field.Type}"));
            NotifyStandaloneImportState();
        });
    }

    [RelayCommand(CanExecute = nameof(CanImportStandalone))]
    private async Task ImportStandaloneAsync()
    {
        if (!CanImportStandalone || _standaloneSnapshot is not { } snapshot) return;
        var request = new StandaloneImportRequest(snapshot.SourceFile, snapshot.Database, snapshot.Collection,
            snapshot.Schema, snapshot.Policy, StandaloneConfirmation)
        {
            RestartCheckpointId = IsRecoveryVerified(ImportCheckpointKind.StandaloneFile)
                ? SelectedImportCheckpoint?.Checkpoint.Id : null,
            Progress = new Progress<DatabaseImportProgress>(progress =>
            {
                if (!IsStandaloneSnapshotCurrent(snapshot)) return;
                var stage = progress.Stage switch
                {
                    DatabaseImportStage.Validating => T("importStageValidating"),
                    DatabaseImportStage.CreatingCollections => T("importStageCreating"),
                    DatabaseImportStage.ImportingDocuments => T("importStageWriting"),
                    DatabaseImportStage.CleaningUp => T("importStageCleanup"),
                    DatabaseImportStage.Completed => T("importStageCompleted"),
                    DatabaseImportStage.Failed when progress.OutcomeMayBePartial => T("importStageFailedPartial"),
                    _ => T("importStageFailed")
                };
                StandaloneImportResults = F("standaloneProgress", stage,
                    progress.ProcessedDocuments, progress.TotalDocuments,
                    progress.InsertedDocuments, progress.ModifiedDocuments, progress.FailedDocuments);
            })
        }.Validate();
        if (request.RestartCheckpointId is not null) InvalidateImportRecoveryVerification();

        if (!await RunAsync(async cancellationToken =>
            {
                var result = await _workspace.ImportStandaloneAsync(snapshot.Profile, request, cancellationToken);
                if (!IsStandaloneSnapshotCurrent(snapshot)) return;
                StandaloneImportResults = F("standaloneImportDone", result.DocumentCount, result.TargetCollection);
                StatusMessage = F("databaseImported", result.DocumentCount);
            })) return;

        if (IsStandaloneSnapshotCurrent(snapshot))
            await LoadCollectionsAsync(snapshot.Database);
    }
}
