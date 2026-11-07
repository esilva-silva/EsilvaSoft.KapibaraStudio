using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EsilvaSoft.KapibaraStudio.Core;

namespace EsilvaSoft.KapibaraStudio.Desktop.ViewModels;

public sealed partial class MainWindowViewModel
{
    public ObservableCollection<ProfilerCaptureTicket> PendingProfilerCaptures { get; } = [];

    [ObservableProperty]
    private ProfilerCaptureTicket? _selectedProfilerCapture;

    [ObservableProperty]
    private string _profilerConfirmation = string.Empty;

    [ObservableProperty]
    private string _profilerCaptureWarning = string.Empty;

    [ObservableProperty]
    private string _profilerCaptureResults = string.Empty;

    private async Task LoadProfilerCaptureRecoveryAsync()
    {
        try
        {
            var pending = await _workspace.GetPendingProfilerCapturesAsync();
            var selectedId = SelectedProfilerCapture?.Id;
            PendingProfilerCaptures.Clear();
            foreach (var ticket in pending)
                PendingProfilerCaptures.Add(ticket);
            SelectedProfilerCapture = PendingProfilerCaptures.FirstOrDefault(item => item.Id == selectedId)
                ?? PendingProfilerCaptures.FirstOrDefault();
            ProfilerCaptureWarning = pending.Count == 0 ? string.Empty
                : F("profilerRecoveryPending", pending.Count);
        }
        catch (Exception exception)
        {
            ProfilerCaptureWarning = F("profilerRecoveryReadFailed", exception.Message);
        }
    }

    [RelayCommand]
    private async Task RestoreProfilerCaptureAsync()
    {
        if (SelectedProfile is not { } profile || SelectedProfilerCapture is not { } ticket
            || profile.Id != ticket.ProfileId || ticket.Database != ProfilerConfirmation?.Trim())
        {
            SetError(T("profilerCaptureConfirmRestore"));
            return;
        }
        var confirmation = ProfilerConfirmation.Trim();
        var submitted = false;
        var completed = await RunAsync(async cancellationToken =>
        {
            submitted = true;
            await _workspace.RestoreProfilerCaptureAsync(profile, ticket.Id,
                confirmation, cancellationToken);
        });
        await LoadProfilerCaptureRecoveryAsync();
        if (!completed)
        {
            if (submitted && PendingProfilerCaptures.Any(item => item.Id == ticket.Id
                    && item.State == ProfilerCaptureState.Uncertain))
            {
                if (ReferenceEquals(SelectedProfile, profile))
                    StatusMessage = T("profilerCaptureEffectUncertain");
                await RecordProfilerAuditAsync(profile, ticket.Database, "profiler.capture.restore.uncertain",
                    T("profilerCaptureEffectUncertain"));
            }
            return;
        }
        ProfilerCaptureResults = string.Empty;
        if (ReferenceEquals(SelectedProfile, profile))
            StatusMessage = F("profilerCaptureRestored", ticket.Database);
        await RecordProfilerAuditAsync(profile, ticket.Database, "profiler.capture.restore",
            F("profilerCaptureRestored", ticket.Database));
    }
    private async Task RecordProfilerAuditAsync(ConnectionProfile profile, string database, string action, string summary)
    {
        try
        {
            await _workspace.SaveAuditAsync(AuditEntry.Create(action, profile.Id, database, null, summary));
        }
        catch (Exception exception)
        {
            if (ReferenceEquals(SelectedProfile, profile)
                && string.Equals(SelectedDatabase, database, StringComparison.Ordinal))
                StatusMessage = summary + " " + F("auditNotRecorded", exception.Message);
        }
    }

}
