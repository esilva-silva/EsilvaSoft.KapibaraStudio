using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Application;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    private ConnectionProfile? _operationsSnapshotProfile;
    private Dictionary<long, string> _currentOperationFingerprints = [];
    private readonly ActivityRefreshBudget _activityRefreshBudget = new();

    [ObservableProperty]
    private string _operationCommentFilter = string.Empty;

    partial void OnOperationCommentFilterChanged(string value)
    {
        _operationsSnapshotProfile = null;
        _currentOperationFingerprints = [];
        OnPropertyChanged(nameof(CanKillOperation));
        KillOperationCommand.NotifyCanExecuteChanged();
    }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKillOperation))]
    [NotifyCanExecuteChangedFor(nameof(KillOperationCommand))]
    private string _operationIdToKill = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanKillOperation))]
    [NotifyCanExecuteChangedFor(nameof(KillOperationCommand))]
    private string _operationKillConfirmation = string.Empty;

    [ObservableProperty]
    private string _administrationResults = T("adminInitial");

    public bool CanKillOperation => SelectedProfile is { IsReadOnly: false } profile
        && ReferenceEquals(profile, _operationsSnapshotProfile)
        && string.Equals(OperationIdToKill.Trim(), OperationKillConfirmation.Trim(), StringComparison.Ordinal)
        && TryGetExpectedOperationFingerprint(OperationIdToKill, out _);

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadServerStatusAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetServerStatusAsync(SelectedProfile, cancellationToken);
            StatusMessage = T("serverStatusLoaded");
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadCurrentOperationsAsync()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            return;
        }

        var commentFilter = OperationCommentFilter;
        if (commentFilter.Length > 256)
        {
            SetError(T("activityCommentTooLong"));
            return;
        }

        if (!_activityRefreshBudget.TryBegin(profile.Id, DateTimeOffset.UtcNow, out var remaining))
        {
            StatusMessage = F("activityRefreshWait", Math.Ceiling(remaining.TotalSeconds));
            return;
        }

        _operationsSnapshotProfile = null;
        _currentOperationFingerprints = [];
        KillOperationCommand.NotifyCanExecuteChanged();

        await RunAsync(async cancellationToken =>
        {
            string operations;
            CurrentActivitySnapshot snapshot;
            try
            {
                operations = await _workspace.GetCurrentOperationsAsync(profile, cancellationToken);
                snapshot = CurrentActivitySnapshot.Parse(operations);
            }
            catch (AdministrationReadException exception)
            {
                if (ReferenceEquals(SelectedProfile, profile))
                    AdministrationResults = exception.Failure == AdministrationReadFailure.PermissionDenied
                        ? T("activityPermissionDenied") : T("activityUnavailable");
                return;
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
            {
                if (ReferenceEquals(SelectedProfile, profile))
                    AdministrationResults = T("activityUnavailable");
                return;
            }

            if (!ReferenceEquals(SelectedProfile, profile)
                || !string.Equals(commentFilter, OperationCommentFilter, StringComparison.Ordinal))
            {
                return;
            }

            var fingerprints = ParseOperationFingerprints(operations);
            var filterTag = CurrentActivitySnapshot.CommentTag(commentFilter);
            var visibleIds = snapshot.Entries
                .Where(entry => (filterTag is null || entry.CommentTag == filterTag) && entry.OperationId is not null)
                .Select(entry => entry.OperationId!.Value)
                .ToHashSet();
            fingerprints = fingerprints.Where(pair => visibleIds.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            _currentOperationFingerprints = fingerprints;
            _operationsSnapshotProfile = profile;
            AdministrationResults = FormatCurrentActivity(snapshot, commentFilter, DateTimeOffset.UtcNow);
            StatusMessage = T("currentOperationsLoaded");
            KillOperationCommand.NotifyCanExecuteChanged();
        });
    }

    private static string FormatCurrentActivity(
        CurrentActivitySnapshot snapshot,
        string commentFilter,
        DateTimeOffset observedAt)
    {
        var filterTag = CurrentActivitySnapshot.CommentTag(commentFilter);
        var entries = filterTag is null ? snapshot.Entries
            : snapshot.Entries.Where(entry => entry.CommentTag == filterTag).ToArray();
        var lines = new List<string>
        {
            F("activitySourceTime", snapshot.Source, observedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.CurrentCulture)),
            F("activityEntryCount", entries.Count),
            T("activityScopeNote")
        };
        if (snapshot.LimitReached)
            lines.Add(T("activityTruncated"));
        if (filterTag is not null)
            lines.Add(F("activityCommentFilterTag", filterTag));
        if (entries.Count == 0)
            lines.Add(T("activityNoEntries"));

        foreach (var entry in entries)
        {
            lines.Add(string.Join(" | ", new[]
            {
                entry.Kind == "idleSession" ? T("activityIdleSession") : T("activityOperation"),
                entry.OperationId is null ? null : "opid=" + entry.OperationId.Value.ToString(CultureInfo.InvariantCulture),
                entry.Host is null ? null : "host=" + entry.Host,
                entry.Operation is null ? null : "op=" + entry.Operation,
                entry.Namespace is null ? null : "ns=" + entry.Namespace,
                entry.SecondsRunning is null ? null : "s=" + entry.SecondsRunning.Value.ToString(CultureInfo.InvariantCulture),
                entry.WaitingForLock is null ? null : "waitingForLock=" + entry.WaitingForLock.Value.ToString(),
                entry.Locks is null ? null : "locks=" + entry.Locks,
                entry.SessionTag is null ? null : "session#" + entry.SessionTag,
                entry.CommentTag is null ? null : "comment#" + entry.CommentTag
            }.Where(part => part is not null)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    [RelayCommand(CanExecute = nameof(CanLoadDatabaseStats))]
    private async Task LoadProfilerStatusAsync()
    {
        if (SelectedProfile is null || string.IsNullOrWhiteSpace(SelectedDatabase))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetProfilerStatusAsync(SelectedProfile, SelectedDatabase, cancellationToken);
            StatusMessage = T("profilerLoaded");
        });
    }

    [RelayCommand(CanExecute = nameof(HasSelectedProfile))]
    private async Task LoadTopologyAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            AdministrationResults = await _workspace.GetTopologyAsync(SelectedProfile, cancellationToken);
            StatusMessage = T("topologyLoaded");
        });
    }

    [RelayCommand(CanExecute = nameof(CanKillOperation))]
    private async Task KillOperationAsync()
    {
        var profile = SelectedProfile;
        if (profile is null || !CanKillOperation || !TryGetExpectedOperationFingerprint(OperationIdToKill, out var fingerprint))
        {
            return;
        }

        var request = new OperationKillRequest(OperationIdToKill, OperationKillConfirmation, fingerprint);
        if (!await RunAsync(cancellationToken => _workspace.KillOperationAsync(profile, request, cancellationToken)))
        {
            return;
        }

        var operationId = request.GetOperationId();
        _currentOperationFingerprints.Remove(operationId);
        OperationIdToKill = string.Empty;
        OperationKillConfirmation = string.Empty;
        var auditRecorded = await TryRecordOperationKillAuditAsync(profile, operationId);
        StatusMessage = auditRecorded
            ? F("killRequested", operationId)
            : F("auditNotRecorded", F("killRequested", operationId));
    }

    private bool TryGetExpectedOperationFingerprint(string operationIdText, out string fingerprint)
    {
        fingerprint = string.Empty;
        if (!long.TryParse(operationIdText?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var operationId)
            || operationId < 1)
        {
            return false;
        }

        return _currentOperationFingerprints.TryGetValue(operationId, out fingerprint!);
    }

    private static Dictionary<long, string> ParseOperationFingerprints(string operationsJson)
    {
        try
        {
            using var document = JsonDocument.Parse(operationsJson);
            if (document.RootElement.TryGetProperty("truncated", out var truncated)
                && truncated.ValueKind == JsonValueKind.True)
            {
                return [];
            }

            if (!document.RootElement.TryGetProperty("inprog", out var operations) || operations.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var result = new Dictionary<long, string>();
            var ambiguousIds = new HashSet<long>();
            foreach (var operation in operations.EnumerateArray())
            {
                if (!OperationKillRequest.TryCreateFingerprint(operation, out var fingerprint)
                    || !operation.TryGetProperty("opid", out var idValue))
                {
                    continue;
                }

                var idText = idValue.ValueKind == JsonValueKind.Object && idValue.TryGetProperty("$numberLong", out var longValue)
                    ? longValue.GetString()
                    : idValue.ValueKind == JsonValueKind.Object && idValue.TryGetProperty("$numberInt", out var intValue)
                        ? intValue.GetString()
                        : idValue.ValueKind == JsonValueKind.Number
                            ? idValue.GetRawText()
                            : idValue.ValueKind == JsonValueKind.String ? idValue.GetString() : null;
                if (!long.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out var operationId) || operationId < 1)
                {
                    continue;
                }

                // Duplicate IDs are ambiguous in this snapshot and must never be offered for interruption.
                if (ambiguousIds.Contains(operationId))
                {
                    continue;
                }

                if (!result.TryAdd(operationId, fingerprint))
                {
                    result.Remove(operationId);
                    ambiguousIds.Add(operationId);
                }
            }

            return result;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private async Task<bool> TryRecordOperationKillAuditAsync(ConnectionProfile profile, long operationId)
    {
        try
        {
            await _workspace.SaveAuditAsync(AuditEntry.Create(
                "operation.kill",
                profile.Id,
                "admin",
                null,
                F("killRequested", operationId)));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
