using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public sealed record ImportCheckpointChoice(ImportCheckpoint Checkpoint, string Display);

    public ObservableCollection<ImportCheckpointChoice> PendingImportCheckpoints { get; } = [];

    [ObservableProperty] private ImportCheckpointChoice? _selectedImportCheckpoint;
    [ObservableProperty] private string _importRecoveryStatus = string.Empty;

    private sealed record ImportRecoveryVerification(ConnectionProfile Profile, Guid? GenerationId,
        Guid CheckpointId, string Signature, ImportRestartDecision Decision);
    private ImportRecoveryVerification? _importRecoveryVerification;

    public bool CanLoadImportCheckpoints => SelectedProfile is not null;
    public bool CanVerifyImportRecovery => SelectedProfile is { IsReadOnly: false } profile
        && SelectedImportCheckpoint is { } choice
        && choice.Checkpoint.ProfileId == profile.Id
        && choice.Checkpoint.SourceGenerationId == profile.SourceGenerationId
        && !string.IsNullOrWhiteSpace(SelectedDatabase)
        && (choice.Checkpoint.Kind == ImportCheckpointKind.LogicalPackage
            ? !string.IsNullOrWhiteSpace(ImportSourceDirectory)
            : !string.IsNullOrWhiteSpace(StandaloneSourceFile));

    public bool ShowPackageRestartAction =>
        SelectedImportCheckpoint?.Checkpoint.Kind == ImportCheckpointKind.LogicalPackage;
    public bool ShowNormalPackageImportAction => !ShowPackageRestartAction;
    public bool ShowStandaloneRestartAction =>
        SelectedImportCheckpoint?.Checkpoint.Kind == ImportCheckpointKind.StandaloneFile;
    public bool ShowNormalStandaloneImportAction => !ShowStandaloneRestartAction;

    private bool IsRecoveryVerified(ImportCheckpointKind kind) =>
        SelectedImportCheckpoint is { } choice
        && choice.Checkpoint.Kind == kind
        && _importRecoveryVerification is { Decision.CanRestartFromBeginning: true } verification
        && verification.CheckpointId == choice.Checkpoint.Id
        && ReferenceEquals(verification.Profile, SelectedProfile)
        && verification.GenerationId == SelectedProfile?.SourceGenerationId
        && string.Equals(verification.Signature, CurrentRecoverySignature(), StringComparison.Ordinal);

    private string CurrentRecoverySignature() => JsonSerializer.Serialize(new
    {
        ProfileId = SelectedProfile?.Id,
        GenerationId = SelectedProfile?.SourceGenerationId,
        SelectedDatabase,
        SourceDirectory = ImportSourceDirectory.Trim(),
        ImportUseUpsert,
        ImportRestoreDefinitions,
        SourceFile = StandaloneSourceFile.Trim(),
        TargetCollection = StandaloneTargetCollection.Trim(),
        StandaloneUseCsv,
        StandaloneUseJsonArray,
        StandaloneUseUpsert,
        StandaloneMappingJson
    });

    partial void OnSelectedImportCheckpointChanged(ImportCheckpointChoice? value)
    {
        InvalidateImportRecoveryVerification();
        ImportRecoveryStatus = value is null ? T("importRecoverySelect") : T("importRecoveryReselect");
    }

    private void InvalidateImportRecoveryVerification()
    {
        _importRecoveryVerification = null;
        if (SelectedImportCheckpoint is not null)
            ImportRecoveryStatus = T("importRecoveryReselect");
        OnPropertyChanged(nameof(CanVerifyImportRecovery));
        OnPropertyChanged(nameof(ShowPackageRestartAction));
        OnPropertyChanged(nameof(ShowNormalPackageImportAction));
        OnPropertyChanged(nameof(ShowStandaloneRestartAction));
        OnPropertyChanged(nameof(ShowNormalStandaloneImportAction));
        VerifyImportRecoveryCommand.NotifyCanExecuteChanged();
        NotifyImportCommandState();
        NotifyStandaloneImportState();
    }

    private void ClearImportRecoveryForTargetChange()
    {
        PendingImportCheckpoints.Clear();
        SelectedImportCheckpoint = null;
        InvalidateImportRecoveryVerification();
        ImportRecoveryStatus = string.Empty;
        OnPropertyChanged(nameof(CanLoadImportCheckpoints));
        LoadImportCheckpointsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanLoadImportCheckpoints))]
    private async Task LoadImportCheckpointsAsync()
    {
        var profile = SelectedProfile;
        if (profile is null) return;
        var generation = profile.SourceGenerationId;
        ImportRecoveryStatus = T("importRecoveryLoading");
        await RunAsync(async cancellationToken =>
        {
            IReadOnlyList<ImportCheckpoint> pending;
            try { pending = await _workspace.GetPendingImportCheckpointsAsync(cancellationToken); }
            catch
            {
                if (ReferenceEquals(profile, SelectedProfile)) ImportRecoveryStatus = T("importRecoveryUnavailable");
                throw;
            }
            if (!ReferenceEquals(profile, SelectedProfile) || generation != SelectedProfile?.SourceGenerationId) return;
            PendingImportCheckpoints.Clear();
            SelectedImportCheckpoint = null;
            foreach (var item in pending.Where(item => item.ProfileId == profile.Id
                         && item.SourceGenerationId == generation).OrderByDescending(item => item.UpdatedAtUtc))
            {
                var kind = item.Kind == ImportCheckpointKind.LogicalPackage
                    ? T("importRecoveryPackage") : T("importRecoveryFile");
                var state = item.State switch
                {
                    ImportCheckpointState.Prepared => T("importRecoveryPrepared"),
                    ImportCheckpointState.Writing => T("importRecoveryWriting"),
                    _ => T("importRecoveryNeedsReview")
                };
                var target = item.TargetCollection is null ? item.TargetDatabase
                    : item.TargetDatabase + "." + item.TargetCollection;
                PendingImportCheckpoints.Add(new ImportCheckpointChoice(item,
                    F("importRecoveryItem", kind, target, state, item.ProcessedDocuments,
                        item.TotalDocuments, item.UpdatedAtUtc.ToLocalTime().ToString("g", CultureInfo.InvariantCulture))));
            }
            ImportRecoveryStatus = PendingImportCheckpoints.Count == 0
                ? T("importRecoveryNone") : T("importRecoverySelect");
        });
    }

    [RelayCommand(CanExecute = nameof(CanVerifyImportRecovery))]
    private async Task VerifyImportRecoveryAsync()
    {
        if (SelectedProfile is not { IsReadOnly: false } profile
            || SelectedImportCheckpoint is not { } choice
            || SelectedDatabase is not { } database) return;
        var checkpoint = choice.Checkpoint;
        var signature = CurrentRecoverySignature();
        var generation = profile.SourceGenerationId;
        DatabaseImportRequest? packageRequest = null;
        StandaloneImportRequest? fileRequest = null;
        try
        {
            if (checkpoint.Kind == ImportCheckpointKind.LogicalPackage)
            {
                var policy = ImportUseUpsert ? DatabaseImportDuplicatePolicy.Upsert : DatabaseImportDuplicatePolicy.Reject;
                packageRequest = new DatabaseImportRequest(ImportSourceDirectory.Trim(), database, policy,
                    database, ImportRestoreDefinitions).Validate();
            }
            else
            {
                var schema = BuildStandaloneSchema();
                var policy = StandaloneUseUpsert ? DatabaseImportDuplicatePolicy.Upsert : DatabaseImportDuplicatePolicy.Reject;
                fileRequest = new StandaloneImportRequest(StandaloneSourceFile.Trim(), database,
                    StandaloneTargetCollection.Trim(), schema, policy, database).Validate();
            }
        }
        catch (ArgumentException)
        {
            ImportRecoveryStatus = T("importRecoveryInvalidPlan");
            return;
        }
        InvalidateImportRecoveryVerification();
        ImportRecoveryStatus = T("importRecoveryChecking");
        await RunAsync(async cancellationToken =>
        {
            ImportRestartDecision decision;
            try
            {
                if (packageRequest is not null)
                    decision = await _workspace.InspectDatabaseImportRestartAsync(profile, packageRequest,
                        checkpoint.Id, cancellationToken);
                else
                    decision = await _workspace.InspectStandaloneImportRestartAsync(profile, fileRequest!,
                        checkpoint.Id, cancellationToken);
            }
            catch
            {
                if (ReferenceEquals(profile, SelectedProfile)
                    && SelectedImportCheckpoint?.Checkpoint.Id == checkpoint.Id)
                    ImportRecoveryStatus = T("importRecoveryUnavailable");
                throw;
            }

            if (!ReferenceEquals(profile, SelectedProfile)
                || generation != SelectedProfile?.SourceGenerationId
                || SelectedImportCheckpoint?.Checkpoint.Id != checkpoint.Id
                || !string.Equals(signature, CurrentRecoverySignature(), StringComparison.Ordinal)) return;
            ImportRecoveryStatus = RecoveryDecisionText(decision);
            if (decision.CanRestartFromBeginning)
                _importRecoveryVerification = new ImportRecoveryVerification(profile,
                    generation, checkpoint.Id, signature, decision);
            NotifyImportCommandState();
            NotifyStandaloneImportState();
        });
    }

    private static string RecoveryDecisionText(ImportRestartDecision decision) => decision.ReasonCode switch
    {
        "restart-from-beginning" => T("importRecoveryEligible"),
        "profile-changed" => T("importRecoveryProfileChanged"),
        "source-changed" => T("importRecoverySourceChanged"),
        "plan-changed" => T("importRecoveryPlanChanged"),
        "target-not-empty" => T("importRecoveryTargetOccupied"),
        "kind-changed" or "destination-changed" => T("importRecoveryDestinationChanged"),
        "checkpoint-missing" => T("importRecoveryMissing"),
        _ => T("importRecoveryUnavailable")
    };
}
