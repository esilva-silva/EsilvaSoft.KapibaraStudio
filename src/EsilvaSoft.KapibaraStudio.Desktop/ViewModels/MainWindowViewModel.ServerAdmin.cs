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
    private readonly ActivityRefreshBudget _activityRefreshBudget = new();

    [ObservableProperty]
    private string _operationCommentFilter = string.Empty;

    [ObservableProperty]
    private string _administrationResults = T("adminInitial");

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

            AdministrationResults = FormatCurrentActivity(snapshot, commentFilter, DateTimeOffset.UtcNow);
            StatusMessage = T("currentOperationsLoaded");
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

}
