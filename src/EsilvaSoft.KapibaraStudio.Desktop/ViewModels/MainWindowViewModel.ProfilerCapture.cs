using System.Collections.ObjectModel;
using System.Globalization;
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
    private string _profilerCaptureDurationMinutes = "10";

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
    private async Task StartProfilerCaptureAsync()
    {
        if (!CanConfigureProfiler || SelectedProfile is not { } profile
            || _profilerConfigurationPreview is not { } preview)
        {
            SetError(T("profilerPreviewRequired"));
            return;
        }
        if (!int.TryParse(ProfilerCaptureDurationMinutes, NumberStyles.None,
                CultureInfo.InvariantCulture, out var minutes) || minutes is < 1 or > 60)
        {
            SetError(T("profilerCaptureDurationInvalid"));
            return;
        }
        var request = BuildProfilerRequest(preview.Database, preview.Level, preview.SlowMs,
            preview.SampleRate, preview.UseFilter, preview.FilterJson,
            ProfilerConfirmation, preview.StatusJson, preview.TopologyJson);
        ProfilerCaptureTicket? ticket = null;
        var submitted = false;
        var completed = await RunAsync(async cancellationToken =>
        {
            submitted = true;
            ticket = await _workspace.StartProfilerCaptureAsync(profile, request,
                TimeSpan.FromMinutes(minutes), cancellationToken);
        });
        await LoadProfilerCaptureRecoveryAsync();
        if (!completed)
        {
            if (submitted && PendingProfilerCaptures.Any(item => item.ProfileId == profile.Id
                    && item.Database == request.Database))
            {
                if (ReferenceEquals(SelectedProfile, profile))
                    StatusMessage = T("profilerCaptureEffectUncertain");
                await RecordProfilerAuditAsync(profile, request.Database, "profiler.capture.uncertain",
                    T("profilerCaptureEffectUncertain"));
            }
            return;
        }
        _profilerConfigurationPreview = null;
        ProfilerConfigurationPreviewText = string.Empty;
        SelectedProfilerCapture = PendingProfilerCaptures.FirstOrDefault(item => item.Id == ticket!.Id);
        if (ReferenceEquals(SelectedProfile, profile))
            StatusMessage = F("profilerCaptureStarted", request.Database, minutes);
        await RecordProfilerAuditAsync(profile, request.Database, "profiler.capture.start",
            F("profilerCaptureStarted", request.Database, minutes));
    }

    [RelayCommand]
    private async Task CollectProfilerCaptureAsync()
    {
        if (SelectedProfile is not { } profile || SelectedProfilerCapture is not { } ticket
            || profile.Id != ticket.ProfileId)
        {
            SetError(T("profilerCaptureSelect"));
            return;
        }
        ProfilerCapturePage? page = null;
        var completed = await RunAsync(async cancellationToken =>
        {
            page = await _workspace.CollectProfilerCaptureAsync(profile, ticket.Id, cancellationToken);
        });
        if (!completed || !ReferenceEquals(SelectedProfile, profile)
            || SelectedProfilerCapture?.Id != ticket.Id)
            return;
        ProfilerCaptureResults = F("profilerCaptureCount", page!.Entries.Count,
                page.Truncated ? T("yes") : T("no"))
            + Environment.NewLine + string.Join(Environment.NewLine, page.Entries.Select(item =>
                $"{item.TimestampUtc:u} | {item.Operation} | {item.Namespace} | {item.Milliseconds?.ToString(CultureInfo.InvariantCulture) ?? "?"} ms | {item.PlanSummary}"));
        await RecordProfilerAuditAsync(profile, ticket.Database, "profiler.capture.read",
            F("profilerCaptureCount", page.Entries.Count, page.Truncated ? T("yes") : T("no")));
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
        var submitted = false;
        var completed = await RunAsync(async cancellationToken =>
        {
            submitted = true;
            await _workspace.RestoreProfilerCaptureAsync(profile, ticket.Id,
                ProfilerConfirmation, cancellationToken);
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
}
